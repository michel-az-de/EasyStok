"""Mercado Pago falso para a homologação (#1318). Imita checkout/preferences, v1/payments (consulta, busca, estorno).
Controle: POST /_pagar {"external_reference": "...", "valor": 43.4, "metodo": "pix"} cria o pagamento aprovado e dispara o webhook assinado
para a API. GET /_estado mostra preferências e pagamentos."""
import hashlib, hmac, json, os, threading, time, urllib.request, uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

SEGREDO = b"homol-mp-secret"
WEBHOOK = "http://127.0.0.1:18510/api/webhooks/mercadopago"
LOG = os.environ.get("MP_LOG", "mp-chamadas.jsonl")
PREFS, PAGTOS, N = {}, {}, [900000]


def log(**k):
    open(LOG, "a", encoding="utf-8").write(json.dumps({"ts": time.strftime("%H:%M:%S"), **k}, ensure_ascii=False) + "\n")


def webhook(pid):
    corpo = json.dumps({"action": "payment.updated", "api_version": "v1", "data": {"id": str(pid)}, "id": int(time.time()), "type": "payment"}).encode()
    ts = str(int(time.time() * 1000)); rid = str(uuid.uuid4())
    v1 = hmac.new(SEGREDO, f"id:{pid};request-id:{rid};ts:{ts};".encode(), hashlib.sha256).hexdigest()
    req = urllib.request.Request(WEBHOOK + f"?data.id={pid}&type=payment", data=corpo, method="POST",
                                 headers={"content-type": "application/json", "x-signature": f"ts={ts},v1={v1}", "x-request-id": rid})
    try:
        r = urllib.request.urlopen(req, timeout=15); log(webhook=pid, http=r.status)
    except Exception as e:
        log(webhook=pid, erro=str(e)[:200])


def pagamento(ref, valor, metodo, status="approved"):
    N[0] += 1; pid = N[0]
    tipo = {"pix": "bank_transfer", "credit_card": "credit_card"}.get(metodo, "credit_card")
    PAGTOS[str(pid)] = {"id": pid, "status": status, "status_detail": "accredited", "external_reference": ref, "transaction_amount": valor,
                        "date_approved": time.strftime("%Y-%m-%dT%H:%M:%S.000-03:00"), "payment_method_id": metodo, "payment_type_id": tipo}
    return pid


class H(BaseHTTPRequestHandler):
    def _corpo(self):
        if self.headers.get("transfer-encoding", "").lower() == "chunked":
            b = b""
            while True:
                t = int(self.rfile.readline().strip() or b"0", 16)
                if t == 0:
                    self.rfile.readline(); break
                b += self.rfile.read(t); self.rfile.readline()
            return b
        return self.rfile.read(int(self.headers.get("content-length", 0)))

    def _json(self, st, obj):
        b = json.dumps(obj).encode()
        self.send_response(st); self.send_header("content-type", "application/json"); self.send_header("content-length", str(len(b))); self.end_headers(); self.wfile.write(b)

    def do_POST(self):
        p = urlparse(self.path).path; raw = self._corpo(); d = json.loads(raw or b"{}")
        if p == "/checkout/preferences":
            pid = f"pref-{len(PREFS)+1}-{uuid.uuid4().hex[:6]}"
            ref = d.get("external_reference"); PREFS[pid] = d
            log(preferencia=pid, ref=ref, itens=[(i.get("title"), i.get("unit_price"), i.get("quantity")) for i in d.get("items", [])])
            return self._json(201, {"id": pid, "init_point": f"http://127.0.0.1:18630/pagar/{pid}"})
        if p == "/_pagar":
            ref = d["external_reference"]; valor = d.get("valor")
            if valor is None:
                valor = sum(i["unit_price"] * i["quantity"] for pr in PREFS.values() if pr.get("external_reference") == ref for i in pr["items"][:0]) or d.get("total", 0)
            pid = pagamento(ref, valor, d.get("metodo", "pix"), d.get("status", "approved"))
            log(pago=pid, ref=ref, valor=valor)
            threading.Thread(target=webhook, args=(pid,), daemon=True).start()
            return self._json(200, {"payment_id": pid})
        if p.startswith("/v1/payments/") and p.endswith("/refunds"):
            pid = p.split("/")[3]; log(estorno=pid, corpo=d)
            if pid in PAGTOS: PAGTOS[pid]["status"] = "refunded"
            return self._json(201, {"id": int(time.time()), "amount": d.get("amount", PAGTOS.get(pid, {}).get("transaction_amount")), "status": "approved"})
        self._json(404, {})

    def do_PUT(self):
        self._corpo(); log(put=self.path); self._json(200, {})

    def do_GET(self):
        u = urlparse(self.path); p = u.path
        if p.startswith("/v1/payments/search"):
            ref = parse_qs(u.query).get("external_reference", [""])[0]
            return self._json(200, {"results": [x for x in PAGTOS.values() if x["external_reference"] == ref]})
        if p.startswith("/v1/payments/"):
            x = PAGTOS.get(p.split("/")[3]); return self._json(200 if x else 404, x or {})
        if p == "/_estado":
            return self._json(200, {"preferencias": PREFS, "pagamentos": PAGTOS})
        self._json(200, {"ok": True})

    def log_message(self, *a): pass


ThreadingHTTPServer(("0.0.0.0", 18630), H).serve_forever()
