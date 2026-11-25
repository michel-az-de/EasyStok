/* eslint-disable no-console */
// Prova da #3339 (R1D-10, `docs/dev/anexos-estresse-atendimento-2026-11-25/r1-devolucao-reclamacao.md`): a nota
// interna com texto, ainda não fixada, ficava guardada como rascunho depois de Concluir e a janela de encerramento
// não dizia nada. Agora o modal avisa, só para a conversa dona da nota e só se houver texto.
//
//   node ferramentas/prova-3339-nota-ao-concluir.mjs

import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

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

const { avisoDaNotaAoEncerrar } = await import('../src/dominio/resumoAtendimento.js')

const notas = [
  { id: 'c1', chave: 'n1', texto: 'Cliente quer o estorno no Pix, ligar amanhã' },
  { id: 'c2', chave: 'n2', texto: '   \n ' },
  { id: 'c3', chave: 'n3', texto: 'Outra conversa' },
]

caso('R1D-10: nota com texto na conversa avisa que não foi fixada', () => {
  assert.equal(typeof avisoDaNotaAoEncerrar, 'function', 'avisoDaNotaAoEncerrar não existe')
  assert.match(avisoDaNotaAoEncerrar(notas, 'c1'), /nota interna.*não foi fixada/i)
})

caso('R1D-10: o aviso diz que Concluir não fixa a nota', () => {
  assert.match(avisoDaNotaAoEncerrar(notas, 'c1'), /Concluir não a fixa/)
})

caso('nota só com espaços não avisa', () => {
  assert.equal(avisoDaNotaAoEncerrar(notas, 'c2'), null)
})

caso('nota de outra conversa não avisa', () => {
  assert.equal(avisoDaNotaAoEncerrar(notas, 'c9'), null)
})

caso('sem lista ou sem conversa, não avisa nem quebra', () => {
  assert.equal(avisoDaNotaAoEncerrar(undefined, 'c1'), null)
  assert.equal(avisoDaNotaAoEncerrar(notas, null), null)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
