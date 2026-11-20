// #2921: rascunho do Novo pedido por cliente. Fechar com itens guarda; reabrir restaura no mesmo passo e tira o item
// que saiu do cardápio do dia, com aviso; Registrar e "Sair" apagam; rascunho de um usuário não aparece para outro.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
})
const memoria = new Map()
globalThis.sessionStorage = {
  getItem: (k) => memoria.get(k) ?? null, setItem: (k, v) => memoria.set(k, String(v)), removeItem: (k) => memoria.delete(k),
  get length() { return memoria.size }, key: (i) => [...memoria.keys()][i] ?? null,
}
const { lerRascunhoDoPedido, gravarRascunhoDoPedido, apagarRascunhoDoPedido, temRascunhoDoPedido } = await import('../src/infra/api/rascunhosDoPedido.js')
const { esquecerRascunhos } = await import('../src/infra/api/rascunhosDaSessao.js')
const { rascunhoDaMontagem, restaurarRascunho } = await import('../src/aplicacao/janelaDoPedido.js')

const ana = { empresa: { id: 'e1' }, usuario: { id: 'ana' } }
const beto = { empresa: { id: 'e1' }, usuario: { id: 'beto' } }
const cardapio = [{ sku: 'bolo', nome: 'Bolo de pote', disponivelHoje: true }, { sku: 'torta', nome: 'Torta', disponivelHoje: true }]
const janela = { clienteId: 'c1', conversaId: 'x', nome: 'Maria' }
const pedido = { itens: [{ sku: 'bolo', qtd: 2, obs: 'sem nozes' }, { sku: 'torta', qtd: 1 }], janela: 'j1|2026-11-21', meio: 'pix' }

// Fechar com itens guarda; sem itens não guarda.
assert.equal(temRascunhoDoPedido(ana, 'c1'), false)
gravarRascunhoDoPedido(ana, 'c1', rascunhoDaMontagem(janela, pedido, 'entrega', cardapio))
assert.equal(temRascunhoDoPedido(ana, 'c1'), true, 'o botão vira "Continuar pedido"')
gravarRascunhoDoPedido(ana, 'c2', rascunhoDaMontagem({ clienteId: 'c2' }, { itens: [] }))
assert.equal(temRascunhoDoPedido(ana, 'c2'), false)

// Reabrir: mesmo passo, mesmas observações; item que saiu do dia vai embora com aviso.
const volta = restaurarRascunho(lerRascunhoDoPedido(ana, 'c1'), cardapio.map(i => i.sku === 'bolo' ? { ...i, disponivelHoje: false } : i))
assert.equal(volta.passo, 'entrega'); assert.deepEqual(volta.itens.map(i => i.sku), ['torta'])
assert.deepEqual(volta.avisos, ['Bolo de pote saiu do cardápio e foi tirado do pedido'])
const inteira = restaurarRascunho(lerRascunhoDoPedido(ana, 'c1'), cardapio)
assert.equal(inteira.itens[0].obs, 'sem nozes'); assert.equal(inteira.janela, 'j1|2026-11-21'); assert.equal(inteira.meio, 'pix'); assert.deepEqual(inteira.avisos, [])

// Outro usuário não vê; Registrar apaga; "Sair" apaga.
assert.equal(temRascunhoDoPedido(beto, 'c1'), false, 'rascunho de um usuário não aparece para outro')
gravarRascunhoDoPedido(ana, 'c3', rascunhoDaMontagem({ clienteId: 'c3' }, pedido, 'itens', cardapio))
apagarRascunhoDoPedido(ana, 'c3')
assert.equal(temRascunhoDoPedido(ana, 'c3'), false, 'Registrar apaga')
assert.equal(temRascunhoDoPedido(ana, 'c1'), true)
esquecerRascunhos()
assert.equal(temRascunhoDoPedido(ana, 'c1'), false, '"Sair" apaga pelo mesmo caminho dos outros rascunhos')
console.log('prova-2921-rascunho: ok')
