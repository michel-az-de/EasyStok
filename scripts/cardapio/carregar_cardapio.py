"""Carrega o cardápio de um JSON na vitrine da empresa logada, pela API oficial (#1334).

Uso:
  python scripts/cardapio/carregar_cardapio.py --api https://api.easystok.online --token-arquivo ~/.easystok/token-console
  (acrescente --aplicar para gravar; sem ele só mostra o que faria)

O token é o JWT do console (sessionStorage 'easystok.sessao', campo token). Fica num arquivo, nunca na linha
de comando. Idempotente: item cujo nome já existe na vitrine é pulado, nada é apagado nem alterado.
"""
import argparse, json, os, sys, urllib.error, urllib.request
from pathlib import Path

PADRAO_DADOS = Path(__file__).with_name("casa-da-baba.json")


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


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--api", required=True)
    p.add_argument("--token-arquivo", required=True)
    p.add_argument("--dados", default=str(PADRAO_DADOS))
    p.add_argument("--aplicar", action="store_true")
    a = p.parse_args()

    token = Path(os.path.expanduser(a.token_arquivo)).read_text(encoding="utf-8").strip()
    itens = json.loads(Path(a.dados).read_text(encoding="utf-8"))["itens"]

    st, vitrine = chamar(a.api, token, "GET", "/api/minha-vitrine")
    if st != 200:
        sys.exit(f"Sem vitrine para a empresa do token (HTTP {st}): {vitrine}")
    vitrine = vitrine["data"]
    print(f"Vitrine: {vitrine['slug']} | ativa={vitrine['ativo']}")

    st, atual = chamar(a.api, token, "GET", "/api/minha-vitrine/cardapio")
    if st != 200:
        sys.exit(f"Falha ao listar o cardápio (HTTP {st}): {atual}")
    existentes = {i["nomeEfetivo"].strip().lower() for i in atual["data"]["itens"]}
    novos = [i for i in itens if i["nome"].strip().lower() not in existentes]
    print(f"Arquivo: {len(itens)} itens | já na vitrine: {len(itens) - len(novos)} | a criar: {len(novos)}")

    falhas = 0
    for i in novos:
        if not a.aplicar:
            print(f"  [simulação] {i['categoria']}: {i['nome']} R$ {i['preco']:.2f}")
            continue
        st, r = chamar(a.api, token, "POST", "/api/minha-vitrine/cardapio", {
            "nomePublico": i["nome"], "categoriaTexto": i["categoria"], "ordemExibicao": i["ordem"],
            "visivel": True, "descricaoPublica": i.get("descricao"), "ingredientes": i.get("ingredientes"),
            "precoStorefront": i["preco"], "pesoExibicao": i["porcao"], "linha": "PrepararEmCasa"})
        if st not in (200, 201):
            falhas += 1
            print(f"  FALHOU {i['nome']}: HTTP {st} {r}")
    if not a.aplicar:
        print("Nada gravado. Rode de novo com --aplicar.")
        return

    req = urllib.request.Request(f"{a.api.rstrip('/')}/api/storefront/{vitrine['slug']}/menu")
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            publico = json.load(r)
        publico = publico.get("data", publico)
        print(f"Cardápio público: {len(publico['itens'])} itens visíveis")
    except urllib.error.HTTPError as e:
        print(f"Cardápio público indisponível (HTTP {e.code}). Vitrine inativa? ativa={vitrine['ativo']}")
    if falhas:
        sys.exit(f"{falhas} itens falharam")


if __name__ == "__main__":
    main()
