"""Eval do agente de atendimento (#3275): roda os casos de eval/casos.json k vezes contra a bancada de homologação, avalia com
asserção determinística (eval/nucleo.py) e grava o resultado. Rode ANTES de trocar modelo, prompt, ferramenta, guarda ou limite.
Uso: python scripts/homologacao/eval_agente.py [--k 3] [--casos id1,id2] [--minimo 0.8] [--saida caminho.json]
Pré-requisitos: os da bancada (README.md): Docker, API em 18510, Meta falsa, proxy do modelo e FW_KEY. Sai com 0 se o eval ficou verde."""
import argparse, json, os, sys, time

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(AQUI, "eval"))
import nucleo

TURNOS_SQL = """select "Desfecho", coalesce("Motivo",''), "TokensEntrada", "TokensSaida", "TokensCacheLido", "Ferramentas"::text
    from atendimento_turnos_agente t join atendimento_conversas c on c."Id"=t."ConversaId"
    where c."ContatoIdExterno" like '%{tel}%' order by t."CriadoEm" """


def telefone(sequencia):
    """Número novo por rodada (conversa nova). Final 8888 e 9999 injetam erro na Meta falsa: pula."""
    base = 5511900000000 + (int(time.time()) % 10_000_000) * 10 + sequencia
    return str(base + 1 if str(base)[-4:] in ("8888", "9999") else base)


def ler_turnos(sim, tel):
    turnos = []
    for linha in sim.sql(TURNOS_SQL.format(tel=tel)).split("\n"):
        if not linha.strip():
            continue
        desfecho, motivo, entrada, saida, cache, ferramentas = linha.split("|", 5)
        turnos.append({"desfecho": int(desfecho), "motivo": motivo or None, "tokens_entrada": int(entrada), "tokens_saida": int(saida),
                       "tokens_cache_lido": int(cache), "ferramentas": [{"nome": f["nome"], "ok": f["ok"]} for f in json.loads(ferramentas)]})
    return turnos


def rodar_caso(sim, caso, sequencia):
    tel = telefone(sequencia)
    saidas = []
    for fala in caso["falas"]:
        marco = sim.sql("select now()")
        sim.webhook(tel, "Eval", sim.msg_texto(tel, fala))
        sim.esperar(marco, tel, time.time())
        saidas += [linha[4] for linha in sim.saidas(marco, tel)]
    turnos = ler_turnos(sim, tel)
    return nucleo.avaliar_rodada(caso, turnos, saidas), turnos


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--k", type=int, default=3)
    ap.add_argument("--casos", default="")
    ap.add_argument("--minimo", type=float, default=nucleo.MINIMO_APROVACAO)
    ap.add_argument("--saida", default="")
    args = ap.parse_args()
    casos = nucleo.carregar_casos(os.path.join(AQUI, "eval", "casos.json"))
    if args.casos:
        casos = [c for c in casos if c["id"] in args.casos.split(",")]
    import sim  # só aqui: o módulo lê o segredo da Meta ao ser importado
    agregados, sequencia = [], 0
    for caso in casos:
        rodadas = []
        for _ in range(args.k):
            sequencia += 1
            rodadas.append(rodar_caso(sim, caso, sequencia))
        agregado = nucleo.agregar(caso, rodadas)
        agregados.append(agregado)
        print(f"{caso['id']}: {agregado['aprovadas']}/{agregado['rodadas']} entrada mediana={agregado['entrada_mediana']} "
              f"máx={agregado['entrada_maxima']} {agregado['regras_quebradas'] or ''}")
    final = nucleo.veredito(agregados, args.minimo)
    saida = args.saida or os.path.join(AQUI, "eval", "resultados", time.strftime("%Y%m%d-%H%M%S") + ".json")
    os.makedirs(os.path.dirname(saida), exist_ok=True)
    with open(saida, "w", encoding="utf-8") as f:
        json.dump({"k": args.k, "veredito": final, "casos": agregados}, f, ensure_ascii=False, indent=1)
    print("VERDE" if final["verde"] else f"VERMELHO: {final['reprovados']}", "->", saida)
    return 0 if final["verde"] else 1


if __name__ == "__main__":
    sys.exit(main())
