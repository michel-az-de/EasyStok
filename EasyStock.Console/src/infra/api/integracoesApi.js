import { chamarApi } from './cliente'

// Integrações da loja (F16, #1246): `api/integracoes`, policy Admin. A API nunca devolve segredo;
// o que vai no PUT sai daqui direto, sem passar por estado da aplicação.
const BASE = '/api/integracoes'

export const listar = () => chamarApi(BASE)

export const salvarChave = (provider, campos, ambiente = null) =>
  chamarApi(`${BASE}/${provider}`, { metodo: 'PUT', corpo: ambiente ? { campos, ambiente } : { campos } })

export const testar = (provider) => chamarApi(`${BASE}/${provider}/testar`, { metodo: 'POST' })

export const desativar = (provider) => chamarApi(`${BASE}/${provider}/desativar`, { metodo: 'POST' })
