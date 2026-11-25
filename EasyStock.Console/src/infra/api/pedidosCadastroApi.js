// #2478: leitura dos pedidos para a tela Pedidos de Cadastros e para o detalhe da Busca na Casa.
import { chamarApi } from './cliente'
import { consultaDePedidos, consultaDoResumo } from '../../dominio/pedidosCadastro'

const opcoes = (contexto, sinal) => ({ identidadeEsperada: contexto.identidade, sinal, prazoComCancelamento: true })

export async function listarPedidos(filtros, contexto, sinal, agoraMs = Date.now()) {
  const parametros = new URLSearchParams({ empresaId: contexto.empresaId, ...consultaDePedidos(filtros, agoraMs) })
  const r = await chamarApi(`/api/pedidos?${parametros}`, { ...opcoes(contexto, sinal), envelope: true })
  const itens = r?.data
  if (!Array.isArray(itens)) throw new Error('A lista de pedidos não foi recebida. Tente carregar novamente.')
  return { itens, total: r?.meta?.totalCount ?? r?.meta?.total ?? itens.length }
}

export const obterPedido = (id, contexto, sinal) =>
  chamarApi(`/api/pedidos/${encodeURIComponent(id)}?empresaId=${encodeURIComponent(contexto.empresaId)}`, opcoes(contexto, sinal))
// #2495 (S6): os 5 pedidos mais recentes do cliente, para a ficha no editor de Cadastros.
export async function listarPedidosDoCliente(clienteId, contexto, sinal) {
  const parametros = new URLSearchParams({ empresaId: contexto.empresaId, clienteId, page: 1, pageSize: 5, sort: 'criadoem', order: 'desc' })
  const r = await chamarApi(`/api/pedidos?${parametros}`, { ...opcoes(contexto, sinal), envelope: true })
  return Array.isArray(r?.data) ? r.data : []
}

// #2832: quantos pedidos de hoje há numa situação (`status`, lista por vírgula; nulo = todos). Pede uma linha só e
// lê o total da página: a contagem é da API, nunca da página que veio.
export async function contarPedidos({ status = null, desde }, contexto, sinal) {
  const parametros = new URLSearchParams({ empresaId: contexto.empresaId, page: 1, pageSize: 1, desde, ...(status ? { status } : {}) })
  const r = await chamarApi(`/api/pedidos?${parametros}`, { ...opcoes(contexto, sinal), envelope: true })
  const total = r?.meta?.totalCount ?? r?.meta?.total
  if (!Number.isInteger(total)) throw new Error('A contagem dos pedidos não foi recebida.')
  return total
}

// #3294: os indicadores da tela Pedidos, calculados pela API sobre os filtros da lista (sem situação nem página).
export async function obterResumoDePedidos(filtros, contexto, sinal, agoraMs = Date.now()) {
  const parametros = new URLSearchParams({ empresaId: contexto.empresaId, ...consultaDoResumo(filtros, agoraMs) })
  const r = await chamarApi(`/api/pedidos/resumo?${parametros}`, opcoes(contexto, sinal))
  if (!Number.isInteger(r?.pedidos)) throw new Error('Os indicadores não carregaram.')
  return r
}
