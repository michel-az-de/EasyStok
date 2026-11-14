// Prova da #2535 (Q05 da auditoria QA de 13/11, `docs/dev/revisoes/2026-11-13-qa-console-web.md`, pacote 4).
//
// O fechamento de uma sessão de vários dias grava em `observacoes` o aviso de lançamentos que ficaram fora dele.
// Antes da #2256 a frase saía sem acento e em caixa alta; depois, em português correto. Nos dois casos o console
// imprimia o texto cru, sem caminho para resolver. Agora a nota do operador fica como está e o aviso vira
// mensagem curta com o período e o link para o Extrato.
//
// Roda com: node ferramentas/prova-2535-aviso-fechamento.mjs

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

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const caixa = await import('../src/dominio/caixa.js')

// Texto gravado em produção no fechamento de 11/10 (antes da #2256).
const LEGADO = 'Atencao: ha lancamentos entre 12/10/2026 e 27/10/2026 sob esta sessao que NAO foram incluidos ' +
  'neste fechamento (revisar/reconciliar manualmente).'
// Texto atual do FecharCaixaUseCase.
const ATUAL = 'Atenção: há lançamentos entre 12/10/2026 e 27/10/2026 nesta sessão que não entraram neste fechamento. ' +
  'Revise e reconcilie manualmente.'

caso('observacaoDoFechamento existe', () => assert.equal(typeof caixa.observacaoDoFechamento, 'function'))
caso('aviso legado vira período, sem nota', () => {
  assert.deepEqual(caixa.observacaoDoFechamento(LEGADO), { nota: '', foraDoFechamento: { de: '12/10', ate: '27/10' } })
})
caso('aviso atual vira período, sem nota', () => {
  assert.deepEqual(caixa.observacaoDoFechamento(ATUAL), { nota: '', foraDoFechamento: { de: '12/10', ate: '27/10' } })
})
caso('nota do operador fica como está e o aviso sai dela', () => {
  assert.deepEqual(caixa.observacaoDoFechamento(`Conferi a gaveta.\nSobrou troco.\n${ATUAL}`),
    { nota: 'Conferi a gaveta.\nSobrou troco.', foraDoFechamento: { de: '12/10', ate: '27/10' } })
})
caso('observação sem aviso continua igual', () => {
  assert.deepEqual(caixa.observacaoDoFechamento('Contado na gaveta: R$ 100,00'), { nota: 'Contado na gaveta: R$ 100,00', foraDoFechamento: null })
})
caso('observação vazia não quebra', () => {
  assert.deepEqual(caixa.observacaoDoFechamento(null), { nota: '', foraDoFechamento: null })
})
caso('a mensagem do produto diz o período sem jargão', () => {
  const texto = caixa.avisoForaDoFechamento({ de: '12/10', ate: '27/10' })
  assert.equal(texto, 'Os lançamentos de 12/10 a 27/10 ficaram fora deste fechamento.')
  assert.doesNotMatch(texto, /reconcili|NAO|Atencao/i)
})

const tela = readFileSync(new URL('../src/features/gestao/caixa/AbaCaixaApi.jsx', import.meta.url), 'utf8')
caso('a tela não imprime mais a observação crua', () => {
  assert.doesNotMatch(tela, />\{f\.observacoes\}</)
  assert.doesNotMatch(tela, />\{dia\.fechamento\.observacoes\}</)
})
caso('os dois lugares usam o mesmo componente, e ele leva ao Extrato', () => {
  assert.equal((tela.match(/<ObservacaoDoFechamento /g) ?? []).length, 2)
  const componente = tela.slice(tela.indexOf('function ObservacaoDoFechamento'))
  assert.match(componente.slice(0, 1200), /href="#\/m\/financeiro\/extrato"/)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
