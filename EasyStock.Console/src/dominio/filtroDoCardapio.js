// Recorte dos Itens do cardápio (gestão): disponibilidade, categoria (#1808) e busca, combinados.
import { categoriasComContagem, daCategoria } from './categorias.js'

export const FILTROS_DO_CARDAPIO = [
  { id: 'todos', rotulo: 'Todos', aceita: () => true },
  { id: 'hoje', rotulo: 'Disponíveis hoje', aceita: (item) => item.hoje && !item.arquivado },
  { id: 'fora', rotulo: 'Fora de hoje', aceita: (item) => !item.hoje || item.arquivado },
  { id: 'sem-foto', rotulo: 'Sem foto', aceita: (item) => !item.foto },
]

const normalizar = (texto) => String(texto ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()
const aceitaDoFiltro = (filtro) => (FILTROS_DO_CARDAPIO.find((f) => f.id === filtro) ?? FILTROS_DO_CARDAPIO[0]).aceita

// A busca olha nome, categoria e porção ("lasanha", "massas" ou "800 g").
function naBusca(itens, busca) {
  const termo = normalizar(String(busca ?? '').trim())
  if (!termo) return itens ?? []
  return (itens ?? []).filter((item) => normalizar(`${item.nome} ${item.categoria ?? ''} ${item.porcao ?? ''}`).includes(termo))
}

export function filtrarCardapio(itens, { filtro = 'todos', categoria = null, busca = '' } = {}) {
  const aceita = aceitaDoFiltro(filtro)
  return naBusca(itens, busca).filter((item) => aceita(item) && daCategoria(item, categoria))
}

// Cada contagem aplica os outros recortes: o número do chip é o que a lista mostra ao tocá-lo. As
// categorias vêm da lista inteira, para os chips não sumirem nem trocarem de lugar com o recorte.
export function contagensDoCardapio(itens, { filtro = 'todos', categoria = null, busca = '' } = {}) {
  const buscados = naBusca(itens, busca)
  const daCategoriaEscolhida = buscados.filter((item) => daCategoria(item, categoria))
  const doFiltro = buscados.filter(aceitaDoFiltro(filtro))
  const totais = new Map(categoriasComContagem(doFiltro).map((c) => [c.nome, c.total]))
  return {
    filtros: Object.fromEntries(FILTROS_DO_CARDAPIO.map((f) => [f.id, daCategoriaEscolhida.filter(f.aceita).length])),
    categorias: categoriasComContagem(itens).map(({ nome }) => ({ nome, total: totais.get(nome) ?? 0 })),
    todas: doFiltro.length,
  }
}

// A posição é a lista completa do servidor; ordenar um recorte mudaria vizinhos invisíveis.
export const podeReordenar = ({ filtro = 'todos', categoria = null, busca = '' } = {}) =>
  filtro === 'todos' && !categoria && !String(busca ?? '').trim()
