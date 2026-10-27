const config = require('/usr/src/wpp-server/dist/config.js').default
const fs = require('node:fs')
const path = require('node:path')
const perfis = '/usr/src/wpp-server/userDataDir'
// Executado sob flock, antes do servidor: a única instância remove apenas as travas
// efêmeras do Chromium deixadas no volume por um contêiner anterior. Mantém o perfil e as chaves.
for (const entrada of fs.readdirSync(perfis, { withFileTypes: true })) {
  if (!entrada.isDirectory() || !/^ez-[a-f0-9]{32}$/i.test(entrada.name)) continue
  for (const nome of ['SingletonLock', 'SingletonCookie', 'SingletonSocket']) {
    const arquivo = path.join(perfis, entrada.name, nome)
    try {
      if (fs.lstatSync(arquivo).isSymbolicLink()) fs.unlinkSync(arquivo)
    } catch (erro) {
      if (erro.code !== 'ENOENT') throw erro
    }
  }
}
config.webhook.autoDownload = false
config.webhook.allUnreadOnStart = true
config.webhook.readMessage = false
config.webhook.onSelfMessage = false
config.webhook.onPresenceChanged = false
config.webhook.onParticipantsChanged = false
config.createOptions.autoClose = 0
// Mantém os padrões seguros da biblioteca; a imagem acrescenta flags que
// desativam cache, segurança de origem e validação TLS sem necessidade.
config.createOptions.browserArgs = ['--no-sandbox', '--disable-dev-shm-usage']
// A versão fixada só repete falhas de rede. Repetir também recusas temporárias
// da nossa API; a idempotência do webhook impede duplicar mensagens já gravadas.
const http = require('axios')
http.interceptors.response.use(undefined, async erro => {
  const requisicao = erro.config
  const status = erro.response?.status
  if (requisicao?.url !== config.webhook.url || !(status === 429 || status >= 500)) throw erro
  const tentativa = (requisicao.easystokRetry || 0) + 1
  if (tentativa >= config.webhook.retryAttempts) throw erro
  await new Promise(resolve => setTimeout(resolve, Math.min(config.webhook.retryDelay * 2 ** (tentativa - 1), 60000)))
  requisicao.easystokRetry = tentativa
  return http.request(requisicao)
})
console.info('[EasyStok WPP] configuração carregada', JSON.stringify({
  autoClose: config.createOptions.autoClose,
  recuperarNaoLidas: config.webhook.allUnreadOnStart,
  midiaInline: config.webhook.autoDownload,
}))
