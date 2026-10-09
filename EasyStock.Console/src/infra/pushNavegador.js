const ARQUIVO_SW = 'sw-avisos.js'
const CHAVE_DONO = 'easystok.push.dono'
const urlDoServiceWorker = () => new URL(ARQUIVO_SW, window.location.href)
export const pushSuportado = () => typeof window !== 'undefined'
  && typeof navigator !== 'undefined' && window.isSecureContext !== false
  && 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window
export const permissaoPush = () => pushSuportado() ? Notification.permission : 'indisponivel'
export const pedirPermissaoPush = () => Notification.requestPermission()

const dono = () => { try { return JSON.parse(localStorage.getItem(CHAVE_DONO)) } catch { return null } }
export const associarInscricaoPush = (identidade, endpoint) => localStorage.setItem(CHAVE_DONO, JSON.stringify({ identidade, endpoint }))
const comPrazo = (promessa) => {
  let timer
  return Promise.race([promessa, new Promise((_, rejeitar) => {
    timer = setTimeout(() => rejeitar(new Error('O navegador demorou para responder. Tente novamente.')), 10000)
  })]).finally(() => clearTimeout(timer))
}
const doConsole = (registro) => (registro?.active ?? registro?.waiting ?? registro?.installing)?.scriptURL === urlDoServiceWorker().href
const registroAtual = async () => {
  if (!pushSuportado()) return null
  const registro = await comPrazo(navigator.serviceWorker.getRegistration(urlDoServiceWorker().href))
  return doConsole(registro) ? registro : null
}

export function chaveDeBase64Url(base64Url) {
  const base64 = (base64Url + '='.repeat((4 - (base64Url.length % 4)) % 4)).replace(/-/g, '+').replace(/_/g, '/')
  return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0))
}
const comoInscricao = (assinatura) => {
  const { endpoint, keys } = assinatura.toJSON()
  return { endpoint, p256dh: keys.p256dh, auth: keys.auth, userAgent: navigator.userAgent }
}
export async function inscricaoPushAtual(identidade) {
  const registro = await registroAtual()
  const assinatura = await registro?.pushManager.getSubscription()
  const vinculo = dono()
  return assinatura && vinculo?.identidade === identidade && vinculo.endpoint === assinatura.endpoint ? comoInscricao(assinatura) : null
}

// Um login novo não reaproveita o endpoint de outra pessoa ou empresa.
export async function prepararPushParaSessao(identidade) {
  if (dono()?.identidade !== identidade) await desinscreverPushNoNavegador()
}

export async function desinscreverPushNoNavegador(identidade = null, endpoint = null) {
  const vinculo = dono()
  if (identidade && vinculo?.identidade !== identidade) return
  const registro = await registroAtual()
  if (!registro) return
  const assinatura = await registro.pushManager.getSubscription()
  if (endpoint && assinatura?.endpoint !== endpoint) return
  for (const aviso of await registro.getNotifications()) aviso.close()
  if (assinatura && !await comPrazo(assinatura.unsubscribe())) throw new Error('Não foi possível desligar os avisos neste aparelho.')
  localStorage.removeItem(CHAVE_DONO)
}

export async function inscreverPushNoNavegador(chavePublica, identidade) {
  const url = urlDoServiceWorker()
  const anterior = await navigator.serviceWorker.getRegistration(url.href)
  if (anterior && !doConsole(anterior)) throw new Error('Outro aplicativo usa esta área do navegador. Abra o Console no endereço próprio.')
  const registro = await comPrazo(navigator.serviceWorker.register(url.href, { scope: new URL('./', url).href }))
  await comPrazo(navigator.serviceWorker.ready)
  if (!doConsole(registro)) throw new Error('O serviço de avisos ainda não está pronto. Tente novamente.')
  const chave = chaveDeBase64Url(chavePublica)
  let assinatura = await registro.pushManager.getSubscription()
  const antiga = assinatura?.options?.applicationServerKey
  const mesmaChave = antiga && new Uint8Array(antiga).length === chave.length
    && new Uint8Array(antiga).every((byte, i) => byte === chave[i])
  if (assinatura && (!mesmaChave || dono()?.identidade !== identidade)) {
    if (!await comPrazo(assinatura.unsubscribe())) throw new Error('Não foi possível renovar os avisos neste aparelho.')
    assinatura = null
  }
  assinatura ??= await comPrazo(registro.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: chave }))
  return comoInscricao(assinatura)
}
