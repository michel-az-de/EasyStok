// Categorias de uma lista de pratos, para os chips de filtro (#1808): Produção do dia e Itens do
// cardápio usam a mesma conta. Ficam as que aparecem, na ordem em que aparecem, com quantos itens
// cada uma tem. Item sem categoria não vira chip; ele continua em "Todas".
export function categoriasComContagem(itens) {
  const totais = new Map()
  for (const item of itens ?? []) {
    if (item.categoria) totais.set(item.categoria, (totais.get(item.categoria) ?? 0) + 1)
  }
  return [...totais].map(([nome, total]) => ({ nome, total }))
}

// Sem categoria escolhida, todo item passa.
export const daCategoria = (item, categoria) => !categoria || item.categoria === categoria
