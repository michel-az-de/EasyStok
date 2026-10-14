"""Teste do Mercado Pago falso (#1716): sobe fake_mp.py numa porta à parte e confere o contrato que a API usa.
Uso: python scripts/homologacao/teste_fake_mp.py   (sai com 0 se tudo passar)"""
import json, os, subprocess, sys, tempfile, time, urllib.request, urllib.error
fake = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "fake_mp.py"); B = "http://127.0.0.1:18746"
env = dict(os.environ, MP_PORTA="18746", MP_WEBHOOK="http://127.0.0.1:1/x", MP_LOG=os.path.join(tempfile.gettempdir(), "teste-fake-mp.jsonl"))
p = subprocess.Popen([sys.executable, fake], env=env); time.sleep(1.5)
def h(m, u, b=None, form=False):
    data = (b.encode() if form else json.dumps(b).encode()) if b is not None else None
    r = urllib.request.Request(B + u, data=data, method=m, headers={"content-type": "application/x-www-form-urlencoded" if form else "application/json"})
    try:
        x = urllib.request.urlopen(r, timeout=5); c = x.read().decode(); return x.status, (json.loads(c) if c[:1] in "[{" else c[:40])
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()[:60]
try:
    ok = []
    st, pref = h("POST", "/checkout/preferences", {"external_reference": "ped-1", "items": [{"title": "Lasanha", "unit_price": 38, "quantity": 1}, {"title": "Entrega", "unit_price": 12, "quantity": 1}], "expires": True, "expiration_date_to": "2099-01-01T00:00:00.000-03:00"})
    ok.append(("preferencia 201 com collector_id", st == 201 and pref["collector_id"] == 900001))
    st, pg = h("POST", "/_pagar", {"external_reference": "ped-1"}); pid = pg["payment_id"]
    ok.append(("pagar usa o total da preferencia", h("GET", f"/v1/payments/{pid}")[1]["transaction_amount"] == 50))
    ok.append(("lista de estornos vazia e lista", h("GET", f"/v1/payments/{pid}/refunds") == (200, [])))
    st, e1 = h("POST", f"/v1/payments/{pid}/refunds", {"amount": 12})
    ok.append(("estorno parcial devolve payment_id e data", st == 201 and e1["payment_id"] == pid and e1["amount"] == 12 and "date_created" in e1))
    pgto = h("GET", f"/v1/payments/{pid}")[1]
    ok.append(("parcial mantem approved e soma devolvido", pgto["status"] == "approved" and pgto["transaction_amount_refunded"] == 12))
    ok.append(("estorno acima do saldo e 400", h("POST", f"/v1/payments/{pid}/refunds", {"amount": 39})[0] == 400))
    st, e2 = h("POST", f"/v1/payments/{pid}/refunds", {})
    ok.append(("estorno do restante vira refunded", st == 201 and e2["amount"] == 38 and h("GET", f"/v1/payments/{pid}")[1]["status"] == "refunded"))
    ok.append(("lista traz os dois e consulta um", len(h("GET", f"/v1/payments/{pid}/refunds")[1]) == 2 and h("GET", f"/v1/payments/{pid}/refunds/{e1['id']}")[1]["amount"] == 12))
    ok.append(("estorno de pagamento inexistente e 404", h("POST", "/v1/payments/1/refunds", {})[0] == 404))
    ok.append(("pagina abre enquanto vale", h("GET", f"/pagar/{pref['id']}")[0] == 200))
    h("PUT", f"/checkout/preferences/{pref['id']}", {"expires": True, "expiration_date_to": "2020-01-01T00:00:00.000-03:00"})
    ok.append(("link vencido nao abre nem paga", h("GET", f"/pagar/{pref['id']}")[0] == 410 and h("POST", f"/pagar/{pref['id']}", "desfecho=pix", form=True)[0] == 410))
    st, pend = h("POST", "/_pagar", {"external_reference": "ped-2", "valor": 30, "status": "pending"})
    ok.append(("pagamento pendente muda para aprovado", h("POST", "/_mudar", {"payment_id": pend["payment_id"], "status": "approved"})[1]["status"] == "approved"))
    ok.append(("users/me devolve a conta", h("GET", "/users/me")[1]["id"] == 900001))
    for n, v in ok: print("ok  " if v else "FALHOU ", n)
    sys.exit(0 if all(v for _, v in ok) else 1)
finally:
    p.terminate()
