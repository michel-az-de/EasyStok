/* eslint-disable no-console */
// Prova da #2377: a impressão automática do canhoto não confirma sozinha uma folha que talvez não saiu
// e não finge estar "ligada" com a fila parada. Regras puras de `componentes/medirPrint.js` e `dominio/impressaoAutomatica.js`.
//
//   node ferramentas/prova-2377-impressao-honesta.mjs

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const dominio = await import('../src/dominio/impressaoAutomatica.js')
const { LIMITE_DIALOGO_MS, destinoDoPrint, medirPrint } = await import('../src/componentes/medirPrint.js')
const {
  FILA_SAUDAVEL, registrarListagem, textoFilaParada, textoDeConferir,
} = dominio

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

await confere('print() que volta na hora (kiosk-printing) é impressão silenciosa', () => {
  assert.equal(destinoDoPrint(40), 'dialogo')
  assert.equal(destinoDoPrint(LIMITE_DIALOGO_MS - 1), 'dialogo')
})

await confere('print() que demorou ao menos o limite teve diálogo humano: a folha precisa de conferência', () => {
  assert.equal(destinoDoPrint(LIMITE_DIALOGO_MS), 'dialogo-lento')
  assert.equal(destinoDoPrint(30000), 'dialogo-lento')
})

await confere('medirPrint mede o tempo que o print() segurou o JS', () => {
  let agora = 1000
  const relogio = () => agora
  assert.equal(medirPrint(() => { agora += 20 }, relogio), 'dialogo')
  assert.equal(medirPrint(() => { agora += 6000 }, relogio), 'dialogo-lento')
})

await confere('o quadro de impressão usa a medição em vez de dar o print() por concluído', () => {
  const fonte = readFileSync(new URL('../src/componentes/htmlNoNavegador.js', import.meta.url), 'utf8')
  assert.match(fonte, /medirPrint\(/)
  assert.doesNotMatch(fonte, /print\(\)\s*\n\s*concluir\('dialogo'\)/)
})

await confere('listagem que falha conta falhas seguidas e guarda desde quando', () => {
  const um = registrarListagem(FILA_SAUDAVEL, false, 1000)
  const dois = registrarListagem(um, false, 2000)
  assert.deepEqual(um, { falhas: 1, desde: 1000 })
  assert.deepEqual(dois, { falhas: 2, desde: 1000 })
})

await confere('listagem que volta a funcionar zera o contador', () => {
  const parada = registrarListagem(registrarListagem(FILA_SAUDAVEL, false, 1000), false, 2000)
  assert.deepEqual(registrarListagem(parada, true, 3000), FILA_SAUDAVEL)
  assert.deepEqual(registrarListagem(FILA_SAUDAVEL, true, 3000), FILA_SAUDAVEL)
})

await confere('"Fila parada desde HH:MM" usa o fuso da loja', () => {
  // 15:05 UTC = 12:05 em America/Sao_Paulo
  assert.equal(textoFilaParada(Date.parse('2026-11-12T15:05:00Z')), 'Fila parada desde 12:05')
})

await confere('aviso de conferência nomeia o pedido', () => {
  assert.match(textoDeConferir({ numero: 1234 }), /Confira se a folha do pedido 1234 saiu/)
  assert.match(textoDeConferir({}), /Confira se a folha/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
