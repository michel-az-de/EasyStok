import { chamarApi } from './cliente'

// Conexão do WhatsApp da loja por coexistência (#1417): `api/integracoes/whatsapp/coexistencia`.
const BASE = '/api/integracoes/whatsapp/coexistencia'

// { appId, configId, graphVersion, habilitado }: só valores públicos.
export const obterConfigCoexistencia = () => chamarApi(`${BASE}/config`)

export const conectarCoexistencia = ({ code, wabaId, phoneNumberId }) =>
  chamarApi(BASE, { metodo: 'POST', corpo: { code, wabaId, phoneNumberId } })
