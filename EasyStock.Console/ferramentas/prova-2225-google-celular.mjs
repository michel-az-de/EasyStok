/* eslint-disable no-console */
// Prova #2225: no Chrome do Android, o botão "Entrar com Google" abria uma aba nova
// (accounts.google.com/gsi/transform) que ficava em branco e não devolvia o login ao console.
// Lá o botão passa a pedir ao Google o seletor de conta do próprio Chrome (FedCM). Nos demais
// navegadores fica o popup de hoje: o Google não avisa quando o FedCM falha, e um Chromium sem a
// tela do FedCM deixaria o botão mudo (medido no navegador embutido).
// Esta prova guarda a configuração enviada ao Google; o efeito no aparelho se confere no Android.
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
    return proximo(spec, ctx)
  },
  load(url, ctx, proximo) {
    if (url === arquivo.href) return { format: 'module', shortCircuit: true, source: compilado }
    return proximo(url, ctx)
  },
})
const { BotaoGoogle } = await import(arquivo.href)
const { fedcmNoBotaoGoogle, opcoesDaEntradaGoogle } = await import('../src/infra/entradaGoogle.js')

const chrome = (versao, extra = '') => `Mozilla/5.0 (Linux; Android 14; K${extra}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/${versao}.0.0.0 Mobile Safari/537.36`
const AGENTES = {
  chromeAndroid: [chrome(142), true],
  chromeAndroid132: [chrome(132), true],
  chromeAndroid131: [chrome(131), false],
  webviewAndroid: ['Mozilla/5.0 (Linux; Android 14; K; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/142.0.0.0 Mobile Safari/537.36', false],
  samsung: ['Mozilla/5.0 (Linux; Android 14; SM-S911B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/28.0 Chrome/136.0.0.0 Mobile Safari/537.36', false],
  edgeAndroid: [chrome(142) + ' EdgA/142.0.0.0', false],
  operaAndroid: [chrome(142) + ' OPR/90.0.0.0', false],
  yandexAndroid: [chrome(142) + ' YaBrowser/25.10.0.0', false],
  firefoxAndroid: ['Mozilla/5.0 (Android 14; Mobile; rv:144.0) Gecko/144.0 Firefox/144.0', false],
  chromeIphone: ['Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/142.0.0.0 Mobile/15E148 Safari/604.1', false],
  chromeWindows: ['Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36', false],
  electron: ['Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Claude/1.0 Chrome/142.0.0.0 Electron/39.0.0 Safari/537.36', false],
}
const usarAgente = (agente) => Object.defineProperty(globalThis, 'navigator', { value: { userAgent: agente }, configurable: true })

const esperar = async () => { for (let i = 0; i < 30; i++) await Promise.resolve() }

// Monta o botão real com o Google falso e registra, em ordem, o que o componente pediu.
async function montar(opcoes) {
  const pedidos = [], inicios = []
  globalThis.window = { google: { accounts: { id: {
    initialize: (config) => { pedidos.push('initialize'); inicios.push(config) },
    renderButton: (alvo, config) => { pedidos.push('renderButton'); alvo.filhos.push({ botao: config }) },
    prompt: () => pedidos.push('prompt'),
  } } } }
  // Um botão de uma montagem anterior já está no alvo (#1354).
  const elemento = { filhos: [{ botao: 'antigo' }], replaceChildren() { pedidos.push('replaceChildren'); this.filhos = [] } }
  const tokens = [], falhas = []
  const props = {
    buscarClientId: async () => 'client-id-fixture',
    aoReceberToken: (token) => tokens.push(token),
    aoFalhar: (msg) => falhas.push(msg),
    opcoes: opcoes === null ? null : () => opcoes,
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
  assert.deepEqual(falhas, [])
  return { pedidos, inicios, elemento, tokens }
}

let passos = 0
const ok = (texto) => { passos += 1; console.log(`ok ${passos} - ${texto}`) }

for (const [nome, [agente, esperado]] of Object.entries(AGENTES)) {
  assert.equal(fedcmNoBotaoGoogle(agente), esperado, `FedCM no botão para ${nome}`)
}
assert.equal(fedcmNoBotaoGoogle(''), false, 'sem agente, fica o popup')
assert.equal(fedcmNoBotaoGoogle(undefined), false)
ok('só o Chrome do Android 132 ou mais pede o FedCM no botão; os demais ficam no popup')

usarAgente(AGENTES.chromeAndroid[0])
assert.equal(opcoesDaEntradaGoogle().fedcm_no_botao, true, 'a opção sai do navegador em uso')
usarAgente(AGENTES.chromeWindows[0])
assert.equal(opcoesDaEntradaGoogle().fedcm_no_botao, false)
ok('as opções da entrada Google levam a escolha do FedCM ao botão')

{
  const { inicios } = await montar({ auto_select: false, oferecer: false, fedcm_no_botao: true })
  assert.equal(inicios.length, 1, 'o Google é iniciado uma vez')
  assert.equal(inicios[0].client_id, 'client-id-fixture')
  assert.equal(inicios[0].use_fedcm_for_button, true, 'no Chrome do Android o botão pede o FedCM')
  assert.equal(inicios[0].use_fedcm_for_prompt, true, 'o One Tap segue pelo FedCM')
  assert.equal(inicios[0].ux_mode, undefined, 'sem sair da página do console')
  ok('no Chrome do Android o botão envia use_fedcm_for_button ao Google')
}

{
  const sem = await montar({ auto_select: false, oferecer: false, fedcm_no_botao: false })
  assert.equal(sem.inicios[0].use_fedcm_for_button, false, 'fora do Chrome do Android fica o popup')
  assert.equal(sem.inicios[0].use_fedcm_for_prompt, true)
  const nulo = await montar(null)
  assert.equal(nulo.inicios[0].use_fedcm_for_button, false, 'sem opções, fica o popup')
  ok('no computador, no iPhone e sem opções o botão continua no popup de hoje')
}

{
  const { pedidos, elemento, inicios, tokens } = await montar({ auto_select: true, oferecer: true, fedcm_no_botao: true })
  assert.deepEqual(pedidos, ['initialize', 'replaceChildren', 'renderButton', 'prompt'], 'inicia, limpa, desenha e só então oferece')
  assert.equal(elemento.filhos.length, 1, 'um botão só no alvo (#1354)')
  assert.equal(elemento.filhos[0].botao.locale, 'pt-BR')
  inicios[0].callback({ credential: 'token-fixture' })
  assert.deepEqual(tokens, ['token-fixture'], 'o token do Google chega a quem faz o login')
  ok('o botão é desenhado uma vez e o token devolvido pelo Google chega ao login do console')
}

{
  const sem = await montar({ auto_select: false, oferecer: false, fedcm_no_botao: true })
  assert.equal(sem.pedidos.includes('prompt'), false, 'depois de Sair não oferece entrar sozinho (#1810)')
  assert.equal(sem.inicios[0].auto_select, false)
  const com = await montar({ auto_select: true, oferecer: true, fedcm_no_botao: false })
  assert.equal(com.pedidos.filter((p) => p === 'prompt').length, 1, 'na primeira visita oferece o One Tap (#1810)')
  assert.equal(com.inicios[0].auto_select, true)
  ok('o One Tap segue a regra de entrar sozinho da #1810')
}

console.log(`\n${passos} passos verdes.`)
