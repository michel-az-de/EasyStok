import { chamarApi } from './cliente'
import { identidadeDaSessao, lerSessao } from './sessao'

const rota = (pedidoId) => `/api/pedidos/${pedidoId}/estornos-manuais`
const chave = (pedidoId) => `easystok.devolucao:${identidadeDaSessao(lerSessao())}:${pedidoId}`
export const listarEstornosManuais = (pedidoId) => chamarApi(rota(pedidoId))
export const registrarEstornoManual = (pedidoId, corpo) => chamarApi(rota(pedidoId), { metodo: 'POST', corpo })

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
