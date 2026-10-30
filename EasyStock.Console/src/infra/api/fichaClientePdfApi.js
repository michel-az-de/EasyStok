import { chamarApi } from './cliente'

export function obterFichaClientePdf(clienteId) {
  if (!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(clienteId)) throw new Error('Cliente inválido.')
  return chamarApi(`/api/clientes/${clienteId}/ficha.pdf`, { arquivo: true })
}
