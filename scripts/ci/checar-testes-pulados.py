#!/usr/bin/env python3
"""Reprova o job quando um teste foi pulado sem ser opt-in (#2371).

Os testes de integração usam [SkippableFact] e pulam quando o Postgres (ou o Docker do Testcontainers)
não sobe. No CI isso deixava o job verde sem provar nada. Este script lê todos os .trx de um diretório
e falha se algum teste pulou por um motivo que não esteja na lista de opt-in abaixo: ensaios que só
rodam com uma variável de ambiente explícita e que o CI, de propósito, não define.

O motivo da fixture costuma ser o ex.Message cru (ex.: "Connection refused"), então a regra é por lista
de permitidos, não por palavra de "indisponível". Para um novo opt-in, acrescente a variável aqui.

Uso: python3 scripts/ci/checar-testes-pulados.py <diretório com .trx>
Saídas: 0 nenhum pulo fora da lista | 1 pulo não permitido ou nenhum .trx | 2 uso inválido.
"""
import pathlib
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

# Variáveis de opt-in: o teste só roda quando a variável existe, e o CI não a define.
OPT_IN = (
    "EASYSTOCK_PERF_PG",                  # FluxoBucketsPerfTests: medição contra um Postgres real
    "EASYSTOK_COMANDA_GO_LIVE_FIXTURE",   # ensaio de go-live da comanda
    "EASYSTOK_COMANDA_BROWSER_FIXTURE",   # fixture interativa do navegador
    "EASYSTOK_R4_BROWSER_FIXTURE",        # fixture local de Cadastros R4 com login real
    "EASYSTOK_TEST_CADASTRO_CLI_REAL",    # inferência sintética explícita no CLI autenticado
)


def pulos(arquivo):
    raiz = ET.parse(arquivo).getroot()
    for r in raiz.iterfind(".//t:UnitTestResult", NS):
        if r.get("outcome") == "NotExecuted":
            saida = r.find("t:Output", NS)
            motivo = " ".join("".join(saida.itertext()).split()) if saida is not None else ""
            yield r.get("testName", "?"), motivo


def main(argv):
    if len(argv) != 2:
        print(__doc__.strip().splitlines()[-2], file=sys.stderr)
        return 2
    arquivos = sorted(pathlib.Path(argv[1]).rglob("*.trx"))
    if not arquivos:
        print(f"::error::nenhum .trx em {argv[1]}: sem resultado não há o que provar")
        return 1

    permitidos, proibidos = [], []
    for arq in arquivos:
        for nome, motivo in pulos(arq):
            (permitidos if any(v in motivo for v in OPT_IN) else proibidos).append((arq.name, nome, motivo))

    for arq, nome, motivo in permitidos:
        print(f"pulo opt-in ({arq}): {nome} | {motivo[:160]}")
    for arq, nome, motivo in proibidos:
        print(f"::error::teste pulado no CI ({arq}): {nome} | {motivo[:300] or '(sem motivo)'}")

    print(f"{len(arquivos)} trx, {len(permitidos)} pulo(s) opt-in, {len(proibidos)} pulo(s) não permitido(s)")
    if proibidos:
        print("::error::teste pulado no CI não prova nada. Se o Postgres ou o Docker não subiu, corrija o runner; "
              "se é um ensaio opt-in novo, acrescente a variável em OPT_IN.")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
