#!/usr/bin/env python3
"""Divide os testes de integração em partes de tempo parecido e confere que cada um rodou uma vez (#2976).

O CI compila uma vez, lista os testes com `dotnet test <dll> --list-tests` e roda cada parte numa
máquina. A divisão é pelo tempo medido (scripts/ci/tempos-testes.json), não por namespace. Classe
maior que meia parte é dividida por método: o xUnit roda os testes de uma classe em série, então
uma classe grande inteira numa parte só seria o caminho crítico. Teste sem tempo medido recebe a
mediana do assembly.

Subcomandos:
  particionar --listas DIR --tempos ARQ --partes N --saida DIR
      DIR de listas tem um <Assembly>.txt por assembly (saída do --list-tests). Grava
      <saida>/parte-<k>/<Assembly>.filtro (valor do --filter) e <saida>/matriz.json.
  guardar --listas DIR --trx DIR
      Falha se algum teste listado não aparece exatamente uma vez nos .trx. O .trx de cada parte
      se chama <Assembly>-parte-<k>.trx.
  tempos --trx DIR --saida ARQ
      Regrava a tabela de tempos a partir dos .trx de um run (soma os casos de cada método).

Saídas: 0 ok | 1 guarda reprovou ou entrada vazia | 2 uso inválido.
"""
import argparse
import collections
import json
import pathlib
import re
import statistics
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
# Cabeçalho do --list-tests em inglês (runner) ou português (máquina local).
CABECALHO = re.compile(r"(tests are available|testes est[aã]o dispon)", re.IGNORECASE)
ESPECIAIS = re.compile(r"([\\()&|=!~])")


def ler_lista(arquivo):
    nomes, dentro = [], False
    for linha in pathlib.Path(arquivo).read_text(encoding="utf-8-sig").splitlines():
        if not dentro:
            dentro = bool(CABECALHO.search(linha))
        elif linha.startswith("    ") and linha.strip():
            nomes.append(linha.strip())
    return nomes


def ler_listas(diretorio):
    listas = {p.stem: ler_lista(p) for p in sorted(pathlib.Path(diretorio).glob("*.txt"))}
    return {a: n for a, n in listas.items() if n}


def metodo(nome):
    return nome.split("(", 1)[0]


def classe(nome):
    return metodo(nome).rsplit(".", 1)[0]


def escapar(valor):
    return ESPECIAIS.sub(r"\\\1", valor)


def unidades(assembly, nomes, tempos):
    metodos = list(dict.fromkeys(metodo(n) for n in nomes))
    conhecidos = [tempos[f"{assembly}::{m}"] for m in metodos if f"{assembly}::{m}" in tempos]
    padrao = statistics.median(conhecidos) if conhecidos else 1.0
    por_classe = collections.defaultdict(list)
    for m in metodos:
        por_classe[m.rsplit(".", 1)[0]].append((m, tempos.get(f"{assembly}::{m}", padrao)))
    return por_classe


def particionar(args):
    listas = ler_listas(args.listas)
    if not listas:
        print("nenhuma lista de testes em", args.listas)
        return 1
    tempos = json.loads(pathlib.Path(args.tempos).read_text(encoding="utf-8")) if args.tempos else {}
    classes = {a: unidades(a, n, tempos) for a, n in listas.items()}
    total = sum(t for c in classes.values() for ms in c.values() for _, t in ms)
    meta = total / args.partes

    blocos = []  # (segundos, assembly, [cláusulas do filtro])
    for assembly, por_classe in classes.items():
        todos = [m for ms in por_classe.values() for m, _ in ms]
        for nome, ms in por_classe.items():
            prefixo = nome + "."
            ambiguo = any(m.startswith(prefixo) and m.rsplit(".", 1)[0] != nome for m in todos)
            soma = sum(t for _, t in ms)
            if soma > meta / 2 or ambiguo:
                # Por método. Sem prefixo ambíguo a classe pequena ainda fica junta numa parte.
                if soma > meta / 2:
                    blocos += [(t, assembly, [f"FullyQualifiedName={escapar(m)}"]) for m, t in ms]
                else:
                    blocos.append((soma, assembly, [f"FullyQualifiedName={escapar(m)}" for m, _ in ms]))
            else:
                blocos.append((soma, assembly, [f"FullyQualifiedName~{escapar(prefixo)}"]))

    # Maior primeiro, sempre na parte mais leve (LPT).
    cargas = [0.0] * args.partes
    filtros = [collections.defaultdict(list) for _ in range(args.partes)]
    for segundos, assembly, clausulas in sorted(blocos, key=lambda b: (-b[0], b[1], b[2])):
        k = min(range(args.partes), key=lambda i: (cargas[i], i))
        cargas[k] += segundos
        filtros[k][assembly] += clausulas

    saida = pathlib.Path(args.saida)
    for k, por_assembly in enumerate(filtros, start=1):
        pasta = saida / f"parte-{k}"
        pasta.mkdir(parents=True, exist_ok=True)
        for assembly, clausulas in por_assembly.items():
            (pasta / f"{assembly}.filtro").write_text("|".join(clausulas), encoding="utf-8")
        print(f"parte {k}: {cargas[k - 1]:.0f} s estimados, "
              + ", ".join(f"{a} {len(c)} cláusulas" for a, c in por_assembly.items()))
    (saida / "matriz.json").write_text(json.dumps(list(range(1, args.partes + 1))), encoding="utf-8")
    return 0


def resultados(diretorio):
    por_assembly = collections.defaultdict(list)
    for trx in sorted(pathlib.Path(diretorio).rglob("*.trx")):
        assembly = trx.stem.rsplit("-parte-", 1)[0]
        raiz = ET.parse(trx).getroot()
        por_assembly[assembly] += [r for r in raiz.iterfind(".//t:UnitTestResult", NS)]
    return por_assembly


def guardar(args):
    listas = ler_listas(args.listas)
    if not listas:
        print("nenhuma lista de testes em", args.listas)
        return 1
    rodados = resultados(args.trx)
    erros = 0
    for assembly, nomes in listas.items():
        esperado = collections.Counter(nomes)
        visto = collections.Counter(r.get("testName", "?") for r in rodados.get(assembly, []))
        faltam = esperado - visto
        sobram = visto - esperado
        if faltam or sobram:
            erros += 1
            print(f"{assembly}: {sum(esperado.values())} listados, {sum(visto.values())} nos trx")
            for nome, n in sorted(faltam.items()):
                print(f"  não rodou ({n}x): {nome}")
            for nome, n in sorted(sobram.items()):
                print(f"  rodou a mais ({n}x): {nome}")
        else:
            print(f"{assembly}: {sum(esperado.values())} testes, cada um exatamente uma vez")
    for assembly in sorted(set(rodados) - set(listas)):
        erros += 1
        print(f"{assembly}: tem trx mas não tem lista")
    return 1 if erros else 0


def duracao(texto):
    h, m, s = (texto or "0:0:0").split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


def tempos(args):
    tabela = collections.defaultdict(float)
    for assembly, rs in resultados(args.trx).items():
        for r in rs:
            tabela[f"{assembly}::{metodo(r.get('testName', '?'))}"] += duracao(r.get("duration"))
    if not tabela:
        print("nenhum resultado em", args.trx)
        return 1
    ordenada = {k: round(v, 1) for k, v in sorted(tabela.items())}
    pathlib.Path(args.saida).write_text(json.dumps(ordenada, indent=0, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{len(ordenada)} métodos gravados em {args.saida}")
    return 0


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="comando", required=True)
    p = sub.add_parser("particionar")
    p.add_argument("--listas", required=True)
    p.add_argument("--tempos")
    p.add_argument("--partes", type=int, required=True)
    p.add_argument("--saida", required=True)
    g = sub.add_parser("guardar")
    g.add_argument("--listas", required=True)
    g.add_argument("--trx", required=True)
    t = sub.add_parser("tempos")
    t.add_argument("--trx", required=True)
    t.add_argument("--saida", required=True)
    args = parser.parse_args(argv)
    if args.comando == "particionar" and args.partes < 1:
        parser.error("--partes precisa ser 1 ou mais")
    return {"particionar": particionar, "guardar": guardar, "tempos": tempos}[args.comando](args)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
