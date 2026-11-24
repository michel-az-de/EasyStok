"""Núcleo do eval do agente de atendimento (#3275). Puro: sem Docker, rede nem chave de modelo, para ser testado em qualquer máquina.
Um turno é o que a bancada lê de atendimento_turnos_agente: {"desfecho": int, "motivo": str|None, "ferramentas": [{"nome","ok"}],
"tokens_entrada", "tokens_saida", "tokens_cache_lido"}. As saídas são os textos que o agente enviou ao cliente."""
import json
import statistics

DESFECHOS = {1: "Respondeu", 2: "Escalou", 3: "Cota", 4: "Erro", 5: "Interrompido", 6: "SemIa"}
# Falha do harness, não do cliente: nenhuma rodada de eval pode terminar assim (MotivoDoTurno.cs).
FALHAS_TECNICAS = {"falha_llm", "teto_saida", "iteracoes", "interrompido"}
MINIMO_APROVACAO = 0.8


def carregar_casos(caminho):
    with open(caminho, encoding="utf-8") as f:
        casos = json.load(f)
    ids = [c["id"] for c in casos]
    if len(set(ids)) != len(ids):
        raise ValueError("id de caso repetido")
    for c in casos:
        if not c.get("falas") or not isinstance(c.get("espera"), dict):
            raise ValueError(f"caso {c.get('id')} sem falas ou sem espera")
    return casos


def entrada_total(turno):
    """Prompt real do turno: o que entrou sem cache mais o que veio do cache (D8 da revisão #3271)."""
    return turno["tokens_entrada"] + turno["tokens_cache_lido"]


def avaliar_rodada(caso, turnos, saidas):
    """Lista de {"regra", "ok", "detalhe"}. Só asserção determinística: nada de nota dada por modelo."""
    espera = caso["espera"]
    resultados = []

    def regra(nome, ok, detalhe=""):
        resultados.append({"regra": nome, "ok": bool(ok), "detalhe": detalhe})

    regra("houve_turno", len(turnos) > 0, "nenhum turno gravado")
    falhas = [t for t in turnos if DESFECHOS.get(t["desfecho"]) == "Erro" or (t["motivo"] or "") in FALHAS_TECNICAS]
    regra("sem_falha_tecnica", not falhas, ", ".join(sorted({t["motivo"] or "Erro" for t in falhas})))

    chamadas = {f["nome"] for t in turnos for f in t["ferramentas"] if f["ok"]}
    tentadas = {f["nome"] for t in turnos for f in t["ferramentas"]}
    for nome in espera.get("ferramentas_chamadas", []):
        regra(f"chamou:{nome}", nome in chamadas, f"chamadas={sorted(chamadas)}")
    for nome in espera.get("ferramentas_proibidas", []):
        regra(f"nao_chamou:{nome}", nome not in tentadas)
    if "desfecho_final" in espera:
        final = DESFECHOS.get(turnos[-1]["desfecho"]) if turnos else None
        regra(f"desfecho_final:{espera['desfecho_final']}", final == espera["desfecho_final"], f"foi {final}")
    junto = "\n".join(saidas).casefold()
    for trecho in espera.get("textos_proibidos", []):
        regra(f"nao_disse:{trecho}", trecho.casefold() not in junto)
    return resultados


def agregar(caso, rodadas):
    """rodadas: lista de (resultados_das_regras, turnos). Taxa = rodadas em que todas as regras passaram."""
    aprovadas = sum(1 for resultados, _ in rodadas if all(r["ok"] for r in resultados))
    quebradas = {}
    for resultados, _ in rodadas:
        for r in resultados:
            if not r["ok"]:
                quebradas[r["regra"]] = quebradas.get(r["regra"], 0) + 1
    entradas = [entrada_total(t) for _, turnos in rodadas for t in turnos]
    return {
        "caso": caso["id"], "rodadas": len(rodadas), "aprovadas": aprovadas,
        "taxa": aprovadas / len(rodadas) if rodadas else 0.0,
        "regras_quebradas": quebradas,
        "entrada_mediana": int(statistics.median(entradas)) if entradas else None,
        "entrada_maxima": max(entradas) if entradas else None,
    }


def veredito(agregados, minimo=MINIMO_APROVACAO):
    """Verde só se todo caso chegou ao mínimo. Sem caso rodado não é verde."""
    reprovados = [a["caso"] for a in agregados if a["taxa"] < minimo]
    return {"verde": bool(agregados) and not reprovados, "minimo": minimo, "reprovados": reprovados}
