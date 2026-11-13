// Prova da #2267: o Caixa do dia e o Extrato dizem quanto há a devolver de pedido cancelado.
//
// Banca de 06/11: o Pix de pedido cancelado aparecia como entrada comum e só a Ficha dizia "a devolver".
// Cobre o texto do aviso (função pura) e que as duas telas leem a mesma soma do fechamento do caixa.
//
// Roda com: node ferramentas/prova-2267-a-devolver.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const ler = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const caixa = await import('../src/dominio/caixa.js')
const aviso = caixa.avisoDeDevolucoesNasTelas

caso('o aviso existe', () => assert.equal(typeof aviso, 'function'))
caso('sem nada a devolver, nada', () => assert.equal(aviso({ total: 0, pedidos: 0, falhas: 0 }), null))
caso('com valor a devolver, diz quanto, de quantos pedidos e que não é da loja', () => {
  const texto = aviso({ total: 19, pedidos: 1, falhas: 0 })
  assert.match(texto, /R\$\s19,00 a devolver de 1 pedido cancelado/)
  assert.match(texto, /não é da loja/)
  assert.match(texto, /Pós-venda/)
  assert.doesNotMatch(texto, /fechamento/i)
})
caso('pedido que não deu para conferir é dito', () => {
  assert.match(aviso({ total: 0, pedidos: 0, falhas: 2 }), /2 pedidos cancelados não foram conferidos/)
})
caso('o aviso do fechamento continua o mesmo', () => {
  assert.match(caixa.avisoDeDevolucoesPendentes({ total: 19, pedidos: 1 }), /O fechamento não espera/)
})

const abaCaixa = ler('features/gestao/caixa/AbaCaixaApi.jsx')
const painel = ler('features/gestao/financeiro/PainelFinanceiro.jsx')
for (const [nome, fonte] of [['Caixa do dia', abaCaixa], ['Extrato', painel]]) {
  caso(`${nome} lê a soma do fechamento`, () => assert.match(fonte, /useDevolucoesPendentes\(/))
  caso(`${nome} mostra o aviso`, () => assert.match(fonte, /avisoDeDevolucoesN(asTelas|oExtrato)\(/))
}

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
