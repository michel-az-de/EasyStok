// #2478: leitura dos pedidos para a tela Pedidos de Cadastros e para o detalhe da Busca na Casa.
import { chamarApi } from './cliente'
import { consultaDePedidos } from '../../dominio/pedidosCadastro'

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
