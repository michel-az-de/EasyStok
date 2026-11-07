// Prova da #2275: o Caixa do dia (tela 14) diz só o que está nos totais do dia e mostra um status e
// alertas coerentes.
//
// Banca de 06/11: o aviso dizia "R$ 282,00 entrou e está nos totais" com Pagamentos e Entrou em R$ 19,00,
// porque somava cancelados dos últimos 90 dias. O aviso do dia conta só o que entrou hoje.
//
// Roda com: node ferramentas/prova-2275-aviso-caixa.mjs

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
const DIA = '2026-11-07'
const ontem = '2026-11-06T15:00:00-03:00'
const hoje = '2026-11-07T10:00:00-03:00'

// 14·4: o aviso do dia soma só o pagamento que entrou hoje, limitado ao que ainda falta devolver.
caso('aDevolverDoDia existe', () => assert.equal(typeof caixa.aDevolverDoDia, 'function'))
caso('cancelado de ontem não conta no caixa de hoje', () => {
  assert.equal(caixa.aDevolverDoDia({ aDevolver: 100, pagamentos: [{ pagoEm: ontem, valor: 100 }] }, DIA), 0)
})
caso('cancelado de hoje conta o que entrou hoje', () => {
  assert.equal(caixa.aDevolverDoDia({ aDevolver: 19, pagamentos: [{ pagoEm: hoje, valor: 19 }] }, DIA), 19)
})
caso('misto (ontem e hoje) conta só o pagamento de hoje', () => {
  assert.equal(caixa.aDevolverDoDia({ aDevolver: 119, pagamentos: [{ pagoEm: ontem, valor: 100 }, { pagoEm: hoje, valor: 19 }] }, DIA), 19)
})
caso('devolução parcial de hoje não passa do que falta devolver', () => {
  assert.equal(caixa.aDevolverDoDia({ aDevolver: 30, pagamentos: [{ pagoEm: hoje, valor: 50 }] }, DIA), 30)
})
caso('sem nada a devolver vale zero', () => {
  assert.equal(caixa.aDevolverDoDia({ aDevolver: 0, pagamentos: [{ pagoEm: hoje, valor: 50 }] }, DIA), 0)
})

const hook = ler('aplicacao/useDevolucoesPendentes.js')
const abaCaixa = ler('features/gestao/caixa/AbaCaixaApi.jsx')
const css = ler('features/gestao/caixa/abaCaixa.module.css')

caso('o Caixa do dia lê a soma do dia, não a dos 90 dias', () => {
  assert.match(abaCaixa, /useDevolucoesPendentes\(\s*Boolean\(caixa\.dia\?\.data\),\s*caixa\.dia\?\.data\s*\)/)
  assert.match(hook, /aDevolverDoDia\(/)
})
caso('o Extrato segue lendo o pool dos 90 dias (não muda aqui)', () => {
  assert.match(ler('features/gestao/financeiro/PainelFinanceiro.jsx'), /useDevolucoesPendentes\(true\)/)
})

// 14·1: o status "Aberto" tem um token só, no topo e na tabela.
caso('tomDoCaixa: aberto é ok, aberto esquecido é aviso, fechado é neutro', () => {
  assert.equal(caixa.tomDoCaixa({ fechado: false, esquecido: false }), 'ok')
  assert.equal(caixa.tomDoCaixa({ fechado: false, esquecido: true }), 'aviso')
  assert.equal(caixa.tomDoCaixa({ fechado: true, esquecido: false }), 'neutro')
})
caso('topo e linha "Desde" da tabela usam o mesmo tomDoCaixa', () => {
  assert.equal((abaCaixa.match(/tomDoCaixa\(/g) ?? []).length >= 2, true)
})

// 14·2: alertas com ícone e título curto.
caso('os alertas do topo têm ícone de alerta e título curto', () => {
  assert.match(abaCaixa, /alertaTitulo/)
  assert.match(abaCaixa, /nome="alerta"|icone="alerta"/)
})

// 14·3: rótulos do resumo numa linha só, com tooltip; valores alinhados.
caso('rótulo do resumo não quebra linha e tem tooltip', () => {
  assert.match(css, /\.resumoLista dt\s*\{[^}]*white-space:\s*nowrap/)
  assert.match(css, /\.resumoLista dt\s*\{[^}]*text-overflow:\s*ellipsis/)
  assert.match(abaCaixa, /<dt title=\{l\.rotulo\}>/)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
