/* eslint-disable no-console */
// Prova da #3339 (R2I-08, `docs/dev/estresse-atendimento-r2-2026-11-25.md`): o chip "Resposta para conferir"
// dizia só que a resposta foi barrada. O motivo da guarda (`valor R$ 45,00 sem origem em ferramenta`) ficava
// entre parênteses e o console o apagava antes de montar o chip. Agora cada categoria da guarda tem frase
// curta da atendente, e a conversa mantém o motivo no texto.
//
//   node ferramentas/prova-3339-motivo.mjs

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

const { origemDaPassagem } = await import('../src/dominio/passagem.js')
const { motivoDeEscalada } = await import('../src/dominio/motivoDeEscalada.js')

// Mesmo caminho do modo API: o motivo gravado passa por `motivoDeEscalada` e depois pela passagem.
const chip = (gravado) => origemDaPassagem({ origem: 'agente', motivo: motivoDeEscalada(gravado) }).chip

// [texto que a API grava (LogsDoAssistente / GuardaRespostaAgente.cs), chip esperado]
const GUARDA = [
  ['resposta do agente barrada (valor R$ 45,00 sem origem em ferramenta); precisa de conferência humana', 'Valor sem origem'],
  ['resposta do agente barrada (link sem origem em ferramenta); precisa de conferência humana', 'Link sem origem'],
  ['resposta do agente barrada (afirma situação do pedido sem a ferramenta confirmar); precisa de conferência humana', 'Situação não confirmada'],
  ['resposta do agente barrada (afirma pagamento ou devolução sem a ferramenta confirmar); precisa de conferência humana', 'Pagamento não confirmado'],
  ['resposta do agente barrada (afirma frete combinado sem a ferramenta confirmar); precisa de conferência humana', 'Frete não confirmado'],
  ['resposta do agente barrada (promete frete grátis ou desconto, que é decisão da equipe); precisa de conferência humana', 'Desconto sem aval'],
  ['resposta do agente barrada (promete encaminhamento sem acionar a equipe); precisa de conferência humana', 'Encaminhamento não feito'],
  ['resposta do agente barrada (texto com chamada de ferramenta no lugar da resposta); precisa de conferência humana', 'Texto de ferramenta'],
  ['resposta do agente barrada (ferramenta falhou no turno); precisa de conferência humana', 'Ferramenta falhou'],
  ['resposta do agente barrada (outro motivo); precisa de conferência humana', 'Resposta para conferir'],
]

for (const [gravado, esperado] of GUARDA) {
  caso(`chip da guarda: ${esperado}`, () => {
    assert.equal(chip(gravado), esperado)
    assert.ok(esperado.length <= 28, 'rótulo do chip passa de 28 letras')
  })
}

caso('a conversa mantém o motivo da guarda, sem o parêntese técnico', () => {
  const texto = motivoDeEscalada('resposta do agente barrada (valor R$ 45,00 sem origem em ferramenta); precisa de conferência humana')
  assert.match(texto, /valor R\$ 45,00 sem origem em ferramenta/)
  assert.doesNotMatch(texto, /[()]/)
})

caso('motivo genérico sem parêntese segue com o rótulo genérico', () => {
  assert.equal(chip('resposta do agente barrada; precisa de conferência humana'), 'Resposta para conferir')
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
