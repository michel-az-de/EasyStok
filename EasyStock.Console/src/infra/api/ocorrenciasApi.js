import { chamarApi } from './cliente'

const rota = (id) => {
  if (!id) throw new Error('Escolha uma ocorrência do pedido.')
  return `/api/ocorrencias/${id}`
}
export const listarOcorrencias = (pedidoId) => chamarApi(`/api/ocorrencias?pedidoId=${encodeURIComponent(pedidoId)}`)
export const abrirOcorrencia = (corpo, chave = null) => chamarApi('/api/ocorrencias', { metodo: 'POST', corpo, ...(chave ? { chave } : {}) })
export const apurarOcorrencia = (id) => chamarApi(`${rota(id)}/apurar`, { metodo: 'POST', corpo: {} })
export const resolverOcorrencia = (id, corpo) => chamarApi(`${rota(id)}/resolver`, { metodo: 'POST', corpo })
