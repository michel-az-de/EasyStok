export const LIMITE_BUSCA_CASA = 10
export const nomesDaBusca = { clientes: 'Pessoas', conversas: 'Conversas', pedidos: 'Pedidos', produtos: 'Produtos', telas: 'Telas' }

export function categoriasDaBusca({ permite, acoes = {} }) {
  return [permite('cadastros') && 'clientes', permite('atendimento') && 'conversas', permite('atendimento') && 'pedidos', acoes.verCadastrosProdutos === true && 'produtos'].filter(Boolean)
}

export function requisicoesDaBusca(termo, acesso, empresaId) {
  const busca = termo.trim()
  if (!busca) return []
  const urls = {
    clientes: ['/api/clientes/buscar', { termo: busca, max: LIMITE_BUSCA_CASA, empresaId }],
    conversas: ['/api/atendimento/conversas', { q: busca, limite: LIMITE_BUSCA_CASA, pagina: 1 }],
    pedidos: ['/api/pedidos', { search: busca, pageSize: LIMITE_BUSCA_CASA, page: 1, empresaId }],
    produtos: ['/api/produtos/search', { termo: busca, limite: LIMITE_BUSCA_CASA, empresaId }],
  }
  return categoriasDaBusca(acesso).map(tipo => ({ tipo, url: `${urls[tipo][0]}?${new URLSearchParams(urls[tipo][1])}` }))
}

export function resultadoDaBusca(tipo, item) {
  if (!item?.id) throw new Error('O resultado chegou sem identidade. Tente buscar novamente.')
  const rotulo = tipo === 'conversas' ? item.contatoNome || item.contatoIdExterno
    : tipo === 'pedidos' ? `Pedido${item.numeroDoDia ? ` ${item.numeroDoDia}` : ''} · ${item.clienteNome || 'Sem nome'}` : item.nome
  const detalhe = tipo === 'conversas' ? item.contatoIdExterno : tipo === 'pedidos' ? item.status : tipo === 'clientes' ? item.telefone : item.skuBase
  return { id: `${tipo}:${item.id}`, tipo, rotulo: rotulo || 'Sem nome', descricao: [nomesDaBusca[tipo], detalhe, `ID ${item.id}`].filter(Boolean).join(' · '), item }
}
