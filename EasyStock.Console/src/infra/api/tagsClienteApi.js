import { chamarApi } from './cliente'

// Tags do cliente (S24). A API normaliza (minúsculo, sem acento, até 40) e devolve 409 na repetida.
export const adicionarTagCliente = (clienteId, tag) =>
  chamarApi(`/api/clientes/${clienteId}/tags`, { metodo: 'POST', corpo: { tag } })
export const removerTagCliente = (clienteId, tag) =>
  chamarApi(`/api/clientes/${clienteId}/tags/${encodeURIComponent(tag)}`, { metodo: 'DELETE' })
