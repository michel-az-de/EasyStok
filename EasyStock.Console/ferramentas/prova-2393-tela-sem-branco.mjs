// Prova da #2393: a tela não fica branca e as falhas silenciosas ficam visíveis.
//
// 1. LimiteDeErro envolve a raiz (main.jsx), o Hall e os modais (App.jsx).
// 2. Falha global (unhandledrejection/error) vira faixa e registro no console.
// 3. Áudio suspenso ao hibernar é re-armado no statechange e na volta de foco.
// 4. Som bloqueado e notificação negada aparecem como aviso fora do ModalAutomacoes.
// 5. O build de produção falha sem a meta do SHA.
//
// Roda com: node ferramentas/prova-2393-tela-sem-branco.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(especificador, contexto)
  },
})

const ler = (caminho) => readFileSync(new URL(`../${caminho}`, import.meta.url), 'utf8')

// 1. Contenção de erro
const main = ler('src/main.jsx')
assert.match(main, /<LimiteDeErro[^>]*>[\s\S]*<ProvedorDeAviso>[\s\S]*<App \/>[\s\S]*<\/LimiteDeErro>/, 'main.jsx: o LimiteDeErro de raiz envolve o provedor e o App')
assert.match(main, /instalarFalhaGlobal\(window\)/, 'main.jsx instala os handlers globais')
assert.match(ler('src/componentes/LimiteDeErro.jsx'), /Recarregar'/, 'a raiz oferece "Recarregar"')
const app = ler('src/app/App.jsx')
assert.equal((app.match(/<LimiteDeErro parte="Início"><HallDeModulos/g) ?? []).length, 2, 'App.jsx: os dois Halls ficam em LimiteDeErro')
assert.match(app, /<LimiteDeErro parte="Janela aberta">[\s\S]*PainelGaleria[\s\S]*ModalEncerrar[\s\S]*<\/LimiteDeErro>/, 'App.jsx: os modais ficam em LimiteDeErro')

// 2. Falha global
const { criarFaixaDeFalha, instalarFalhaGlobal, TEXTO_DA_FALHA } = await import('../src/infra/falhaGlobal.js')
assert.equal(TEXTO_DA_FALHA, 'Algo falhou. Confira o que estava fazendo.')
{
  const faixa = criarFaixaDeFalha()
  const ouvintes = {}
  const janela = { addEventListener: (n, f) => { ouvintes[n] = f }, removeEventListener: (n) => { delete ouvintes[n] } }
  const logs = []
  const desinstalar = instalarFalhaGlobal(janela, faixa, { error: (...a) => logs.push(a) })
  let avisos = 0
  faixa.assinar(() => { avisos += 1 })
  assert.equal(faixa.ver().falhou, false)
  ouvintes.unhandledrejection({ reason: new Error('x') })
  assert.equal(faixa.ver().falhou, true, 'promessa rejeitada acende a faixa')
  assert.equal(logs.length, 1, 'e fica registrada')
  faixa.dispensar()
  assert.equal(faixa.ver().falhou, false)
  ouvintes.error({})
  assert.equal(faixa.ver().falhou, false, 'evento de erro sem erro (recurso que não carregou) é ignorado')
  ouvintes.error({ error: new Error('y'), message: 'y' })
  assert.equal(faixa.ver().falhou, true)
  assert.equal(avisos, 3)
  // #2431: eventos benignos não acendem a faixa.
  ouvintes.error({ message: 'ResizeObserver loop completed with undelivered notifications.' })
  ouvintes.error({ error: new Error('ResizeObserver loop limit exceeded'), message: 'ResizeObserver loop limit exceeded' })
  ouvintes.unhandledrejection({ reason: Object.assign(new Error('cancelado'), { name: 'AbortError' }) })
  assert.equal(faixa.ver().total, 2, 'ResizeObserver e AbortError são ignorados')
  desinstalar()
  assert.deepEqual(Object.keys(ouvintes), [])
}

// 3. Áudio re-armado
{
  const alvos = () => {
    const o = {}
    return { o, addEventListener: (n, f) => { o[n] = f }, removeEventListener: (n) => { delete o[n] } }
  }
  const ctxFalso = Object.assign(alvos(), { state: 'suspended', resume() { this.state = 'running'; this.o.statechange?.(); return Promise.resolve() } })
  globalThis.window = { AudioContext: function () { return ctxFalso } }
  const janela = alvos()
  const documento = Object.assign(alvos(), { hidden: true })
  const { observarAudio, audioDestravado } = await import('../src/infra/som.js')
  let chamadas = 0
  const parar = observarAudio(() => { chamadas += 1 }, janela, documento)
  assert.equal(audioDestravado(), false)
  janela.o.focus()
  assert.equal(ctxFalso.state, 'suspended', 'aba escondida não tenta religar')
  documento.hidden = false
  janela.o.focus()
  assert.equal(ctxFalso.state, 'running', 'na volta de foco o áudio é religado')
  assert.ok(chamadas >= 1 && audioDestravado(), 'e o aviso é atualizado')
  ctxFalso.state = 'suspended'
  ctxFalso.o.statechange()
  assert.equal(chamadas >= 2, true, 'statechange também avisa')
  parar()
  assert.deepEqual([Object.keys(ctxFalso.o), Object.keys(janela.o), Object.keys(documento.o)], [[], [], []])
}
assert.match(ler('src/aplicacao/AtendimentoProvider.jsx'), /observarAudio\(/, 'o Provider usa observarAudio')

// 4. Avisos visíveis
const { avisosDoDispositivo } = await import('../src/dominio/avisoDoDispositivo.js')
assert.deepEqual(avisosDoDispositivo({ som: true, audioBloqueado: false, permissaoNotificacao: 'granted' }), [])
assert.deepEqual(avisosDoDispositivo({ som: false, audioBloqueado: true, permissaoNotificacao: 'default' }), [], 'som desligado não incomoda')
assert.deepEqual(avisosDoDispositivo({ som: true, audioBloqueado: true, permissaoNotificacao: 'denied' }).map((a) => a.id), ['som', 'notificacao'])
assert.match(avisosDoDispositivo({ permissaoNotificacao: 'denied' })[0].texto, /cadeado/, 'diz como reativar')
assert.match(app, /<AvisosDoDispositivo[^>]*><TelaDaRota/, 'App.jsx reserva espaço para avisos sem sobrepor a tela')

// 5. Build sem SHA
const { resolverShaDoConsole, plugin } = await import('./shaDoConsole.mjs')
const sha = 'a'.repeat(40)
assert.equal(resolverShaDoConsole({ env: { CONSOLE_SHA: sha } }), sha)
assert.equal(resolverShaDoConsole({ env: {}, lerGit: () => `${sha}\n` }), sha)
assert.equal(resolverShaDoConsole({ env: { CONSOLE_SHA: 'curto' } }), null)
assert.throws(() => plugin({ exigir: true, env: {}, lerGit: () => '' }).transformIndexHtml('<head></head>'), /SHA do console/)
assert.equal(plugin({ exigir: false, env: {}, lerGit: () => '' }).transformIndexHtml('<head></head>'), '<head></head>')
assert.equal(plugin({ exigir: true, env: { CONSOLE_SHA: sha } }).transformIndexHtml('<head></head>'), `<head><meta name="easystok-console-sha" content="${sha}"></head>`)
assert.match(ler('vite.config.js'), /metaDoSha\(/, 'vite.config.js usa o plugin')

console.log('prova-2393: ok')
