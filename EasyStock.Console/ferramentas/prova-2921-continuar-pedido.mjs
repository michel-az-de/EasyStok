// #2921: o botão de entrada do Novo pedido diz "Continuar pedido" quando a cliente tem rascunho guardado, e volta a
// "Novo pedido" quando o rascunho some (Registrar, Descartar, "Sair"). Rascunho de outra cliente não muda o rótulo.
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
const { gravarRascunhoDoPedido, apagarRascunhoDoPedido } = await import('../src/infra/api/rascunhosDoPedido.js')
const { rascunhoDaMontagem, rotuloDoNovoPedido } = await import('../src/aplicacao/janelaDoPedido.js')

const ana = { empresa: { id: 'e1' }, usuario: { id: 'ana' } }
const beto = { empresa: { id: 'e1' }, usuario: { id: 'beto' } }
const pedido = { itens: [{ sku: 'bolo', qtd: 1 }] }

assert.equal(rotuloDoNovoPedido(ana, 'c1'), 'Novo pedido', 'sem rascunho')
gravarRascunhoDoPedido(ana, 'c1', rascunhoDaMontagem({ clienteId: 'c1' }, pedido))
assert.equal(rotuloDoNovoPedido(ana, 'c1'), 'Continuar pedido', 'com rascunho da mesma cliente')
assert.equal(rotuloDoNovoPedido(ana, 'c2'), 'Novo pedido', 'rascunho de outra cliente não muda')
assert.equal(rotuloDoNovoPedido(beto, 'c1'), 'Novo pedido', 'rascunho de outro usuário não muda')
assert.equal(rotuloDoNovoPedido(ana, null), 'Novo pedido', 'sem cliente escolhida')
apagarRascunhoDoPedido(ana, 'c1')
assert.equal(rotuloDoNovoPedido(ana, 'c1'), 'Novo pedido', 'depois de apagar')
console.log('prova-2921-continuar-pedido: ok')
