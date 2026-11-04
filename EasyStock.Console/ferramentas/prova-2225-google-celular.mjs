/* eslint-disable no-console */
// Prova #2225: no Chrome do Android, o botão "Entrar com Google" abria uma aba nova
// (accounts.google.com/gsi/transform) que ficava em branco e não devolvia o login ao console.
// Lá o botão passa a usar o seletor de conta do próprio Chrome (FedCM), sem aba nova. Nos demais
// navegadores fica o popup de hoje: o Google não avisa quando o FedCM falha, e um Chromium sem a
// tela do FedCM deixaria o botão mudo (medido no navegador embutido).
//
//   node ferramentas/prova-2225-google-celular.mjs

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'

// React mínimo: estado, ref e efeito com dependências, como o React faz depois de desenhar.
let host
const mudou = (antes, depois) => !antes || !depois || depois.some((d, i) => !Object.is(d, antes[i]))
globalThis.__prova2225 = {
  useState(inicial) {
    const h = host, i = h.indice++
    if (!(i in h.valores)) h.valores[i] = inicial
    return [h.valores[i], (valor) => { h.valores[i] = valor; h.agendar() }]
  },
  useRef(inicial) { const h = host, i = h.indice++; return h.valores[i] ??= { current: inicial } },
  useEffect(efeito, deps) {
    const h = host, i = h.indice++, anterior = h.valores[i]
    if (!mudou(anterior?.deps, deps)) return
    h.pendentes.push(() => { anterior?.limpar?.(); h.valores[i] = { deps, limpar: efeito() } })
  },
}

const arquivo = new URL('../src/features/login/BotaoGoogle.jsx', import.meta.url)
const compilado = (await transformWithOxc(readFileSync(arquivo, 'utf8'), arquivo.pathname, { jsx: { runtime: 'automatic' } })).code
const codigo = (texto) => 'data:text/javascript,' + encodeURIComponent(texto)
registerHooks({
  resolve(spec, ctx, proximo) {
    if (spec === 'react') return { url: codigo('const h=globalThis.__prova2225;export const useState=h.useState,useRef=h.useRef,useEffect=h.useEffect'), shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: codigo('export const jsx=(type,props)=>({type,props});export const jsxs=jsx'), shortCircuit: true }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return proximo(spec + '.js', ctx)
    return proximo(spec, ctx)
  },
  load(url, ctx, proximo) {
    if (url === arquivo.href) return { format: 'module', shortCircuit: true, source: compilado }
    return proximo(url, ctx)
  },
})
const { BotaoGoogle } = await import(arquivo.href)
const { fedcmNoBotaoGoogle } = await import('../src/infra/entradaGoogle.js')

const AGENTES = {
  chromeAndroid: 'Mozilla/5.0 (Linux; Android 14; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Mobile Safari/537.36',
  chromeAndroidAntigo: 'Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36',
  webviewAndroid: 'Mozilla/5.0 (Linux; Android 14; K; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/142.0.0.0 Mobile Safari/537.36',
  samsung: 'Mozilla/5.0 (Linux; Android 14; SM-S911B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/26.0 Chrome/122.0.0.0 Mobile Safari/537.36',
  edgeAndroid: 'Mozilla/5.0 (Linux; Android 14; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Mobile Safari/537.36 EdgA/142.0.0.0',
  operaAndroid: 'Mozilla/5.0 (Linux; Android 14; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Mobile Safari/537.36 OPR/90.0.0.0',
  firefoxAndroid: 'Mozilla/5.0 (Android 14; Mobile; rv:144.0) Gecko/144.0 Firefox/144.0',
  chromeIphone: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/142.0.0.0 Mobile/15E148 Safari/604.1',
  chromeWindows: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36',
  electron: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Claude/1.0 Chrome/142.0.0.0 Electron/39.0.0 Safari/537.36',
}
const usarAgente = (agente) => Object.defineProperty(globalThis, 'navigator', { value: { userAgent: agente }, configurable: true })

const esperar = async () => { for (let i = 0; i < 30; i++) await Promise.resolve() }

// Monta o botão real com o Google falso; devolve o que o componente pediu ao Google.
async function montar({ oferecer, agente = AGENTES.chromeAndroid }) {
  usarAgente(agente)
  const google = { inicios: [], botoes: [], prompts: 0, limpezas: 0 }
  globalThis.window = { google: { accounts: { id: {
    initialize: (config) => google.inicios.push(config),
    renderButton: (elemento, config) => google.botoes.push({ elemento, config }),
    prompt: () => { google.prompts += 1 },
  } } } }
  const elemento = { replaceChildren: () => { google.limpezas += 1 } }
  const tokens = [], falhas = []
  const props = {
    buscarClientId: async () => 'client-id-fixture',
    aoReceberToken: (token) => tokens.push(token),
    aoFalhar: (msg) => falhas.push(msg),
    opcoes: () => ({ auto_select: oferecer, oferecer }),
  }
  const h = { valores: [], indice: 0, pendentes: [], agendado: false }
  h.agendar = () => { if (!h.agendado) { h.agendado = true; queueMicrotask(() => { h.agendado = false; desenhar() }) } }
  const desenhar = () => {
    host = h; h.indice = 0; h.pendentes = []
    const arvore = BotaoGoogle(props)
    if (arvore?.props?.ref) arvore.props.ref.current ??= elemento
    h.pendentes.forEach((rodar) => rodar())
  }
  desenhar()
  await esperar()
  return { google, elemento, tokens, falhas }
}

let passos = 0
const ok = (texto) => { passos += 1; console.log(`ok ${passos} - ${texto}`) }

{
  const sim = ['chromeAndroid']
  for (const [nome, agente] of Object.entries(AGENTES)) {
    assert.equal(fedcmNoBotaoGoogle(agente), sim.includes(nome), `FedCM no botão para ${nome}`)
  }
  assert.equal(fedcmNoBotaoGoogle(''), false, 'sem agente, fica o popup')
  assert.equal(fedcmNoBotaoGoogle(undefined), false)
  ok('só o Chrome do Android (132 ou mais) usa o FedCM no botão; os demais ficam no popup')
}

{
  const { google, falhas } = await montar({ oferecer: false, agente: AGENTES.chromeAndroid })
  assert.deepEqual(falhas, [])
  assert.equal(google.inicios.length, 1, 'o Google é iniciado uma vez')
  const config = google.inicios[0]
  assert.equal(config.client_id, 'client-id-fixture')
  assert.equal(config.use_fedcm_for_button, true, 'no Chrome do Android o botão usa o seletor de conta do Chrome (FedCM), sem aba nova')
  assert.equal(config.ux_mode, undefined, 'sem sair da página do console')
  ok('no Chrome do Android o botão pede o FedCM, que não abre a aba que ficava em branco')
}

{
  for (const nome of ['chromeWindows', 'electron', 'chromeIphone']) {
    const { google } = await montar({ oferecer: false, agente: AGENTES[nome] })
    assert.equal(google.inicios[0].use_fedcm_for_button, false, `${nome} fica no popup de hoje`)
  }
  ok('no computador e no iPhone o botão continua no popup de hoje')
}

{
  const { google, elemento, tokens } = await montar({ oferecer: false })
  assert.equal(google.limpezas, 1, 'limpa o alvo antes de desenhar (#1354)')
  assert.equal(google.botoes.length, 1)
  assert.equal(google.botoes[0].elemento, elemento)
  assert.equal(google.botoes[0].config.locale, 'pt-BR')
  google.inicios[0].callback({ credential: 'token-fixture' })
  assert.deepEqual(tokens, ['token-fixture'], 'o token do Google chega a quem faz o login')
  ok('o token devolvido pelo Google chega ao login do console')
}

{
  const sem = await montar({ oferecer: false })
  assert.equal(sem.google.prompts, 0, 'depois de Sair não oferece entrar sozinho (#1810)')
  assert.equal(sem.google.inicios[0].auto_select, false)
  const com = await montar({ oferecer: true })
  assert.equal(com.google.prompts, 1, 'na primeira visita oferece o One Tap (#1810)')
  assert.equal(com.google.inicios[0].auto_select, true)
  ok('o One Tap segue a regra de entrar sozinho da #1810')
}

console.log(`\n${passos} passos verdes.`)
