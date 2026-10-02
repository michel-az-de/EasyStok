"""Graph API falsa da Meta para a homologação local: grava cada envio da API e devolve wamid;
depois reporta sent/delivered/read pelo webhook assinado (como a Meta faria).
Telefone terminado em 9999 -> erro 131026 (nao entregavel). Terminado em 8888 -> 131047 (fora da janela).
N6 (WhatsApp de plataforma): o phone_number_id vem do caminho da chamada; o do numero de plataforma (META_PLATAFORMA_PHONE_ID)
tem os status mandados a api/webhooks/whatsapp-plataforma, com o biz_opaque_callback_data devolvido. Template de
autenticacao (nome codigo_*) so passa com o mesmo codigo no corpo e no botao url indice "0", ate 15 caracteres; senao 132000.
Telefone terminado em 7777 -> a Meta aceita e responde depois do timeout do cliente, e o sent chega depois pelo webhook.
POST /__inject/categoria?nome=X&idioma=pt_BR&categoria=MARKETING injeta template_category_update no callback do app."""
import hashlib, hmac, json, os, threading, time, urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

SEGREDO = open(os.path.expanduser("~/.easystok/meta-appsecret-teste-local")).read().strip().encode()
WEBHOOK = "http://127.0.0.1:18510/api/webhooks/whatsapp"
WEBHOOK_PLATAFORMA = "http://127.0.0.1:18510/api/webhooks/whatsapp-plataforma"
PHONE_ID_PLATAFORMA = os.environ.get("META_PLATAFORMA_PHONE_ID", "7770000001")
ATRASO_ACEITA_E_DEMORA = float(os.environ.get("META_ATRASO_7777", "40"))
LOG = os.environ.get("META_LOG", "meta-envios.jsonl")
n = 0

def assinar_e_postar(url, corpo):
    bruto = json.dumps(corpo).encode()
    sig = "sha256=" + hmac.new(SEGREDO, bruto, hashlib.sha256).hexdigest()
    req = urllib.request.Request(url, data=bruto, method="POST", headers={"content-type": "application/json", "X-Hub-Signature-256": sig})
    try: urllib.request.urlopen(req, timeout=10).read()
    except Exception as e: print("webhook falhou", url, e, flush=True)

def status(wamid, para, estado, atraso, phone_id="homol-001", opaco=None):
    time.sleep(atraso)
    st = {"id": wamid, "status": estado, "timestamp": str(int(time.time())), "recipient_id": para}
    if opaco: st["biz_opaque_callback_data"] = opaco
    corpo = {"object": "whatsapp_business_account", "entry": [{"id": "waba-homol", "changes": [{"field": "messages", "value": {
        "messaging_product": "whatsapp", "metadata": {"display_phone_number": "5511900000000", "phone_number_id": phone_id},
        "statuses": [st]}}]}]}
    # N6: o numero de plataforma tem webhook proprio (override por numero); o da loja segue no callback do app.
    assinar_e_postar(WEBHOOK_PLATAFORMA if phone_id == PHONE_ID_PLATAFORMA else WEBHOOK, corpo)

def injetar_categoria(nome, idioma, categoria):
    """template_category_update chega sempre ao callback do app: a Meta nao deixa sobrescrever esse webhook."""
    corpo = {"object": "whatsapp_business_account", "entry": [{"id": "waba-homol", "changes": [{"field": "template_category_update", "value": {
        "message_template_id": 1, "message_template_name": nome, "message_template_language": idioma.replace("_", "-"),
        "previous_category": "UTILITY", "new_category": categoria}}]}]}
    assinar_e_postar(WEBHOOK, corpo)

def validar_template_autenticacao(corpo):
    """Copy code: o mesmo codigo no corpo e no botao url indice "0", ate 15 caracteres. None se valido, senao o motivo."""
    comps = corpo.get("template", {}).get("components", [])
    body = next((c for c in comps if c.get("type") == "body"), None)
    botao = next((c for c in comps if c.get("type") == "button" and c.get("sub_type") == "url" and str(c.get("index")) == "0"), None)
    if not body or not botao: return "faltam corpo ou botao url indice 0"
    cod_corpo = body["parameters"][0].get("text", "")
    cod_botao = botao["parameters"][0].get("text", "")
    if cod_corpo != cod_botao: return "codigo do corpo difere do botao"
    if len(cod_botao) > 15: return "codigo acima de 15 caracteres"
    return None

JPEG = bytes.fromhex("ffd8ffe000104a46494600010100000100010000ffdb004300080606070605080707070909080a0c140d0c0b0b0c1912130f141d1a1f1e1d1a1c1c20242e2720222c231c1c2837292c30313434341f27393d38323c2e333432ffc0000b080001000101011100ffc4001f0000010501010101010100000000000000000102030405060708090a0bffc400b5100002010303020403050504040000017d01020300041105122131410613516107227114328191a1082342b1c11552d1f02433627282090a161718191a25262728292a3435363738393a434445464748494a535455565758595a636465666768696a737475767778797a838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae1e2e3e4e5e6e7e8e9eaf1f2f3f4f5f6f7f8f9faffda0008010100003f00fbfcffd9")

class H(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path.startswith("/media/"):
            self.send_response(200); self.send_header("content-type", "image/jpeg"); self.send_header("content-length", str(len(JPEG))); self.end_headers(); self.wfile.write(JPEG); return
        mid = self.path.rsplit("/", 1)[-1].split("?")[0]
        b = json.dumps({"url": f"http://host.docker.internal:18620/media/{mid}", "mime_type": "image/jpeg", "id": mid}).encode()
        open(LOG, "a", encoding="utf-8").write(json.dumps({"ts": time.strftime("%H:%M:%S"), "get": self.path}) + "\n")
        self.send_response(200); self.send_header("content-type", "application/json"); self.send_header("content-length", str(len(b))); self.end_headers(); self.wfile.write(b)

    def do_POST(self):
        global n
        if self.path.startswith("/__inject/categoria"):
            q = dict(p.split("=", 1) for p in self.path.split("?", 1)[-1].split("&") if "=" in p)
            injetar_categoria(q.get("nome", ""), q.get("idioma", "pt_BR"), q.get("categoria", "MARKETING"))
            self.send_response(204); self.end_headers(); return
        if self.headers.get("transfer-encoding", "").lower() == "chunked":
            bruto = b""
            while True:
                tam = int(self.rfile.readline().strip() or b"0", 16)
                if tam == 0:
                    self.rfile.readline(); break
                bruto += self.rfile.read(tam); self.rfile.readline()
        else:
            bruto = self.rfile.read(int(self.headers.get("content-length", 0)))
        corpo = json.loads(bruto or b"{}")
        para = corpo.get("to", "")
        # Caminho: /{versao}/{phone_number_id}/messages
        partes = [x for x in self.path.split("?")[0].split("/") if x]
        phone_id = partes[-2] if len(partes) >= 2 and partes[-1] == "messages" else "homol-001"
        opaco = corpo.get("biz_opaque_callback_data")
        reg = {"ts": time.strftime("%H:%M:%S"), "path": self.path, "to": para, "type": corpo.get("type"), "corpo": corpo}
        nome_tpl = corpo.get("template", {}).get("name", "")
        invalido = validar_template_autenticacao(corpo) if nome_tpl.startswith("codigo_") else None
        if invalido:
            reg["resposta"] = "erro 132000"
            resp = {"error": {"message": "Number of parameters does not match: " + invalido, "type": "OAuthException", "code": 132000, "fbtrace_id": "FAKE"}}
            status_http = 400
        elif para.endswith("9999") or para.endswith("8888"):
            cod = 131026 if para.endswith("9999") else 131047
            reg["resposta"] = f"erro {cod}"
            resp = {"error": {"message": "Message undeliverable" if cod == 131026 else "Re-engagement message", "type": "OAuthException", "code": cod, "fbtrace_id": "FAKE"}}
            status_http = 400
        else:
            n += 1
            wamid = f"wamid.FAKE{int(time.time()*1000)}{n}"
            reg["resposta"] = wamid
            resp = {"messaging_product": "whatsapp", "contacts": [{"input": para, "wa_id": para}], "messages": [{"id": wamid}]}
            status_http = 200
            if para.endswith("7777"):
                # Aceita e demora mais que o timeout do cliente: o cliente nao sabe que saiu (Indeterminado) e o
                # sent chega depois pelo webhook.
                reg["resposta"] = wamid + " (demorou)"
                open(LOG, "a", encoding="utf-8").write(json.dumps(reg, ensure_ascii=False) + "\n")
                reg = None
                threading.Thread(target=status, args=(wamid, para, "sent", ATRASO_ACEITA_E_DEMORA + 2, phone_id, opaco), daemon=True).start()
                time.sleep(ATRASO_ACEITA_E_DEMORA)
            elif "status" not in corpo:
                for est, atr in (("sent", 1), ("delivered", 3), ("read", 7)):
                    threading.Thread(target=status, args=(wamid, para, est, atr, phone_id, opaco), daemon=True).start()
        if reg is not None:
            open(LOG, "a", encoding="utf-8").write(json.dumps(reg, ensure_ascii=False) + "\n")
        b = json.dumps(resp).encode()
        try:
            self.send_response(status_http); self.send_header("content-type", "application/json"); self.send_header("content-length", str(len(b))); self.end_headers(); self.wfile.write(b)
        except (BrokenPipeError, ConnectionResetError):
            pass  # o cliente ja desistiu (timeout do caso 7777)
    def log_message(self, *a): pass

ThreadingHTTPServer(("0.0.0.0", 18620), H).serve_forever()
