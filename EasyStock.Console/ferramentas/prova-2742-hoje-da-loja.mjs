/* eslint-disable no-console */
// Prova da issue #2742: a marca "Hoje" da lista de horários das automações segue o dia civil da loja
// (America/Sao_Paulo), nunca o fuso do navegador de quem opera.
//
//   node ferramentas/prova-2742-hoje-da-loja.mjs

import assert from 'node:assert/strict'

// Navegador de quem opera em Toquio: sábado 23h em São Paulo já é domingo por lá.
process.env.TZ = 'Asia/Tokyo'

const { chaveDoDiaNaLoja } = await import('../src/dominio/funcionamento.js')

const sabadoNoite = Date.UTC(2026, 10, 15, 2, 0)
assert.equal(new Date(sabadoNoite).getDay(), 0, 'premissa: no navegador de Toquio já é domingo')
assert.equal(chaveDoDiaNaLoja(sabadoNoite), 'sabado', 'na loja ainda é sábado')

const domingoManha = Date.UTC(2026, 10, 15, 12, 0)
assert.equal(chaveDoDiaNaLoja(domingoManha), 'domingo', 'meio-dia em São Paulo é domingo')

console.log('prova-2742: 2 verificações ok')
