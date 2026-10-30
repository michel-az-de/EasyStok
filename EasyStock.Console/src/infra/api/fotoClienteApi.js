import { chamarApi } from './cliente'

const GUID = '[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}'
const ROTA_FOTO = new RegExp(`^/api/clientes/${GUID}/foto\\?v=[a-f0-9]{32}$`, 'i')
const caminho = (clienteId) => {
  if (!new RegExp(`^${GUID}$`, 'i').test(clienteId)) throw new Error('Cliente inválido.')
  return `/api/clientes/${clienteId}/foto`
}

export function obterFotoCliente(fotoUrl, sinal) {
  // Somente a rota privada emitida pela API. Não recebe URL externa nem credencial na query.
  if (!ROTA_FOTO.test(fotoUrl)) throw new Error('Endereço da foto inválido.')
  return chamarApi(fotoUrl, { arquivo: true, sinal })
}

export function enviarFotoCliente(clienteId, arquivo) {
  const formulario = new FormData()
  formulario.append('file', arquivo)
  return chamarApi(caminho(clienteId), { metodo: 'POST', formulario })
}

export const removerFotoCliente = clienteId => chamarApi(caminho(clienteId), { metodo: 'DELETE' })
