import { chamarApi } from './cliente'

// Conexão do WhatsApp da loja por coexistência (#1417): `api/integracoes/whatsapp/coexistencia`.
const BASE = '/api/integracoes/whatsapp/coexistencia'

// { appId, configId, graphVersion, habilitado }: só valores públicos.
export const obterConfigCoexistencia = () => chamarApi(`${BASE}/config`)

export const conectarCoexistencia = ({ code, wabaId, phoneNumberId }) =>
  chamarApi(BASE, { metodo: 'POST', corpo: { code, wabaId, phoneNumberId } })

// Estado do canal (#1447, tela Canais): `api/integracoes/whatsapp/status` (Admin).
// { phoneNumberId, apiVersion, webhookVerificadoEm, ultimaMensagemRecebidaEm, provider };
// 404 quando o atendimento não está ligado para a loja.
export const obterStatusWhatsApp = () => chamarApi('/api/integracoes/whatsapp/status')
