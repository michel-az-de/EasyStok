"""Mercado Pago falso para a homologação (#1318). Imita checkout/preferences, v1/payments (consulta, busca, estorno)
e users/me. O link devolvido (init_point) abre uma página de checkout simulado: quem clica escolhe Pix aprovado,
cartão aprovado ou cartão recusado, o pagamento nasce com esse status, o webhook assinado vai para a API e o
navegador volta pela back_url da preferência, como no Checkout Pro (#1716).
Controle por HTTP: POST /_pagar {"external_reference": "...", "valor": 43.4, "metodo": "pix"} faz o mesmo sem tela.
POST /_mudar {"payment_id": 900001, "status": "charged_back"} muda um pagamento já criado (Pix pendente que aprova,
contestação) e reenvia o webhook. GET /_estado mostra preferências, pagamentos e estornos.
Como no provedor: o link vencido ou substituído (expiration_date_to, PUT da preferência) não aceita pagamento, o estorno
devolve payment_id e a lista de estornos, e estorno parcial mantém o pagamento aprovado com o valor devolvido somado.
Ambiente: MP_PORTA (18630), MP_WEBHOOK (API local), MP_URL_PUBLICA (como o navegador alcança este servidor)."""
import hashlib, hmac, html, itertools, json, os, threading, time, urllib.request, uuid
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlencode, urlparse, parse_qs

SEGREDO = os.environ.get("MP_SEGREDO", "homol-mp-secret").encode()
PORTA = int(os.environ.get("MP_PORTA", "18630"))
WEBHOOK = os.environ.get("MP_WEBHOOK", "http://127.0.0.1:18510/api/webhooks/mercadopago")
URL_PUBLICA = os.environ.get("MP_URL_PUBLICA", f"http://127.0.0.1:{PORTA}").rstrip("/")
LOG = os.environ.get("MP_LOG", "mp-chamadas.jsonl")
CONTA = 900001  # conta recebedora: a mesma em users/me, na preferência e no pagamento, como a API confere (I3.2)
PREFS, PAGTOS, ESTORNOS, N = {}, {}, {}, [900000]
TRAVA = threading.Lock()  # o servidor atende em threads: id e saldo devolvido mudam sob trava
EVENTO = itertools.count(int(time.time()) * 1000)  # id da notificação: único, o da API é a chave de repetição

# Desfechos da página: rótulo, método e status do contrato de v1/payments, e a back_url que o Checkout Pro usaria.
DESFECHOS = {
    "pix": ("Pagar com Pix", "pix", "approved", "accredited", "success"),
    "cartao": ("Pagar com cartão", "credit_card", "approved", "accredited", "success"),
    "recusado": ("Cartão recusado", "credit_card", "rejected", "cc_rejected_other_reason", "failure"),
}


def log(**k):
    open(LOG, "a", encoding="utf-8").write(json.dumps({"ts": time.strftime("%H:%M:%S"), **k}, ensure_ascii=False) + "\n")


def webhook(pid):
    corpo = json.dumps({"action": "payment.updated", "api_version": "v1", "data": {"id": str(pid)}, "id": next(EVENTO), "type": "payment"}).encode()
    ts = str(int(time.time() * 1000)); rid = str(uuid.uuid4())
    v1 = hmac.new(SEGREDO, f"id:{pid};request-id:{rid};ts:{ts};".encode(), hashlib.sha256).hexdigest()
    req = urllib.request.Request(WEBHOOK + f"?data.id={pid}&type=payment", data=corpo, method="POST",
                                 headers={"content-type": "application/json", "x-signature": f"ts={ts},v1={v1}", "x-request-id": rid})
    try:
        r = urllib.request.urlopen(req, timeout=15); log(webhook=pid, http=r.status)
    except Exception as e:
        log(webhook=pid, erro=str(e)[:200])


def vencida(pref):
    """Preferência com expires e expiration_date_to no passado, como o Checkout Pro recusa."""
    if not pref.get("expires") or not pref.get("expiration_date_to"):
        return False
    try:
        fim = datetime.fromisoformat(pref["expiration_date_to"])
        return (fim if fim.tzinfo else fim.replace(tzinfo=timezone.utc)) <= datetime.now(timezone.utc)
    except ValueError:
        return False


def estornar(pid, valor):
    """Contrato de POST /v1/payments/{id}/refunds: devolve payment_id e data; parcial mantém o pagamento aprovado."""
    pg = PAGTOS.get(pid)
    if pg is None:
        return 404, {"message": "Payment not found", "status": 404}
    devolvido = float(pg.get("transaction_amount_refunded") or 0)
    valor = round(float(valor if valor is not None else pg["transaction_amount"] - devolvido), 2)
    if pg["status"] not in ("approved", "refunded") or valor <= 0 or round(devolvido + valor, 2) > float(pg["transaction_amount"]):
        return 400, {"message": "Amount attempting to refund is greater than payment amount or payment is not refundable", "status": 400}
    N[0] += 1
    item = {"id": N[0], "payment_id": pg["id"], "amount": valor, "status": "approved",
            "date_created": time.strftime("%Y-%m-%dT%H:%M:%S.000-03:00")}
    ESTORNOS.setdefault(pid, []).append(item)
    pg["transaction_amount_refunded"] = round(devolvido + valor, 2)
    if pg["transaction_amount_refunded"] >= float(pg["transaction_amount"]):
        pg["status"] = "refunded"; pg["status_detail"] = "refunded"
    return 201, item


def total(pref):
    return round(sum(float(i.get("unit_price", 0)) * int(i.get("quantity", 1)) for i in pref.get("items", [])), 2)


def pagamento(ref, valor, metodo, status="approved", detalhe="accredited"):
    with TRAVA:
        N[0] += 1; pid = N[0]
    tipo = {"pix": "bank_transfer", "credit_card": "credit_card"}.get(metodo, "credit_card")
    PAGTOS[str(pid)] = {"id": pid, "status": status, "status_detail": detalhe, "external_reference": ref, "transaction_amount": valor,
                        "date_approved": time.strftime("%Y-%m-%dT%H:%M:%S.000-03:00") if status == "approved" else None,
                        "payment_method_id": metodo, "payment_type_id": tipo, "collector_id": CONTA, "transaction_amount_refunded": 0}
    return pid


def reais(v):
    return "R$ " + f"{v:,.2f}".replace(",", "X").replace(".", ",").replace("X", ".")


def pagina(pid, pref, aviso=""):
    itens = "".join(f"<li><span>{int(i.get('quantity', 1))}x {html.escape(str(i.get('title', '')))}</span>"
                    f"<span>{reais(float(i.get('unit_price', 0)) * int(i.get('quantity', 1)))}</span></li>" for i in pref.get("items", []))
    botoes = "".join(f'<button name="desfecho" value="{k}" class="{k}">{v[0]}</button>' for k, v in DESFECHOS.items())
    return f"""<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Pagamento simulado</title><style>
body{{font:17px/1.45 system-ui,sans-serif;background:#f3f1ec;color:#1c1b19;margin:0;padding:16px}}
main{{max-width:440px;margin:24px auto;background:#fff;border-radius:14px;padding:22px;box-shadow:0 1px 4px #0002}}
.selo{{display:inline-block;background:#fff3c4;color:#5c4400;border-radius:6px;padding:3px 9px;font-size:14px;font-weight:600}}
h1{{font-size:22px;margin:12px 0 4px}} ul{{list-style:none;padding:0;margin:14px 0;border-top:1px solid #e6e2d9}}
li{{display:flex;justify-content:space-between;gap:12px;padding:9px 0;border-bottom:1px solid #e6e2d9}}
li span:last-child,.total span:last-child{{white-space:nowrap}}
.total{{display:flex;justify-content:space-between;font-size:20px;font-weight:700;margin:14px 0 18px}}
button{{display:block;width:100%;font:inherit;font-weight:600;padding:14px;border-radius:10px;border:0;margin-top:10px;cursor:pointer}}
.pix{{background:#0a7a52;color:#fff}} .cartao{{background:#1c4fd8;color:#fff}} .recusado{{background:#fff;color:#a3261a;border:2px solid #a3261a}}
p{{margin:6px 0;color:#55524b}} .aviso{{color:#1c1b19;font-weight:600}}</style></head><body><main>
<span class="selo">SIMULAÇÃO</span><h1>Pagamento simulado</h1>
<p>Nenhum dinheiro sai daqui. Escolha o que o cliente faria no Mercado Pago.</p>
{f'<p class="aviso">{html.escape(aviso)}</p>' if aviso else ''}
<ul>{itens}</ul><div class="total"><span>Total</span><span>{reais(total(pref))}</span></div>
<form method="post" action="/pagar/{pid}">{botoes}</form></main></body></html>"""


VENCIDO = """<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Link vencido</title></head><body style="font:17px/1.45 system-ui,sans-serif;background:#f3f1ec;padding:24px">
<h1 style="font-size:22px">Este link de pagamento venceu</h1><p>Simulação: o Mercado Pago não aceita mais este link. Peça um novo à Casa.</p></body></html>"""


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

    def _html(self, st, texto):
        b = texto.encode("utf-8")
        self.send_response(st); self.send_header("content-type", "text/html; charset=utf-8"); self.send_header("content-length", str(len(b))); self.end_headers(); self.wfile.write(b)

    def _pagar_pela_tela(self, pid, raw):
        pref = PREFS.get(pid)
        if pref is None:
            return self._html(404, "<p>Link de pagamento simulado não encontrado.</p>")
        if vencida(pref):
            return self._html(410, VENCIDO)
        _, metodo, status, detalhe, volta = DESFECHOS.get(parse_qs(raw.decode()).get("desfecho", ["pix"])[0], DESFECHOS["pix"])
        ref = pref.get("external_reference")
        pg = pagamento(ref, total(pref), metodo, status, detalhe)
        log(pago=pg, ref=ref, valor=total(pref), status=status, origem="tela")
        threading.Thread(target=webhook, args=(pg,), daemon=True).start()
        destino = (pref.get("back_urls") or {}).get(volta)
        if not destino:
            texto = "Pagamento aprovado. Pode fechar esta página." if status == "approved" else "Pagamento recusado. Volte e tente outra forma."
            return self._html(200, pagina(pid, pref, texto))
        q = urlencode({"collection_id": pg, "collection_status": status, "payment_id": pg, "status": status,
                       "external_reference": ref or "", "preference_id": pid, "payment_type": metodo})
        self.send_response(303); self.send_header("location", destino + ("&" if "?" in destino else "?") + q); self.end_headers()

    def do_POST(self):
        p = urlparse(self.path).path; raw = self._corpo()
        if p.startswith("/pagar/"):
            return self._pagar_pela_tela(p.split("/")[2], raw)
        d = json.loads(raw or b"{}")
        if p == "/checkout/preferences":
            pid = f"pref-{len(PREFS)+1}-{uuid.uuid4().hex[:6]}"
            ref = d.get("external_reference"); PREFS[pid] = d
            log(preferencia=pid, ref=ref, itens=[(i.get("title"), i.get("unit_price"), i.get("quantity")) for i in d.get("items", [])])
            link = f"{URL_PUBLICA}/pagar/{pid}"
            return self._json(201, {"id": pid, "init_point": link, "sandbox_init_point": link, "external_reference": ref, "collector_id": CONTA})
        if p == "/_pagar":
            ref = d["external_reference"]; valor = d.get("valor")
            if valor is None:
                valor = next((total(pr) for pr in reversed(list(PREFS.values())) if pr.get("external_reference") == ref), d.get("total", 0))
            pid = pagamento(ref, valor, d.get("metodo", "pix"), d.get("status", "approved"))
            log(pago=pid, ref=ref, valor=valor)
            threading.Thread(target=webhook, args=(pid,), daemon=True).start()
            return self._json(200, {"payment_id": pid})
        if p == "/_mudar":
            pg = PAGTOS.get(str(d["payment_id"]))
            if pg is None:
                return self._json(404, {})
            pg["status"] = d["status"]; pg["status_detail"] = d.get("status_detail", d["status"])
            if d["status"] == "approved" and not pg.get("date_approved"):
                pg["date_approved"] = time.strftime("%Y-%m-%dT%H:%M:%S.000-03:00")
            log(mudou=pg["id"], status=d["status"])
            threading.Thread(target=webhook, args=(pg["id"],), daemon=True).start()
            return self._json(200, pg)
        if p.startswith("/v1/payments/") and p.endswith("/refunds"):
            pid = p.split("/")[3]
            with TRAVA:
                st, corpo = estornar(pid, d.get("amount"))
            log(estorno=pid, corpo=d, http=st)
            return self._json(st, corpo)
        self._json(404, {})

    def do_PUT(self):
        p = urlparse(self.path).path; d = json.loads(self._corpo() or b"{}"); log(put=p, corpo=d)
        pid = p.rsplit("/", 1)[-1]
        if p.startswith("/checkout/preferences/") and pid in PREFS:
            PREFS[pid].update(d); return self._json(200, {"id": pid, **PREFS[pid]})
        self._json(404 if p.startswith("/checkout/preferences/") else 200, {})

    def do_GET(self):
        u = urlparse(self.path); p = u.path
        if p.startswith("/pagar/"):
            pid = p.split("/")[2]; pref = PREFS.get(pid)
            if pref is None:
                return self._html(404, "<p>Link de pagamento simulado não encontrado.</p>")
            return self._html(410, VENCIDO) if vencida(pref) else self._html(200, pagina(pid, pref))
        if p == "/users/me":
            return self._json(200, {"id": CONTA, "nickname": "LOJA_SIMULADA", "site_id": "MLB"})
        if p.startswith("/v1/payments/search"):
            ref = parse_qs(u.query).get("external_reference", [""])[0]
            return self._json(200, {"results": [x for x in PAGTOS.values() if x["external_reference"] == ref]})
        partes = p.split("/")
        if p.startswith("/v1/payments/") and len(partes) > 4 and partes[4] == "refunds":
            lista = ESTORNOS.get(partes[3], [])
            if len(partes) == 5:
                return self._json(200 if partes[3] in PAGTOS else 404, lista if partes[3] in PAGTOS else {})
            x = next((e for e in lista if str(e["id"]) == partes[5]), None); return self._json(200 if x else 404, x or {})
        if p.startswith("/v1/payments/"):
            x = PAGTOS.get(partes[3]); return self._json(200 if x else 404, x or {})
        if p == "/_estado":
            return self._json(200, {"preferencias": PREFS, "pagamentos": PAGTOS, "estornos": ESTORNOS})
        self._json(200, {"ok": True})

    def log_message(self, *a): pass


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", PORTA), H).serve_forever()
