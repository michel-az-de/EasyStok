"""Graph API falsa da Meta para a homologação local: grava cada envio da API e devolve wamid;
depois reporta sent/delivered/read pelo webhook assinado (como a Meta faria).
Telefone terminado em 9999 -> erro 131026 (nao entregavel). Terminado em 8888 -> 131047 (fora da janela)."""
import hashlib, hmac, json, os, threading, time, urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

SEGREDO = open(os.path.expanduser("~/.easystok/meta-appsecret-teste-local")).read().strip().encode()
WEBHOOK = "http://127.0.0.1:18510/api/webhooks/whatsapp"
LOG = os.environ.get("META_LOG", "meta-envios.jsonl")
n = 0

def status(wamid, para, estado, atraso):
    time.sleep(atraso)
    corpo = {"object": "whatsapp_business_account", "entry": [{"id": "waba-homol", "changes": [{"field": "messages", "value": {
        "messaging_product": "whatsapp", "metadata": {"display_phone_number": "5511900000000", "phone_number_id": "homol-001"},
        "statuses": [{"id": wamid, "status": estado, "timestamp": str(int(time.time())), "recipient_id": para}]}}]}]}
    bruto = json.dumps(corpo).encode()
    sig = "sha256=" + hmac.new(SEGREDO, bruto, hashlib.sha256).hexdigest()
    req = urllib.request.Request(WEBHOOK, data=bruto, method="POST", headers={"content-type": "application/json", "X-Hub-Signature-256": sig})
    try: urllib.request.urlopen(req, timeout=10).read()
    except Exception as e: print("webhook status falhou", e, flush=True)

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
        reg = {"ts": time.strftime("%H:%M:%S"), "path": self.path, "to": para, "type": corpo.get("type"), "corpo": corpo}
        if para.endswith("9999") or para.endswith("8888"):
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
            if "status" not in corpo:
                for est, atr in (("sent", 1), ("delivered", 3), ("read", 7)):
                    threading.Thread(target=status, args=(wamid, para, est, atr), daemon=True).start()
        open(LOG, "a", encoding="utf-8").write(json.dumps(reg, ensure_ascii=False) + "\n")
        b = json.dumps(resp).encode()
        self.send_response(status_http); self.send_header("content-type", "application/json"); self.send_header("content-length", str(len(b))); self.end_headers(); self.wfile.write(b)
    def log_message(self, *a): pass

ThreadingHTTPServer(("0.0.0.0", 18620), H).serve_forever()
