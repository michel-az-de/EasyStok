import { chamarApi } from './cliente'

const rota = id => `/api/atendimento/conversas/${id}/comandas-cliente`
export const listarComandasCliente = id => chamarApi(rota(id))
export const encaminharEntregaComanda = (id, linkId, revisao) => chamarApi(`${rota(id)}/${linkId}/encaminhar-entrega`, { metodo: 'POST', corpo: { revisao } })
export const confirmarPedidoComanda = (id, linkId, revisao) => chamarApi(`${rota(id)}/${linkId}/confirmar-pedido`, { metodo: 'POST', corpo: { revisao } })
export const combinarFreteComanda = (id, linkId, revisao, valor) => chamarApi(`${rota(id)}/${linkId}/combinar-frete`, { metodo: 'POST', corpo: { revisao, valor } })
export const confirmarComandaCliente = (id, linkId, aceitar) => chamarApi(`${rota(id)}/${linkId}/confirmar`, { metodo: 'POST', corpo: { aceitar } })
export const salvarComandaCliente = (id, linkId, revisao, rascunho) => chamarApi(`${rota(id)}/${linkId}/rascunho`, { metodo: 'PUT', corpo: { revisao, rascunho } })
