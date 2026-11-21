// #3076: com o cardápio vazio (loja sem itens) ou recusado (vitrine desligada), o Novo pedido mostrava
// "Carregando o cardápio…" para sempre: `cardapioPronto` não separava "ainda não leu" de "leu e veio vazio".
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
})
const acao = await import('../src/aplicacao/acoes.js')
const { casosApi } = await import('../src/aplicacao/casos/api.js')
const { situacaoDoCardapio } = await import('../src/aplicacao/janelaDoPedido.js')

const inicial = { catalogo: { cardapio: [], galeria: [] }, sincronizacao: {} }
assert.equal(situacaoDoCardapio(inicial.catalogo).tipo, 'carregando', 'antes da primeira leitura')

const vazio = casosApi[acao.SINCRONIZAR_CARDAPIO](inicial, { cardapio: [] })
assert.equal(situacaoDoCardapio(vazio.catalogo).tipo, 'pronto', 'leu e veio vazio: não é mais carregando')

const recusado = casosApi[acao.CARDAPIO_INDISPONIVEL](inicial, { mensagem: 'A loja online está desligada.' })
assert.deepEqual(situacaoDoCardapio(recusado.catalogo), { tipo: 'falhou', mensagem: 'A loja online está desligada.' }, 'recusa diz o motivo')

const comItens = { catalogo: { cardapio: [{ sku: 'bolo', nome: 'Bolo', preco: 10 }], galeria: [] }, sincronizacao: {} }
const falhaDepois = casosApi[acao.CARDAPIO_INDISPONIVEL](comItens, { mensagem: 'Sem conexão.' })
assert.equal(falhaDepois.catalogo.cardapio.length, 1, 'falha passageira não apaga o cardápio que já estava na tela')
assert.equal(situacaoDoCardapio(falhaDepois.catalogo).tipo, 'pronto', 'com itens na tela, segue montando')

const voltou = casosApi[acao.SINCRONIZAR_CARDAPIO](recusado, { cardapio: comItens.catalogo.cardapio })
assert.equal(situacaoDoCardapio(voltou.catalogo).tipo, 'pronto', 'a leitura seguinte limpa a recusa')
assert.equal(voltou.catalogo.cardapioFalha, null)

// Modo demonstração: a massa já vem com itens e nunca passa pela leitura da API.
assert.equal(situacaoDoCardapio({ cardapio: comItens.catalogo.cardapio }).tipo, 'pronto', 'demonstração')
console.log('ok prova-3076-cardapio-vazio')

// Revisão da #3076: leu vazio e depois a vitrine desligou: vale o motivo da recusa, não "cardápio vazio".
{
  const { situacaoDoCardapio: s } = await import('../src/aplicacao/janelaDoPedido.js')
  const assert2 = (await import('node:assert/strict')).default
  assert2.deepEqual(s({ cardapio: [], cardapioLido: true, cardapioFalha: 'A loja online está desligada.' }), { tipo: 'falhou', mensagem: 'A loja online está desligada.' })
  assert2.deepEqual(s({ cardapio: [{ sku: 'a' }], cardapioLido: true, cardapioFalha: 'x' }), { tipo: 'pronto' }, 'itens na tela valem mesmo com falha depois')
}
