// Service worker do console (#1426): só os avisos Web Push do EasyStok. Não tem `fetch`, então
// não guarda nem intercepta nada; o console continua indo à rede como sempre.
//
// Carga do EasyStok (WebPushCanal): { title, body, tag, data }. Sem carga legível, aviso genérico.

self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (evento) => evento.waitUntil(self.clients.claim()))

self.addEventListener('push', (evento) => {
  let carga = {}
  if (evento.data) {
    try { carga = evento.data.json() } catch { carga = { body: evento.data.text() } }
  }
  const tag = carga.tag || 'easystok-aviso'
  evento.waitUntil((async () => {
    await self.registration.showNotification(carga.title || 'EasyStok', {
      body: carga.body || 'Tem novidade no atendimento.',
      tag,
      data: carga.data || {},
    })
    // #1807: com o console na frente, o aviso já apareceu na tela (toast). O navegador exige mostrar algo a cada
    // push, então mostra e fecha. Com a aba escondida, a tag igual à do console substitui em vez de duplicar.
    if (await consoleNaFrente()) {
      for (const aviso of await self.registration.getNotifications({ tag })) aviso.close()
    }
  })())
})

async function consoleNaFrente() {
  const janelas = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
  return janelas.some((janela) => janela.url.startsWith(self.registration.scope) && janela.visibilityState === 'visible' && janela.focused)
}

// Tocar no aviso traz o console para a frente; sem aba aberta, abre uma.
self.addEventListener('notificationclick', (evento) => {
  evento.notification.close()
  evento.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((janelas) => {
    const doConsole = janelas.find((janela) => janela.url.startsWith(self.registration.scope))
    if (doConsole) return doConsole.focus()
    return self.clients.openWindow(self.registration.scope)
  }))
})
