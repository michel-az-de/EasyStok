import { chamarApi } from '../infra/api/cliente'

// O servidor escolhe o endereço salvo e a vitrine da empresa autenticada.
export const consultarFreteDoCliente = (clienteId, sinal) =>
  chamarApi(`/api/clientes/${encodeURIComponent(clienteId)}/frete`, { sinal })
