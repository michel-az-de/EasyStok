/* eslint-disable no-console */
// Prova da #2559: pacote 7 da auditoria QA de 13/11 (Q11). O trilho do Atendimento listava Entregas
// antes de Cozinha em JSX fixo, ao contrário do trilho da gestão, que segue ORDEM_DOS_MODULOS (#2258 A2).
// A ordem dos módulos no trilho do Atendimento tem de ser a mesma de ORDEM_DOS_MODULOS.
//
//   node ferramentas/prova-2559-ordem-do-trilho.mjs

import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const { ORDEM_DOS_MODULOS } = await import('../src/dominio/modulos.js')

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const moldura = readFileSync(new URL('../src/app/Moldura.jsx', import.meta.url), 'utf8')
const inicio = moldura.indexOf('\nfunction Trilho(')
const trilho = moldura.slice(inicio, moldura.indexOf('\nfunction ', inicio + 1))
// Ordem em que os módulos aparecem no trilho: o item atual (Atendimento) e os que dependem de permite().
const modulosNoTrilho = [...trilho.matchAll(/modulo=\{MODULO_ATENDIMENTO\} \/>|permite\('([a-z]+)'\)/g)]
  .map(([, id]) => id ?? 'atendimento')
  .filter((id) => ORDEM_DOS_MODULOS.includes(id))

caso('o trilho do Atendimento tem Atendimento, Cozinha e Entregas', () => {
  assert.ok(inicio > 0, 'function Trilho não encontrada em Moldura.jsx')
  for (const id of ['atendimento', 'cozinha', 'entregas']) assert.ok(modulosNoTrilho.includes(id), id)
})
caso('os módulos do trilho do Atendimento seguem ORDEM_DOS_MODULOS', () => {
  const esperado = ORDEM_DOS_MODULOS.filter((id) => modulosNoTrilho.includes(id))
  assert.deepEqual(modulosNoTrilho, esperado)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
