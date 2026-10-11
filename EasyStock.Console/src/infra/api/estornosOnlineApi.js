import { chamarApi } from './cliente'
import { identidadeDaSessao, lerSessao } from './sessao'

const rota = (id) => `/api/pedidos/${id}/estornos-online`
const chave = (id) => `easystok.estorno-online:${identidadeDaSessao(lerSessao())}:${id}`
export const listarEstornosOnline = (id) => chamarApi(rota(id))
export const solicitarEstornoOnline = (id, corpo) => chamarApi(rota(id), { metodo: 'POST', corpo })
export const retomarEstornoOnline = (id, operacaoId) => chamarApi(`${rota(id)}/${operacaoId}/retomar`, { metodo: 'POST' })
// #1563: encerra a solicitação pendente que nunca foi ao Mercado Pago. Sem corpo; 409 se já foi enviada.
export const descartarEstornoOnline = (id, operacaoId) => chamarApi(`${rota(id)}/${operacaoId}/descartar`, { metodo: 'POST' })
export const lerEstornoSemResposta = (id) => JSON.parse(localStorage.getItem(chave(id)) ?? 'null')
export const guardarEstornoSemResposta = (id, corpo) => localStorage.setItem(chave(id), JSON.stringify(corpo))
export function limparEstornoSemResposta(id, operacaoId) {
  if (lerEstornoSemResposta(id)?.operacaoId === operacaoId) localStorage.removeItem(chave(id))
}
