// Web Push do console (#1426): o aviso chega com o console fechado, no celular ou no computador.
// O service worker (`public/sw-avisos.js`) só mostra a notificação que o EasyStok empurra
// (ConversaEscalada, LembreteVencido); não intercepta requisição nenhuma.
//
// O endereço do service worker é relativo à página: o console usa rota por hash, então a pasta
// da página é a pasta do console, esteja ele na raiz ou num caminho.

const ARQUIVO_SW = 'sw-avisos.js'

const urlDoServiceWorker = () => new URL(ARQUIVO_SW, window.location.href)

export const pushSuportado = () => typeof window !== 'undefined'
  && 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window

export const permissaoPush = () => (pushSuportado() ? Notification.permission : 'indisponivel')

// Precisa vir de um clique: navegador nenhum concede sem gesto.
export const pedirPermissaoPush = () => Notification.requestPermission()

// Chave VAPID pública (base64url) para o `applicationServerKey` do PushManager.
export function chaveDeBase64Url(base64Url) {
  const base64 = (base64Url + '='.repeat((4 - (base64Url.length % 4)) % 4)).replace(/-/g, '+').replace(/_/g, '/')
  const bruto = atob(base64)
  return Uint8Array.from(bruto, (c) => c.charCodeAt(0))
}

const mesmosBytes = (a, b) => a.length === b.length && a.every((byte, i) => byte === b[i])

// O que a API guarda da inscrição (`POST api/pwa/push/subscribe`).
const comoInscricao = (assinatura) => {
  const { endpoint, keys } = assinatura.toJSON()
  return { endpoint, p256dh: keys.p256dh, auth: keys.auth, userAgent: navigator.userAgent }
}

export async function inscricaoPushAtual() {
  if (!pushSuportado()) return null
  const registro = await navigator.serviceWorker.getRegistration(urlDoServiceWorker().href)
  const assinatura = await registro?.pushManager.getSubscription()
  return assinatura ? comoInscricao(assinatura) : null
}

// Registra o service worker, assina com a chave do servidor e devolve a inscrição. Assinatura
// antiga feita com outra chave VAPID (chave trocada no servidor) é desfeita antes: o navegador
// recusa assinar de novo com chave diferente.
export async function inscreverPushNoNavegador(chavePublica) {
  const url = urlDoServiceWorker()
  await navigator.serviceWorker.register(url.href, { scope: new URL('./', url).href })
  const registro = await navigator.serviceWorker.ready
  const chave = chaveDeBase64Url(chavePublica)
  let assinatura = await registro.pushManager.getSubscription()
  const chaveAntiga = assinatura?.options?.applicationServerKey
  if (assinatura && chaveAntiga && !mesmosBytes(new Uint8Array(chaveAntiga), chave)) {
    await assinatura.unsubscribe()
    assinatura = null
  }
  assinatura ??= await registro.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: chave })
  return comoInscricao(assinatura)
}
