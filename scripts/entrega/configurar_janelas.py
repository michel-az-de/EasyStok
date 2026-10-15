"""Cadastra as janelas de entrega da vitrine pela API do console (S45).

Uso:
  python scripts/entrega/configurar_janelas.py --api https://api.easystok.online --token-arquivo ~/.easystok/token-console
  (acrescente --aplicar para gravar; sem ele só mostra o que faria)

O token é o JWT do console (sessionStorage 'easystok.sessao', campo token), de um usuário Admin. Fica num
arquivo, nunca na linha de comando nem no chat. Idempotente: a chave é dia da semana + hora de início + hora de
fim; janela que já existe não é recriada nem alterada (capacidade e rótulo diferentes só são avisados).
"""
import argparse
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

PADRAO_DADOS = Path(__file__).with_name("janelas-casa-da-baba.json")
DIAS = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"]


def chamar(api, token, metodo, caminho, corpo=None):
    req = urllib.request.Request(
        api.rstrip("/") + caminho, method=metodo,
        data=json.dumps(corpo).encode() if corpo is not None else None,
        headers={"content-type": "application/json", "authorization": f"Bearer {token}"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return r.status, json.load(r)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")[:300]


def hhmm(valor):
    """'09:00' ou '09:00:00' → '09:00'."""
    return str(valor)[:5]


def chave(dia, inicio, fim):
    return (int(dia), hhmm(inicio), hhmm(fim))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--api", required=True)
    p.add_argument("--token-arquivo", required=True)
    p.add_argument("--dados", default=str(PADRAO_DADOS))
    p.add_argument("--aplicar", action="store_true")
    a = p.parse_args()

    token = Path(os.path.expanduser(a.token_arquivo)).read_text(encoding="utf-8").strip()
    desejadas = json.loads(Path(a.dados).read_text(encoding="utf-8"))["janelas"]

    st, atual = chamar(a.api, token, "GET", "/api/minha-vitrine/entrega/janelas")
    if st != 200:
        sys.exit(f"Falha ao listar as janelas (HTTP {st}): {atual}")
    existentes = {chave(j["diaDaSemana"], j["horaInicio"], j["horaFim"]): j for j in atual["data"]}

    criar = []
    for d in desejadas:
        k = chave(d["diaDaSemana"], d["horaInicio"], d["horaFim"])
        j = existentes.get(k)
        if j is None:
            criar.append(d)
        elif j["capacidadeMaxima"] != d["capacidade"] or not j["ativa"]:
            print(f"AVISO: {DIAS[k[0]]} {k[1]}-{k[2]} já existe com capacidade {j['capacidadeMaxima']}, "
                  f"ativa={j['ativa']}; não alterado")

    print(f"Arquivo: {len(desejadas)} janelas | já existem: {len(desejadas) - len(criar)} | a criar: {len(criar)}")
    for d in criar:
        print(f"  {DIAS[d['diaDaSemana']]} {d['horaInicio']}-{d['horaFim']} {d['label']} ({d['capacidade']} vagas)")
    if not a.aplicar:
        print("Simulação: nada gravado. Acrescente --aplicar.")
        return

    falhas = 0
    for d in criar:
        st, r = chamar(a.api, token, "POST", "/api/minha-vitrine/entrega/janelas", {
            "diaDaSemana": d["diaDaSemana"], "horaInicio": hhmm(d["horaInicio"]) + ":00",
            "horaFim": hhmm(d["horaFim"]) + ":00",
            "capacidadeMaxima": d["capacidade"], "label": d["label"]})
        if st != 200:
            falhas += 1
            print(f"FALHA {DIAS[d['diaDaSemana']]} {d['horaInicio']} (HTTP {st}): {r}")
    print(f"Criadas: {len(criar) - falhas} | falhas: {falhas}")
    sys.exit(1 if falhas else 0)


if __name__ == "__main__":
    main()
