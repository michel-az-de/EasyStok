import { chamarApi } from './cliente'

// Os avisos InApp são consultados exclusivamente por notificacoesDaSessao.js.
const LEMBRETES = '/api/atendimento/lembretes'
const PUSH = '/api/pwa/push'
const prazo = () => AbortSignal.timeout(15000)

export const listarLembretes = () => chamarApi(LEMBRETES, { sinal: prazo() })
export const criarLembreteNaApi = (corpo, chave) =>
  chamarApi(LEMBRETES, { metodo: 'POST', corpo, chave, sinal: prazo() })
export const concluirLembreteNaApi = (id) =>
  chamarApi(`${LEMBRETES}/${encodeURIComponent(id)}/concluir`, { metodo: 'POST', sinal: prazo() })
export const marcarLembretesVistosNaApi = () =>
  chamarApi(`${LEMBRETES}/vistos`, { metodo: 'POST', sinal: prazo() })

export const obterChavePush = () =>
  chamarApi(`${PUSH}/vapid-public`, { autenticado: false, sinal: prazo() })
export const inscreverPush = ({ endpoint, p256dh, auth, userAgent = null }) =>
  chamarApi(`${PUSH}/subscribe`, { metodo: 'POST', corpo: { endpoint, p256dh, auth, userAgent }, sinal: prazo() })