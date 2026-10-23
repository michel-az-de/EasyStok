import { chamarApi } from './cliente'
import { identidadeDaSessao, lerSessao } from './sessao'

const rota = (pedidoId) => `/api/pedidos/${pedidoId}/estornos-manuais`
const chave = (pedidoId) => `easystok.devolucao:${identidadeDaSessao(lerSessao())}:${pedidoId}`
export const listarEstornosManuais = (pedidoId) => chamarApi(rota(pedidoId))
export const registrarEstornoManual = (pedidoId, corpo) => chamarApi(rota(pedidoId), { metodo: 'POST', corpo })

// J2 (#1990): cancelados dos últimos 90 dias (pela criação; encomenda cancelada hoje pode ter
// nascido semanas antes), os 200 mais novos, para o fechamento do caixa somar o que falta devolver.
export const DIAS_DE_CANCELADOS = 90
export function listarPedidosCanceladosRecentes(agora = Date.now()) {
  const desde = new Date(agora - DIAS_DE_CANCELADOS * 86400000).toISOString()
  const busca = new URLSearchParams({ status: 'cancelado', desde, pageSize: '200', sort: 'criadoem', order: 'desc' })
  return chamarApi(`/api/pedidos?${busca}`)
}

// Só a solicitação ainda sem resposta fica no navegador. O saldo e o histórico vêm da API.
export function lerDevolucaoPendente(pedidoId) {
  const texto = localStorage.getItem(chave(pedidoId))
  return texto ? JSON.parse(texto) : null
}
export function guardarDevolucaoPendente(pedidoId, corpo) {
  const existente = lerDevolucaoPendente(pedidoId)
  if (existente && existente.operacaoId !== corpo.operacaoId)
    throw new Error('Há outra confirmação pendente neste navegador. Atualize as devoluções.')
  localStorage.setItem(chave(pedidoId), JSON.stringify(corpo))
}
export function limparDevolucaoPendente(pedidoId, operacaoId) {
  if (lerDevolucaoPendente(pedidoId)?.operacaoId === operacaoId) localStorage.removeItem(chave(pedidoId))
}
