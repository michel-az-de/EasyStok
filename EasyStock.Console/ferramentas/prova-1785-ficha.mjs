// Componentes reais: o encerramento não monta os formulários do pedido anterior.
// Interação, foco, concorrência e rolagem são conferidos também no navegador.
import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { transformWithOxc } from 'vite'
import { createElement as h } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter(n => n.endsWith('.jsx')).map(async nome => {
  const arquivo = join(raiz, nome)
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env', '({ DEV: false })')
  return [pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code]
})))
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(spec + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API=true;export const API_BASE="";', shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})

const { PainelFicha } = await import('../src/features/ficha-cliente/PainelFicha.jsx')
const { ContextoEstado, ContextoAcoes, ContextoCatalogo } = await import('../src/aplicacao/contextos.js')
const { ContextoAcessoModulos } = await import('../src/aplicacao/acessoModulos.jsx')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { CANAIS, LINHAS_PRODUTO } = await import('../src/infra/catalogo.js')
const pedido = pedidoDaApi({
  pedidoId: 'pedido-anterior', numeroComanda: 4321, status: 'aguardando', total: 81, frete: 5,
  itens: [{ cardapioItemId: 'lasanha', nome: 'Lasanha da compra anterior', quantidade: 2, precoUnitario: 38 }],
  alocado: 38, totalPago: 38, pagamentos: [{ id: 'pg1', valor: 38, metodo: 'pix', pagoEm: '2026-10-14T12:00:00Z' }],
})
const conversa = {
  id: 'ana', nome: 'Ana Ribeiro', estado: 'Em atendimento', canal: 'WhatsApp', conta: 'cliente', clienteId: 'cliente-ana',
  cliente: { telefone: '11999990001', endereco: 'Rua de teste, 10', pedidos: 1, tags: [], notas: [] },
  mensagens: [], pedido,
}
function renderizar(alteracoes = {}, fonteApi = true) {
  const selecionada = { ...conversa, ...alteracoes }
  return renderToStaticMarkup(h(ContextoAcessoModulos.Provider, { value: { acoes: {} } },
    h(ContextoEstado.Provider, { value: { selecionada, visiveis: [selecionada], conversas: [selecionada], alertasEstoque: [], automaticoPausado: {},
      agora: Date.parse('2026-10-14T12:00:00Z'), fonteApi, fidelidade: { ativa: false }, historico: [] } },
    h(ContextoCatalogo.Provider, { value: { cardapio: [], canais: CANAIS, linhas: LINHAS_PRODUTO, janelas: [], entregadores: [] } },
    h(ContextoAcoes.Provider, { value: {} }, h(PainelFicha, { aoAbrirCardapio() {} }))))))
}

let total = 0
function provar(nome, fn) { fn(); total++; console.log('ok ' + nome) }
for (const estado of ['aguardando', 'pago', 'entregue', 'cancelado']) {
  provar(`encerrado com pedido ${estado}: não exibe valores, itens, comanda compartilhada ou ações de venda`, () => {
    const html = renderizar({ estado: 'Encerrado', pedido: { ...pedido, estado } })
    assert.match(html, /Atendimento encerrado/)
    assert.match(html, /Consultar pedido vinculado/)
    assert.match(html, /Cliente e histórico/)
    assert.doesNotMatch(html, /Lasanha|4321|R\$|Comanda compartilhada|Pagamento completo|Gerar cobrança|Iniciar preparo|Revisar seleção/)
    assert.doesNotMatch(html, /<dialog|<input|<textarea|<select/)
  })
}
provar('encerrado sem pedido permite histórico sem inventar uma venda', () => {
  const html = renderizar({ estado: 'Encerrado', pedido: null })
  assert.match(html, /Cliente e histórico/)
  assert.doesNotMatch(html, /Consultar pedido vinculado|Adicionar itens|Antes da venda/)
})
provar('lead sem compra abre antes da venda e aponta cadastro incompleto', () => {
  const html = renderizar({ conta: 'lead', clienteId: null, pedido: null, cliente: { tags: [], notas: [] } }, false)
  assert.match(html, /Antes da venda|Vamos atender Ana/)
  assert.match(html, /Adicionar itens/)
  assert.match(html, /Conferir cadastro e endereço/)
  assert.doesNotMatch(html, /Pós-venda|Pedido e entrega|Pagamento completo/)
})
provar('pedido com sinal mostra saldo do pedido inteiro incluindo frete', () => {
  const html = renderizar()
  assert.match(html, /Pedido em andamento/)
  assert.match(html, /81,00/)
  assert.match(html, /43,00 a receber/)
  assert.match(html, /Pedido e entrega/)
  assert.doesNotMatch(html, /Lasanha da compra anterior|<dialog|Histórico financeiro/)
})
for (const estado of ['entregue', 'cancelado']) {
  provar(`pedido ${estado} fica no pós-venda com acesso a ocorrências`, () => {
    const html = renderizar({ pedido: { ...pedido, estado } })
    assert.match(html, /Pós-venda/)
    assert.match(html, /Ocorrências e devoluções/)
    assert.doesNotMatch(html, /Pedido em andamento|Antes da venda/)
  })
}
provar('cliente seguinte não herda nome, comanda nem saldo do anterior', () => {
  const html = renderizar({ id: 'bruno', nome: 'Bruno Lima', pedido: null })
  assert.match(html, /Vamos atender Bruno/)
  assert.doesNotMatch(html, /Ana|4321|43,00|81,00/)
})
console.log(`${total} cenários reais de renderização da ficha aprovados.`)
