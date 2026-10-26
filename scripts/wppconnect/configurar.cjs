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
console.info('[EasyStok WPP] configuração carregada', JSON.stringify({
  autoClose: config.createOptions.autoClose,
  recuperarNaoLidas: config.webhook.allUnreadOnStart,
  midiaInline: config.webhook.autoDownload,
}))
