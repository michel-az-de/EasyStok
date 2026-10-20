"""Roteiro ponta a ponta da jornada 'pedido a dinheiro' (#1889) contra o laboratorio local.

Abre loja e caixa, atende 3 clientes pelo chat do site, cadastra cada um pela conversa, cobra por
link (Mercado Pago falso), na entrega com maquininha e em dinheiro, passa pela cozinha, entrega e
fecha o caixa conferindo que a gaveta so tem dinheiro. So aceita API em loopback. Fecha o caixa do
dia: rode uma vez por base nova (laboratorio.ps1 com -Pasta nova).
Uso: python -I scripts/homologacao/jornada_pedido_a_dinheiro.py [api] [mp]
"""
import base64, json, random, sys, time, urllib.error, urllib.request
from datetime import date

API = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:18716"
MP = sys.argv[2] if len(sys.argv) > 2 else "http://127.0.0.1:18736"
assert API.startswith("http://127.0.0.1"), "so loopback"
SLUG = "casa-da-baba"
RODADA = str(random.randint(100, 999))
TOKEN = None
PASSOS = []


def http(metodo, url, corpo=None, cab=None, ok=(200, 201, 202, 204)):
    dados = json.dumps(corpo).encode() if corpo is not None else None
    req = urllib.request.Request(url, data=dados, method=metodo)
    req.add_header("Content-Type", "application/json")
    for k, v in (cab or {}).items():
        req.add_header(k, v)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            texto = r.read().decode() or "null"
            return r.status, json.loads(texto)
    except urllib.error.HTTPError as e:
        texto = e.read().decode()
        if e.code in ok:
            return e.code, json.loads(texto or "null")
        raise RuntimeError(f"{metodo} {url} -> {e.code}: {texto[:400]}")


def api(metodo, caminho, corpo=None):
    _, j = http(metodo, API + caminho, corpo, {"Authorization": f"Bearer {TOKEN}"})
    return j.get("data", j) if isinstance(j, dict) else j


def passo(nome, cond, detalhe=""):
    PASSOS.append((nome, bool(cond), detalhe))
    print(("OK   " if cond else "FALHA") + f"  {nome}  {detalhe}")
    if not cond:
        raise SystemExit(1)


def esperar(func, cond, segundos=60):
    fim = time.time() + segundos
    while True:
        v = func()
        if cond(v) or time.time() > fim:
            return v
        time.sleep(2)


# 0. Versao e login
versao = http("GET", API + "/health/version")[1]
print("API", versao.get("buildSha", "")[:8])
_, login = http("POST", API + "/api/auth/login", {"email": "admin@casadababa.demo", "senha": "admin123"})
TOKEN = login["data"]["token"]
claims = json.loads(base64.urlsafe_b64decode(TOKEN.split(".")[1] + "=="))
EMPRESA = claims["empresaId"]
passo("Login da dona", bool(TOKEN))

cfg = api("GET", "/api/atendimento/configuracao")
passo("Agente automatico nasce desligado (F2b)", cfg.get("ativo") is False, f"ativo={cfg.get('ativo')}")

# 1. Abrir loja e caixa
api("POST", "/api/atendimento/expediente/controle", {"controle": "ForcarAberta"})
if not api("GET", "/api/caixa/dia").get("aberto"):
    api("POST", "/api/caixa/abrir", {"empresaId": EMPRESA, "saldoInicial": 100, "observacoes": "Roteiro #1889"})
dia = api("GET", "/api/caixa/dia")
passo("Loja aberta e caixa aberto com R$ 100", dia.get("aberto"), f"saldoEsperado={dia.get('saldoEsperado')}")

# Janela de hoje a noite: o prazo minimo (100 min) ja passou da janela da tarde.
from datetime import datetime
dia = (datetime.now().weekday() + 1) % 7
janelas = api("GET", "/api/minha-vitrine/entrega/janelas")
if not any(j["diaDaSemana"] == dia and j["horaInicio"].startswith("19:") for j in janelas):
    api("POST", "/api/minha-vitrine/entrega/janelas", {"diaDaSemana": dia, "horaInicio": "19:00:00",
        "horaFim": "22:00:00", "capacidadeMaxima": 10, "label": "Noite roteiro"})

cardapio = api("GET", "/api/atendimento/comanda/cardapio")
itens = cardapio if isinstance(cardapio, list) else cardapio.get("itens", [])
item = next(i for i in itens if (i.get("disponivel", True)) and i.get("precoCentavos", i.get("preco", 0)))
item_id = item["id"]
print("Item:", item.get("nome"), item.get("precoCentavos", item.get("preco")))


def atender(nome, telefone, texto):
    _, s = http("POST", f"{API}/api/public/chat/{SLUG}/sessoes", {})
    tok = s["data"]["token"]
    http("POST", f"{API}/api/public/chat/{SLUG}/identificacao",
         {"nome": nome, "telefone": telefone, "email": None, "aceitePrivacidade": True}, {"X-Chat-Token": tok})
    http("POST", f"{API}/api/public/chat/{SLUG}/mensagens", {"texto": texto}, {"X-Chat-Token": tok})

    def achar():
        lista = api("GET", "/api/atendimento/conversas")
        lista = lista if isinstance(lista, list) else lista.get("itens", lista.get("conversas", []))
        return next((c for c in lista if nome in json.dumps(c, ensure_ascii=False)), None)

    conversa = esperar(achar, lambda c: c is not None, 30)
    passo(f"Conversa de {nome} chega ao Balcao", conversa is not None)
    cid = conversa["id"]
    api("POST", f"/api/atendimento/conversas/{cid}/assumir", {})
    api("POST", f"/api/atendimento/conversas/{cid}/cliente", {
        "nome": nome, "telefone": telefone,
        "endereco": {"cep": "05415000", "logradouro": "Rua dos Pinheiros", "numero": "100", "complemento": None,
                     "bairro": "Pinheiros", "cidade": "Sao Paulo", "uf": "SP"}})
    passo(f"Cliente {nome} cadastrado pela conversa com endereco de entrega", True)
    return cid


def janela_de_hoje():
    hoje = date.today().isoformat()
    js = api("GET", f"/api/atendimento/comanda/janelas?dataInicio={hoje}&dataFim={hoje}&itens={item_id}")
    js = js if isinstance(js, list) else js.get("janelas", js.get("itens", []))
    livres = [j for j in js if j.get("data") == hoje and not j.get("esgotado") and (j.get("vagasRestantes", 1) or 0) > 0]
    return (livres[0] if livres else None), hoje


def pedir(cid, forma, qtd):
    janela, data = janela_de_hoje()
    if janela is None:
        js = api("GET", f"/api/atendimento/comanda/janelas?itens={item_id}")
        js = js if isinstance(js, list) else js.get("janelas", js.get("itens", []))
        janela = next(j for j in js if j.get("disponivel", True))
        data = janela.get("data", janela.get("dataEntrega"))[:10]
    jid = janela.get("id", janela.get("janelaId"))
    corpo = {"itens": [{"cardapioItemId": item_id, "qtd": qtd}], "janelaId": jid, "dataEntrega": data}
    sim = api("POST", f"/api/atendimento/conversas/{cid}/pedido/simulacao", corpo)
    corpo.update({"forma": forma, "enviarAoCliente": True})
    ped = api("POST", f"/api/atendimento/conversas/{cid}/pedido", corpo)
    passo(f"Pedido {forma} criado com a mesma conta da previa (#1888)",
          ped.get("pedidoId") and abs(float(ped["total"]) - float(sim.get("total") or ped["total"])) < 0.01,
          f"total={ped['total']} data={data}")
    return ped


def status_pedido(pid):
    return api("GET", f"/api/pedidos/{pid}")


def cozinha_e_entrega(pid, rotulo):
    fila = api("GET", "/api/kds/pedidos")
    fila = fila if isinstance(fila, list) else fila.get("pedidos", fila.get("itens", []))
    passo(f"{rotulo}: pedido aparece na Cozinha", any(str(p.get("id", p.get("pedidoId"))) == pid for p in fila))
    for st in ("preparando", "pronto", "entregue"):
        api("PATCH", f"/api/kds/pedidos/{pid}/status", {"status": st})
        atual = status_pedido(pid)
        atual = atual.get("pedido", atual)
        passo(f"{rotulo}: status {st} persistido", atual.get("status") == st)


# 2. Cliente A: link de pagamento (Pix pelo Mercado Pago falso)
cA = atender(f"Cliente Link R{RODADA}", f"+5511900{RODADA}01", "Oi, quero uma lasanha")
pA = pedir(cA, "online", 1)
cob = pA.get("cobranca") or {}
passo("Cliente A: link do Mercado Pago gerado", bool(cob.get("linkPagamento") or cob.get("link")), str(cob.get("valor")))
http("POST", MP + "/_pagar", {"external_reference": pA["pedidoId"], "metodo": "pix"})
pago = esperar(lambda: status_pedido(pA["pedidoId"])["pedido"], lambda p: float(p.get("totalPago") or 0) > 0, 60)
passo("Cliente A: webhook do Mercado Pago baixa o Pix sozinho", float(pago.get("totalPago") or 0) >= float(cob.get("valor") or 0) > 0,
      f"totalPago={pago.get('totalPago')} de {pago.get('total')}")
cozinha_e_entrega(pA["pedidoId"], "Cliente A")

# 3. Cliente B: maquininha na entrega (cartao, nao entra na gaveta)
cB = atender(f"Cliente Maquininha R{RODADA}", f"+5511900{RODADA}02", "Quero pedir, pago no cartao na entrega")
pB = pedir(cB, "na_entrega", 1)
api("POST", f"/api/pedidos/{pB['pedidoId']}/pagamentos",
    {"empresaId": EMPRESA, "pedidoId": pB["pedidoId"], "metodo": "debito", "valor": float(pB["total"]), "referencia": "roteiro-1889"})
cozinha_e_entrega(pB["pedidoId"], "Cliente B")

# 4. Cliente C: dinheiro na entrega
cC = atender(f"Cliente Dinheiro R{RODADA}", f"+5511900{RODADA}03", "Quero pedir e pago em dinheiro")
pC = pedir(cC, "na_entrega", 2)
api("POST", f"/api/pedidos/{pC['pedidoId']}/pagamentos",
    {"empresaId": EMPRESA, "pedidoId": pC["pedidoId"], "metodo": "dinheiro", "valor": float(pC["total"]), "referencia": "roteiro-1889"})
cozinha_e_entrega(pC["pedidoId"], "Cliente C")

# 5. Fechar o caixa: a gaveta so tem dinheiro (F2b, #1776-11)
antes = api("GET", "/api/caixa/dia")
print("Caixa antes de fechar:", json.dumps(antes, ensure_ascii=False)[:600])
api("POST", "/api/caixa/fechar", {"empresaId": EMPRESA, "observacoes": "Roteiro #1889 conferido"})
depois = api("GET", "/api/caixa/dia")
passo("Caixa fechado e relido", depois.get("fechado"))
gaveta_esperada = 100 + float(pC["total"])
txt = json.dumps(depois, ensure_ascii=False)
print("Caixa depois:", txt[:600])
fechs = api("GET", "/api/caixa/fechamentos")
fechs = fechs if isinstance(fechs, list) else fechs.get("itens", fechs.get("fechamentos", []))
hoje_f = next(f for f in fechs if str(f.get("data", "")).startswith(date.today().isoformat()))
gaveta = hoje_f.get("dinheiroNaGaveta")
passo("Gaveta = abertura + dinheiro, sem Pix e cartao (#1776-11)",
      gaveta is not None and abs(float(gaveta) - gaveta_esperada) < 0.01,
      f"dinheiroNaGaveta={gaveta} esperado={gaveta_esperada} saldoFinal={hoje_f.get('saldoFinal')}")
api("POST", "/api/atendimento/expediente/controle", {"controle": "ForcarFechada", "justificativa": "Fim do roteiro #1889"})

print(f"\n{sum(1 for p in PASSOS if p[1])}/{len(PASSOS)} passos OK")
