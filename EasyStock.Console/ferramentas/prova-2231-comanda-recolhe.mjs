/* eslint-disable no-console */
// Prova da issue #2231: com o painel "Comanda · <cliente>" solto, o "Gerar cobrança" da barra da Ficha
// abria "Como o cliente vai pagar?" embaixo do painel. Agora a barra avisa que vai perguntar o meio e a
// Ficha recolhe a Comanda; sem a Comanda solta nada muda. A geometria fica no navegador (conferida
// a 1568x900 no modo demonstração); aqui, a ligação entre as duas peças.
//
//   node ferramentas/prova-2231-comanda-recolhe.mjs

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 2).join(' ')}`)
  }
}
const fonte = (caminho) => readFileSync(new URL('../src/features/ficha-cliente/' + caminho, import.meta.url), 'utf8')
const barra = fonte('BarraProximoPasso.jsx')
const ficha = fonte('PainelFicha.jsx')

confere('1. a barra recebe aoPedirMeio', () => {
  assert.match(barra, /export function BarraProximoPasso\(\{[^}]*\baoPedirMeio\b/s)
})
confere('1. a barra avisa ao abrir a pergunta do meio, antes de perguntar', () => {
  assert.match(barra, /aoGerarPix: \(\) => \{[^}]*aoPedirMeio\?\.\(\)[^}]*setPedindoMeio\(true\)/)
})
confere('2. a Ficha recolhe a Comanda solta quando a barra pede o meio', () => {
  const chamada = ficha.match(/<BarraProximoPasso[\s\S]*?\/>/)?.[0] ?? ''
  assert.match(chamada, /aoPedirMeio=\{\(\) => \{ if \(comandaMovel\.solto\) comandaMovel\.recolher\(\) \}\}/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
