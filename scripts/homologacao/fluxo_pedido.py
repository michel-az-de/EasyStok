"""Fluxo de pedido por WhatsApp: pede, informa endereço, confirma, escolhe janela pelo botão. Uso: python fluxo_pedido.py <tel> <nome> <pedido> <endereco> [manha|tarde]"""
import json, sys, time
tel, nome, pedido, endereco = sys.argv[1:5]; turno = sys.argv[5] if len(sys.argv) > 5 else "tarde"
sys.argv = [sys.argv[0]]
import sim

sim.ESPERA = 90


def fala(texto):
    marco = sim.sql("select now()"); t0 = time.time()
    print(f"\nCLIENTE[{nome}]: {texto}")
    print("  webhook", sim.webhook(tel, nome, sim.msg_texto(tel, texto)))
    sim.esperar(marco, tel, t0, quiet=8)


def ultimo_interativo():
    linhas = [json.loads(l) for l in open("meta-envios.jsonl", encoding="utf-8") if '"interactive"' in l and tel in l]
    return linhas[-1]["corpo"]["interactive"] if linhas else None


fala(pedido)
fala(endereco)
fala("Confirmo o endereço, pode seguir.")
it = ultimo_interativo()
if it:
    botoes = it["action"]["buttons"]
    print("  botões:", [b["reply"]["title"] for b in botoes])
    escolha = next((b for b in botoes if (turno == "manha") == ("09:00" in b["reply"]["title"])), botoes[0])["reply"]
    marco = sim.sql("select now()"); t0 = time.time()
    m = {"from": tel, "id": f"wamid.btn.{int(time.time()*1000)}", "timestamp": str(int(time.time())), "type": "interactive",
         "interactive": {"type": "button_reply", "button_reply": {"id": escolha["id"], "title": escolha["title"]}}}
    print("CLIENTE toca:", escolha["title"], "webhook", sim.webhook(tel, nome, m))
    sim.esperar(marco, tel, t0, quiet=8)
else:
    print("  (nenhum botão de janela recebido)")
