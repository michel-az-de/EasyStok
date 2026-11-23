import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
})
const m = await import('../src/features/gestao/respostas/mensagemPadrao.js')

// #3206: lógica pura da mensagem padrão (espelha ModeloMensagemLocal no servidor).
assert.equal(m.nomeTecnico('Pedido pronto para retirada'), 'pedido_pronto_para_retirada')
assert.equal(m.nomeTecnico('  Confirmação de endereço! '), 'confirmacao_de_endereco')
assert.equal(m.nomeTecnico('???'), '')
assert.equal(m.tituloDoNome('pedido_pronto'), 'Pedido pronto')

// Próximo campo em sequência.
assert.equal(m.proximoCampo(''), 1)
assert.equal(m.acrescentarCampo('Olá '), 'Olá {{1}}')
assert.equal(m.acrescentarCampo('Olá {{1}}, pedido '), 'Olá {{1}}, pedido {{2}}')
assert.equal(m.proximoCampo('a {{1}} b {{2}}'), 3)

// Corpo: obrigatório, sequência, não começa nem termina com campo, chaves soltas.
assert.match(m.erroDoCorpo(''), /Escreva/)
assert.match(m.erroDoCorpo('{{1}} olá'), /começar nem terminar/)
assert.match(m.erroDoCorpo('olá {{1}}'), /começar nem terminar/)
assert.match(m.erroDoCorpo('olá {{2}} tchau'), /em ordem/)
assert.match(m.erroDoCorpo('olá {{1}} e {{3}} tchau'), /em ordem/)
assert.match(m.erroDoCorpo('olá {{nome}} tchau'), /chaves/)
assert.match(m.erroDoCorpo('x'.repeat(1025)), /1024/)
assert.equal(m.erroDoCorpo('Olá {{1}}, pedido {{2}} pronto.'), null)

// Cabeçalho e rodapé: uma linha, até 60, sem campo.
const base = { titulo: 'Aviso', cabecalho: '', corpo: 'Olá {{1}}, tudo bem?', rodape: '' }
assert.deepEqual(m.validarMensagemPadrao(base), {})
assert.ok(m.validarMensagemPadrao({ ...base, cabecalho: 'a\nb' }).cabecalho)
assert.ok(m.validarMensagemPadrao({ ...base, cabecalho: 'a{{1}}' }).cabecalho)
assert.ok(m.validarMensagemPadrao({ ...base, rodape: 'x'.repeat(61) }).rodape)
assert.ok(m.validarMensagemPadrao({ ...base, titulo: ' ' }).titulo)
assert.ok(m.validarMensagemPadrao({ ...base, titulo: '???' }).titulo)

// Prévia troca os campos por exemplos; corpo para a API leva idioma e categoria fixos.
assert.equal(m.previaDoCorpo('Olá {{1}}, pedido {{2}}.'), 'Olá Maria, pedido #1234.')
assert.deepEqual(m.corpoParaApi({ ...base, titulo: 'Pedido pronto' }),
  { nome: 'pedido_pronto', idioma: 'pt_BR', categoria: 'UTILITY', cabecalho: null, corpo: 'Olá {{1}}, tudo bem?', rodape: null })

// Toda sugestão passa na regra, e nenhuma é promoção.
assert.ok(m.SUGESTOES.length >= 4 && m.SUGESTOES.length <= 6)
for (const s of m.SUGESTOES) {
  assert.deepEqual(m.validarMensagemPadrao(s), {}, s.id)
  assert.ok(!/promo|desconto|oferta|cupom/i.test(s.corpo), s.id)
}
console.log('prova-3206-mensagem-padrao: ok')
