/* eslint-disable no-console */
// Prova #1810 (revisão gravada de 14/10): com a conta Google já escolhida antes, o console entra
// sozinho (One Tap com auto_select); depois de "Sair" nesta aba, não entra sozinho de novo até a
// pessoa escolher a conta outra vez.
//
//   node ferramentas/prova-1810-entrada-google.mjs

import assert from 'node:assert/strict'

const guardado = new Map()
globalThis.sessionStorage = {
  getItem: (k) => (guardado.has(k) ? guardado.get(k) : null),
  setItem: (k, v) => guardado.set(k, String(v)),
  removeItem: (k) => guardado.delete(k),
}
const chamadas = []
globalThis.window = { google: { accounts: { id: { disableAutoSelect: () => chamadas.push('disableAutoSelect') } } } }

const { opcoesDaEntradaGoogle, travarEntradaSozinha, liberarEntradaSozinha } = await import('../src/infra/entradaGoogle.js')

let passos = 0
const ok = (texto) => { passos += 1; console.log(`ok ${passos} - ${texto}`) }

assert.equal(opcoesDaEntradaGoogle().auto_select, true, 'primeira visita: entra sozinho com a conta já escolhida')
assert.equal(opcoesDaEntradaGoogle().oferecer, true)
ok('sem saída nesta aba, o login com Google tenta entrar sozinho')

travarEntradaSozinha()
assert.deepEqual(chamadas, ['disableAutoSelect'], 'avisa o Google para não escolher a conta sozinho')
assert.equal(opcoesDaEntradaGoogle().auto_select, false, 'depois de Sair não entra sozinho')
assert.equal(opcoesDaEntradaGoogle().oferecer, false)
ok('depois de "Sair", a tela de login fica parada até a pessoa escolher')

liberarEntradaSozinha()
assert.equal(opcoesDaEntradaGoogle().auto_select, true)
ok('quem entra de novo pelo botão volta a entrar sozinho nas próximas visitas')

globalThis.sessionStorage = { getItem: () => { throw new Error('bloqueado') }, setItem: () => { throw new Error('bloqueado') }, removeItem: () => {} }
assert.doesNotThrow(() => travarEntradaSozinha())
assert.equal(opcoesDaEntradaGoogle().auto_select, true)
ok('sem armazenamento do navegador, não quebra')

console.log(`\n${passos} passos verdes.`)
