// #3193 (re-pontuação da #3076, H4 e H2): textos de Cadastros, Pedidos e Financeiro no mesmo tom.
// 1. "Emitir a conta ao salvar" vira texto de loja; 2. "Lead" ganha explicação no próprio selo.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
})
const { palavrasDoLado } = await import('../src/dominio/financeiro.js')
const { situacaoDaLinha } = await import('../src/dominio/cadastros.js')

for (const lado of ['pagar', 'receber']) {
  const texto = palavrasDoLado(lado).emitir
  assert.equal(texto, 'Deixar a conta aberta ao salvar', `${lado}: texto de loja no lugar de "Emitir a conta ao salvar"`)
  assert.doesNotMatch(texto, /emitir/i, 'sem o jargão')
}

const lead = situacaoDaLinha('clientes', { orderCount: 0, ativo: true }, false)
assert.equal(lead.texto, 'Lead', 'o texto segue "Lead" (outras telas e provas o usam)')
assert.equal(lead.rotulo, 'Sem pedido ainda', 'o selo visível diz o que a pessoa é, sem jargão')
assert.match(lead.titulo, /ainda não fez nenhum pedido/i, 'o selo explica o que é Lead')
assert.equal(situacaoDaLinha('clientes', { orderCount: 3, ativo: true }, false).titulo, undefined, 'cliente com pedido não precisa de explicação')
assert.equal(situacaoDaLinha('clientes', { ativo: true }, false).titulo, undefined, 'histórico não informado não é lead')
console.log('ok prova-3193-consistencia')
