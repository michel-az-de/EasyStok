import { chamarApi } from '../infra/api/cliente'

const idValido = (id) => /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(id)

export function consultarViagemDoCliente(clienteId, pedidoId, sinal) {
  if (!idValido(clienteId) || !idValido(pedidoId)) return Promise.reject(new Error('Cliente ou pedido inválido.'))
  return chamarApi(`/api/clientes/${clienteId}/viagem?pedidoId=${pedidoId}`, { sinal, prazoComCancelamento: true })
}
