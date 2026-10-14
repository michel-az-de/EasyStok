#!/usr/bin/env bash
# Puxa uma imagem oficial do Docker Hub (ex.: postgres:16) por espelhos e a deixa com a tag curta.
# #1514/#1796: o Docker Hub anônimo estoura o limite de pulls e a ECR pública o de dados, as duas
# por IP do runner. Cada fonte tem novas tentativas; esgotada uma, passa para a próxima.
# Uso: scripts/ci/puxar-imagem.sh postgres:16 [postgres:17-alpine ...]
set -euo pipefail

FONTES=(${PUXAR_IMAGEM_FONTES:-mirror.gcr.io/library public.ecr.aws/docker/library docker.io/library})
TENTATIVAS=${PUXAR_IMAGEM_TENTATIVAS:-3}

puxar() {
  local imagem=$1 fonte origem i
  for fonte in "${FONTES[@]}"; do
    origem="$fonte/$imagem"
    for ((i = 1; i <= TENTATIVAS; i++)); do
      if docker pull -q "$origem" >/dev/null; then
        docker tag "$origem" "$imagem"
        echo "puxar-imagem: $imagem <- $origem"
        return 0
      fi
      echo "puxar-imagem: $origem falhou (tentativa $i de $TENTATIVAS)" >&2
      ((i < TENTATIVAS)) && sleep $((i * 5))
    done
  done
  echo "puxar-imagem: nenhuma fonte entregou $imagem" >&2
  return 1
}

[ $# -gt 0 ] || { echo "uso: $0 imagem:tag [...]" >&2; exit 2; }
for imagem in "$@"; do puxar "$imagem"; done
