"""Teste do núcleo do eval (#3275), sem Docker nem chave. Uso: python -I scripts/homologacao/teste_eval_agente.py (sai com 0 se passar)"""
import os, sys

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(AQUI, "eval"))
import nucleo

FERRAMENTAS_DO_AGENTE = {"criar_pedido", "confirmar_encomenda", "enviar_link_pagamento", "consultar_cardapio", "escalar_para_dona"}


def turno(desfecho=1, motivo=None, ferramentas=(), entrada=100, cache=0):
    return {"desfecho": desfecho, "motivo": motivo, "tokens_entrada": entrada, "tokens_saida": 10, "tokens_cache_lido": cache,
            "ferramentas": [{"nome": n, "ok": ok} for n, ok in ferramentas]}


def regras(resultados):
    return {r["regra"]: r["ok"] for r in resultados}


falhas = []


def confere(nome, condicao):
    if not condicao:
        falhas.append(nome)


# Escalou quando devia: passa. Respondeu quando devia escalar: falha só a regra do desfecho.
caso = {"id": "x", "falas": ["oi"], "espera": {"desfecho_final": "Escalou"}}
confere("escalou passa", all(r["ok"] for r in nucleo.avaliar_rodada(caso, [turno(2)], [])))
confere("respondeu reprova", regras(nucleo.avaliar_rodada(caso, [turno(1)], []))["desfecho_final:Escalou"] is False)

# Falha técnica nunca passa, mesmo com o desfecho certo.
confere("falha_llm reprova", regras(nucleo.avaliar_rodada(caso, [turno(2, "falha_llm")], []))["sem_falha_tecnica"] is False)
confere("falha_llm_definitiva reprova", regras(nucleo.avaliar_rodada(caso, [turno(2, "falha_llm_definitiva")], []))["sem_falha_tecnica"] is False)
confere("teto_saida reprova", regras(nucleo.avaliar_rodada(caso, [turno(2, "teto_saida")], []))["sem_falha_tecnica"] is False)
confere("prazo_turno reprova", regras(nucleo.avaliar_rodada(caso, [turno(2, "prazo_turno")], []))["sem_falha_tecnica"] is False)
confere("recusa_modelo reprova", regras(nucleo.avaliar_rodada(caso, [turno(2, "recusa_modelo")], []))["sem_falha_tecnica"] is False)
confere("erro reprova", regras(nucleo.avaliar_rodada(caso, [turno(4)], []))["sem_falha_tecnica"] is False)
confere("escalada do modelo não é falha", regras(nucleo.avaliar_rodada(caso, [turno(2, "escalada_modelo")], []))["sem_falha_tecnica"] is True)
confere("sem turno reprova", regras(nucleo.avaliar_rodada(caso, [], []))["houve_turno"] is False)

# Ferramentas: chamada precisa ter dado ok; proibida vale mesmo que tenha falhado.
caso_f = {"id": "f", "falas": ["oi"], "espera": {"ferramentas_chamadas": ["consultar_cardapio"], "ferramentas_proibidas": ["criar_pedido"]}}
confere("chamada ok passa", regras(nucleo.avaliar_rodada(caso_f, [turno(1, ferramentas=[("consultar_cardapio", True)])], []))["chamou:consultar_cardapio"])
confere("chamada que falhou não conta", not regras(nucleo.avaliar_rodada(caso_f, [turno(1, ferramentas=[("consultar_cardapio", False)])], []))["chamou:consultar_cardapio"])
confere("proibida que falhou reprova", not regras(nucleo.avaliar_rodada(caso_f, [turno(1, ferramentas=[("criar_pedido", False)])], []))["nao_chamou:criar_pedido"])

# Texto proibido ignora caixa.
caso_t = {"id": "t", "falas": ["oi"], "espera": {"textos_proibidos": ["100% de desconto"]}}
confere("texto proibido reprova", not regras(nucleo.avaliar_rodada(caso_t, [turno()], ["Aplicado: 100% DE DESCONTO"]))["nao_disse:100% de desconto"])
confere("texto limpo passa", regras(nucleo.avaliar_rodada(caso_t, [turno()], ["Não posso dar desconto."]))["nao_disse:100% de desconto"])

# Agregação: taxa por rodada inteira, entrada com cache (D8) e mediana/máximo.
rodadas = []
for entrada, cache, desfecho in [(500, 1500, 2), (600, 2400, 2), (300, 0, 1)]:
    t = [turno(desfecho, entrada=entrada, cache=cache)]
    rodadas.append((nucleo.avaliar_rodada(caso, t, []), t))
ag = nucleo.agregar(caso, rodadas)
confere("taxa 2/3", ag["aprovadas"] == 2 and abs(ag["taxa"] - 2 / 3) < 1e-9)
confere("regra quebrada contada", ag["regras_quebradas"] == {"desfecho_final:Escalou": 1})
confere("entrada soma cache", ag["entrada_maxima"] == 3000 and ag["entrada_mediana"] == 2000)

# Veredito: verde só com todos no mínimo; vazio não é verde.
confere("2/3 reprova com 0,8", nucleo.veredito([ag])["verde"] is False and nucleo.veredito([ag])["reprovados"] == ["x"])
confere("2/3 passa com 0,6", nucleo.veredito([ag], 0.6)["verde"] is True)
confere("vazio não é verde", nucleo.veredito([])["verde"] is False)

# O arquivo de casos do repositório é válido e só cita ferramentas que o agente tem.
casos = nucleo.carregar_casos(os.path.join(AQUI, "eval", "casos.json"))
confere("casos carregam", len(casos) >= 5)
for c in casos:
    for n in c["espera"].get("ferramentas_chamadas", []) + c["espera"].get("ferramentas_proibidas", []):
        confere(f"ferramenta existe no agente: {n}", n in FERRAMENTAS_DO_AGENTE)

if falhas:
    print("FALHOU:", *falhas, sep="\n  ")
    sys.exit(1)
print("#3275 núcleo do eval: ok")
