#!/usr/bin/env python3
"""Publicação OTA em duas fases. Nenhum modo remove releases ou pacotes anteriores; só o staging da própria publicação."""
import base64
import contextlib
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

APP = "com.casadababa.piloto"
BASE = "https://easystok.online/downloads/casa-da-baba/ota/"
FIELDS = {"schemaVersion", "appId", "channel", "sequence", "bundleVersion", "nativeVersionCode", "dataSchemaVersion", "url", "sizeBytes", "sha256"}
MAX_ZIP = 64 * 1024 * 1024

def require(condition, message):
    if not condition:
        raise ValueError(message)

def validate(payload):
    require(set(payload) == FIELDS, "Campos de manifesto invalidos.")
    require(payload["schemaVersion"] == 1 and payload["appId"] == APP and payload["channel"] == "piloto", "Identidade OTA invalida.")
    # A VPS só confere a forma: o aparelho recusa APK ou esquema diferente do seu, e o manifesto é assinado.
    require(all(type(payload[f]) is int and 0 < payload[f] <= 2147483647 for f in ("nativeVersionCode", "dataSchemaVersion")), "Versao nativa/esquema invalida.")
    require(type(payload["sequence"]) is int and 0 < payload["sequence"] <= 2147483647, "Sequencia invalida.")
    require(isinstance(payload["bundleVersion"], str) and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}", payload["bundleVersion"]), "bundleVersion invalida.")
    require(isinstance(payload["sha256"], str) and re.fullmatch(r"[a-f0-9]{64}", payload["sha256"]), "Hash invalido.")
    require(type(payload["sizeBytes"]) is int and 0 < payload["sizeBytes"] <= MAX_ZIP, "Tamanho invalido.")
    require(payload["url"] == BASE + payload["sha256"] + "/bundle.zip", "URL invalida.")
    return payload

def manifest(file, public):
    require(file.is_file() and not file.is_symlink() and file.stat().st_size <= 24576, "Manifesto ausente/invalido.")
    envelope = json.loads(file.read_text(encoding="utf-8"))
    require(set(envelope) == {"payloadBase64", "signatureBase64"}, "Envelope invalido.")
    payload = base64.b64decode(envelope["payloadBase64"], validate=True)
    signature = base64.b64decode(envelope["signatureBase64"], validate=True)
    require(len(payload) <= 12288 and len(signature) <= 1536, "Envelope excede limite.")
    with tempfile.TemporaryDirectory(prefix="ota-verify-") as temp:
        signature_file = Path(temp) / "signature"
        signature_file.write_bytes(signature)
        result = subprocess.run([os.environ.get("OPENSSL", "openssl"), "dgst", "-sha256", "-verify", str(public), "-signature", str(signature_file)],
                                input=payload, capture_output=True, timeout=15)
        require(result.returncode == 0, "Assinatura OTA invalida.")
    return validate(json.loads(payload))

def verify_zip(file, payload):
    require(file.is_file() and not file.is_symlink() and file.stat().st_size == payload["sizeBytes"], "Tamanho do ZIP divergente.")
    require(hashlib.sha256(file.read_bytes()).hexdigest() == payload["sha256"], "SHA256 do ZIP divergente.")
    with zipfile.ZipFile(file) as archive:
        entries = archive.infolist()
        require(0 < len(entries) <= 4096 and sum(e.file_size for e in entries) <= 128 * 1024 * 1024, "Limite expandido excedido.")
        names = set()
        for entry in entries:
            name = entry.filename
            parts = name.rstrip("/").split("/")
            require(name not in names and name and not name.startswith("/") and "\\" not in name and all(p not in ("", ".", "..") for p in parts), "Caminho ZIP invalido.")
            require(entry.file_size <= 32 * 1024 * 1024 and not entry.flag_bits & 1, "Entrada ZIP invalida.")
            require((entry.external_attr >> 16) & 0o170000 != 0o120000, "Symlink ZIP proibido.")
            names.add(name)
        require("index.html" in names and "bundle.json" in names and archive.testzip() is None, "ZIP incompleto/corrompido.")
        metadata = json.loads(archive.read("bundle.json"))
        for field in ("schemaVersion", "appId", "channel", "bundleVersion", "nativeVersionCode", "dataSchemaVersion"):
            require(metadata[field] == payload[field], "bundle.json diverge do manifesto: " + field)

def immutable(file, data):
    if file.exists():
        require(file.is_file() and not file.is_symlink() and file.read_bytes() == data, "Artefato imutavel divergente: " + file.name)
        return
    descriptor, temporary = tempfile.mkstemp(prefix=".ota-", dir=file.parent)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temporary, 0o644)
        os.link(temporary, file)  # Criação atômica sem substituir artefato concorrente.
    finally:
        os.unlink(temporary)

def replace(file, data):
    descriptor, temporary = tempfile.mkstemp(prefix=".ota-", dir=file.parent)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temporary, 0o644)
        os.replace(temporary, file)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)

@contextlib.contextmanager
def publication_lock(root):
    lock = root / ".publish-lock"
    lock.mkdir()  # Falha fechada se outra publicação estiver em andamento.
    try:
        yield
    finally:
        lock.rmdir()

def publish(root, staging, mode):
    root, staging = Path(root).absolute(), Path(staging).absolute()
    require(staging.parent == root and re.fullmatch(r"\.incoming-[a-f0-9]{32}", staging.name), "Staging fora do destino.")
    require(not root.is_symlink() and not staging.is_symlink(), "Destino nao pode ser symlink.")
    if mode == "stage":
        root.mkdir(parents=True, exist_ok=True)
        staging.mkdir(mode=0o700)
        return {"phase": mode}
    require(mode in ("install", "activate", "discard") and staging.is_dir(), "Fase/diretorio invalido.")
    with publication_lock(root):
        if mode == "discard":
            shutil.rmtree(staging)
            return {"phase": mode}
        releases = list(staging.glob("release-*.json"))
        require(len(releases) == 1, "Staging deve conter uma release.")
        staged_key, trusted_key = staging / "ota-public-key.der", root / "ota-public-key.der"
        require(staged_key.is_file() and not staged_key.is_symlink() and staged_key.stat().st_size < 4096, "Chave publica ausente/invalida.")
        if trusted_key.exists():
            require(trusted_key.read_bytes() == staged_key.read_bytes(), "A chave OTA publicada nao pode ser trocada.")
        payload = manifest(releases[0], staged_key)
        release_name = "release-" + str(payload["sequence"]) + ".json"
        require(releases[0].name == release_name, "Nome da release diverge da sequencia.")
        data = releases[0].read_bytes()
        # APKs antigos consultam o caminho global; versões novas têm ponteiros compatíveis próprios.
        channel_root = root / ("native-" + str(payload["nativeVersionCode"])) if payload["nativeVersionCode"] >= 5 else root
        require(not channel_root.is_symlink() and (not channel_root.exists() or channel_root.is_dir()), "Diretorio do canal invalido.")
        latest = channel_root / "latest.json"
        if latest.exists():
            require(trusted_key.exists(), "Manifesto sem chave confiavel.")
            current = manifest(latest, trusted_key)
            require(payload["sequence"] >= current["sequence"], "Replay/downgrade de sequencia recusado.")
            if payload["sequence"] == current["sequence"]:
                require(latest.read_bytes() == data, "Sequencia existente com outro conteudo.")
        bundle_dir = root / payload["sha256"]
        require(not bundle_dir.is_symlink(), "Diretorio do pacote invalido.")
        if mode == "install":
            verify_zip(staging / "bundle.zip", payload)
            version_record = json.dumps({key: payload[key] for key in ('bundleVersion', 'sha256', 'nativeVersionCode', 'dataSchemaVersion')}, sort_keys=True).encode('utf-8')
            immutable(root / ('bundle-version-' + payload['bundleVersion'] + '.json'), version_record)
            immutable(trusted_key, staged_key.read_bytes())
            bundle_dir.mkdir(exist_ok=True)
            immutable(bundle_dir / "bundle.zip", (staging / "bundle.zip").read_bytes())
            immutable(root / release_name, data)
        else:
            require(trusted_key.exists() and (root / release_name).read_bytes() == data, "Release nao instalada.")
            verify_zip(bundle_dir / "bundle.zip", payload)
            channel_root.mkdir(exist_ok=True)
            if latest.exists() and latest.read_bytes() != data:
                replace(channel_root / "previous.json", latest.read_bytes())
            replace(latest, data)
            shutil.rmtree(staging)  # Tudo já está nos caminhos imutáveis; o staging não fica servido.
        return {"phase": mode, "sequence": payload["sequence"], "sha256": payload["sha256"]}

if __name__ == "__main__":
    try:
        require(len(sys.argv) == 4, "Uso: ota-publish.py destino staging stage|install|activate|discard")
        print(json.dumps(publish(*sys.argv[1:])))
    except Exception as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
