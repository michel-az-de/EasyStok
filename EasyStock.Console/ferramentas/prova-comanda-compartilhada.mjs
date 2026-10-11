import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'

const arquivo = new URL('../src/features/ficha-cliente/ComandaDoCliente.jsx', import.meta.url)
const compilado = (await transformWithOxc(readFileSync(arquivo, 'utf8'), arquivo.pathname, { jsx: { runtime: 'automatic' } })).code
const contexto = { dados: [], erro: '', ocupado: false, salvar: (...args) => { contexto.salvo = args }, confirmar: (...args) => { contexto.confirmado = args } }
globalThis.__comandaCompartilhada = contexto
const codigo = fonte => 'data:text/javascript,' + encodeURIComponent(fonte)
registerHooks({
  resolve(spec, ctx, proximo) {
    if (spec === 'react/jsx-runtime') return { url: codigo('export const Fragment="fragment"; export const jsx=(type,props,key)=>({type,props,key}); export const jsxs=jsx'), shortCircuit: true }
    if (spec.endsWith('.css')) return { url: codigo('export default {}'), shortCircuit: true }
    if (spec.endsWith('useComandasCliente')) return { url: codigo('export const useComandasCliente=()=>globalThis.__comandaCompartilhada'), shortCircuit: true }
    if (spec.endsWith('/Botao')) return { url: codigo('export function Botao(){}'), shortCircuit: true }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return proximo(spec + '.js', ctx)
    return proximo(spec, ctx)
  },
  load(url, ctx, proximo) { return url === arquivo.href ? { format: 'module', source: compilado, shortCircuit: true } : proximo(url, ctx) },
})
const { ComandaDoCliente } = await import(arquivo.href)
const { faltaPagar, totalPago } = await import('../src/dominio/pagamento.js')
function elementos(arvore) {
  if (Array.isArray(arvore)) return arvore.flatMap(elementos)
  return arvore?.props ? [arvore, ...elementos(arvore.props.children)] : []
}
const comanda = { linkId: 'link', revisao: 7, podeEditar: true, totalPrevisto: 45, pedido: null,
  selecao: [{ cardapioItemId: 'prato', variacaoId: 'porcao', nome: 'Lasanha', qtd: 2, preco: 20 }],
  rascunho: { itens: [{ cardapioItemId: 'prato', variacaoId: 'porcao', qtd: 2 }], observacoes: '', pedidoId: null } }
contexto.dados = [comanda]
let arvore = elementos(ComandaDoCliente({ conversaId: 'conversa', ativo: true }))
arvore.find(e => e.props['aria-label'] === 'Tirar uma unidade de Lasanha').props.onClick()
assert.equal(contexto.salvo[0].revisao, 7)
assert.equal(contexto.salvo[1].itens[0].qtd, 1)
assert.equal(contexto.salvo[1].itens[0].variacaoId, 'porcao')
assert.equal(comanda.rascunho.itens[0].qtd, 2, 'a seleção carregada permanece preservada até a resposta')
arvore.find(e => e.type === 'textarea').props.onBlur({ target: { value: 'Molho à parte' } })
assert.equal(contexto.salvo[1].observacoes, 'Molho à parte')
comanda.podeEditar = false; comanda.envio = { situacao: 'alteracao_aguardando_confirmacao', saldo: -20 }
arvore = elementos(ComandaDoCliente({ conversaId: 'conversa', ativo: true }))
arvore.find(e => e.props.children === 'Manter pedido original').props.onClick()
assert.deepEqual(contexto.confirmado, ['link', false])
arvore.find(e => e.props.children === 'Confirmar alteração').props.onClick()
assert.deepEqual(contexto.confirmado, ['link', true])
assert.ok(!arvore.some(e => e.props['aria-label'] === 'Tirar uma unidade de Lasanha'), 'envio consumido não oferece edição')
const pedido = { pedidoId: 'pedido', totalApi: 50, alocadoApi: 30, pagamentosApi: [{ valor: 40, coberto: 40 }], frete: 0, itens: [] }
assert.equal(totalPago(pedido), 40, 'o histórico preserva o dinheiro bruto')
assert.equal(faltaPagar(pedido, []), 20, 'o saldo desconta estornos e soma somente o crédito aplicado')
console.log('Comanda compartilhada: controles, revisão, observação, conferência e saldo financeiro aprovados.')
