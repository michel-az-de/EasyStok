// #1808: os pratos em que uma imagem aprovada do acervo pode ser usada. Qualquer prato da empresa
// que não foi tirado do cardápio; os já vinculados à imagem primeiro, depois por nome.
export function pratosParaUsar(itens, vinculados) {
  const ligados = new Set(vinculados ?? [])
  return (itens ?? [])
    .filter((i) => !i.arquivado)
    .map((i) => ({ id: i.cardapioItemId, nome: i.porcao ? `${i.nome} · ${i.porcao}` : i.nome, vinculado: ligados.has(i.cardapioItemId) }))
    .sort((a, b) => Number(b.vinculado) - Number(a.vinculado) || a.nome.localeCompare(b.nome, 'pt-BR'))
}
