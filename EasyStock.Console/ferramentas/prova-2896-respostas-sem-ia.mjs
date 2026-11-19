import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
})
const { semIaDoUso } = await import('../src/dominio/painelAssistente.js')

// #2896 (S78): a aba Uso e cota diz quantas respostas do mês saíram sem IA e a estimativa do que pouparam.
// O aceite da spec: 3 respostas sem IA e 2 turnos do modelo mostram "3 de 5".
const base = { ano: 2026, mes: 11, turnos: 5, totalTokens: 30_000 }

const comEstimativa = semIaDoUso({ ...base, respostasSemIa: 3, tokensPoupadosEstimados: 45_000 })
assert.equal(comEstimativa.titulo, 'Respostas sem IA: 3 de 5')
assert.equal(comEstimativa.detalhe, 'cerca de 45 mil tokens poupados, pela média dos turnos com IA do mês')

// Só resposta sem IA no mês: não há média para estimar, e a tela diz isso em vez de mostrar 0.
const semMedia = semIaDoUso({ ...base, turnos: 2, totalTokens: 0, respostasSemIa: 2, tokensPoupadosEstimados: null })
assert.equal(semMedia.titulo, 'Respostas sem IA: 2 de 2')
assert.equal(semMedia.detalhe, 'sem turno com IA no mês para estimar o que foi poupado')

// Nenhuma resposta sem IA: o número aparece zerado, sem frase de economia.
assert.deepEqual(semIaDoUso({ ...base, respostasSemIa: 0, tokensPoupadosEstimados: null }), { titulo: 'Respostas sem IA: 0 de 5', detalhe: null })

// API antiga (sem os campos) não quebra a aba nem inventa número.
assert.deepEqual(semIaDoUso(base), { titulo: 'Respostas sem IA: 0 de 5', detalhe: null })
assert.equal(semIaDoUso(null), null)

// Milhar com separador, como o resto da linha de uso.
assert.equal(semIaDoUso({ turnos: 12_345, respostasSemIa: 1_234, tokensPoupadosEstimados: 1_500_000 }).titulo, 'Respostas sem IA: 1.234 de 12.345')

console.log('prova-2896-respostas-sem-ia: ok')
