import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, proximo) {
    if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return proximo(s + '.js', c) } catch { /* padrão */ } }
    return proximo(s, c)
  },
  load(url, c, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API=true; export const API_BASE=''" }
    return proximo(url, c)
  },
})
const { agruparApresentacoes } = await import('../src/dominio/apresentacoesDoCardapio.js')
const { corpoDoPedido, produtoDaApi } = await import('../src/infra/api/comandaApi.js')
const { novoPedido, comItem } = await import('../src/dominio/pedido.js')
const { produtoDaPorcao, situacaoDoItem } = await import('../src/dominio/cardapio.js')
const pratos = [600, 800, 1000].map((g, i) => Object.freeze({ sku: 'lasanha-' + g,
  nome: 'Lasanha Bolonhesa Clássica ' + g + ' g', porcao: g + ' g', categoria: 'Lasanhas',
  preco: [38, 45, 68][i], disponivelHoje: i !== 1, porcoes: [] }))
const antes = JSON.stringify(pratos)
const grupos = agruparApresentacoes(pratos)
assert.equal(grupos.length, 1)
assert.equal(grupos[0].nome, 'Lasanha Bolonhesa Clássica')
assert.deepEqual(grupos[0].itens, pratos)
for (let i = 0; i < pratos.length; i++) assert.equal(grupos[0].itens[i], pratos[i])
assert.equal(JSON.stringify(pratos), antes)
let pedido = novoPedido('1')
for (const item of grupos[0].itens) pedido = comItem(pedido, item.sku)
assert.deepEqual(corpoDoPedido(pedido).itens.map(i => i.cardapioItemId), pratos.map(i => i.sku))
assert.equal(corpoDoPedido(pedido).itens.some(i => i.variacaoId), false)
const diferentes = [
  { ...pratos[0], categoria: 'Outra categoria' },
  { ...pratos[0], sku: 'desconhecido', nome: 'Lasanha desconhecida 600 g' },
  { ...pratos[0], sku: 'sem-peso', porcao: '' },
  { ...pratos[0], sku: 'peso-nao-corresponde', nome: 'Lasanha Bolonhesa Clássica 600 gramas' },
  { ...pratos[0], sku: 'grafia-distinta', nome: 'lasanha bolonhesa clássica 600 g' },
  { ...pratos[0], sku: 'nativo', porcoes: [{ id: 'p600', rotulo: '600 g', preco: 38, disponivel: true }] },
]
assert.equal(agruparApresentacoes([...pratos, ...diferentes]).length, 7)
const nativa = produtoDaPorcao(diferentes.at(-1), diferentes.at(-1).porcoes[0])
assert.deepEqual(corpoDoPedido(comItem(novoPedido('2'), nativa.sku)).itens[0],
  { cardapioItemId: 'nativo', variacaoId: 'p600', qtd: 1, observacao: null })
const publico = produtoDaApi({ id: 'original', nome: 'Lasanha Bolonhesa Clássica 600 g', pesoExibicao: '600 g',
  precoCentavos: 3800, disponivel: false, estoqueAtual: 0, linha: 'prepararEmCasa' })
assert.equal(agruparApresentacoes([publico])[0].itens[0], publico)
assert.equal(publico.preco, 38); assert.equal(publico.disponivelHoje, false); assert.equal(publico.estoque, 0)
assert.equal(situacaoDoItem({ ...publico, disponivelHoje: true, estoque: 1 }).rotulo, '1 porção')
console.log('ok apresentação: referências/IDs/preços/disponibilidade intactos; desconhecidos separados; API e porções nativas preservadas')
