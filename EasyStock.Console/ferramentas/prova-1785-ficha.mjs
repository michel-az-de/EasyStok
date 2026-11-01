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
  pedidoId: 'pedido-anterior', numeroComanda: 4321, status: 'aguardando_pagamento', total: 81, frete: 5,
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
    h(ContextoCatalogo.Provider, { value: { cardapio: [], canais: CANAIS, linhas: LINHAS_PRODUTO, janelas: [], entregadores: [], restricoes: ['Lactose', 'Glúten'] } },
    h(ContextoAcoes.Provider, { value: {} }, h(PainelFicha, { aoAbrirCardapio() {} }))))))
}

let total = 0
function provar(nome, fn) { fn(); total++; console.log('ok ' + nome) }
for (const estado of ['aguardando', 'pago', 'entregue', 'cancelado']) {
  provar(`encerrado com pedido ${estado}: não exibe valores, itens, comanda compartilhada ou ações de venda`, () => {
    const html = renderizar({ estado: 'Encerrado', pedido: { ...pedido, estado } })
    assert.match(html, /Conversa concluída/)
    assert.match(html, /Consultar pedido vinculado/)
    assert.match(html, /Ficha/)
    assert.doesNotMatch(html, /Lasanha|4321|R\$|Comanda compartilhada|Pagamento completo|Gerar cobrança|Começar preparo|Revisar itens/)
    assert.doesNotMatch(html, /<dialog|<input|<textarea|<select/)
  })
}
provar('encerrado sem pedido permite histórico sem inventar uma venda', () => {
  const html = renderizar({ estado: 'Encerrado', pedido: null })
  assert.match(html, /Ficha/)
  assert.doesNotMatch(html, /Consultar pedido vinculado|Adicionar itens|Novo pedido|Em montagem/)
})
provar('contato sem cadastro abre novo pedido e aponta cadastro incompleto', () => {
  const html = renderizar({ conta: 'lead', clienteId: null, pedido: null, cliente: { tags: [], notas: [] } }, false)
  assert.match(html, /<h3>Comanda/)
  assert.match(html, /Adicionar itens/)
  assert.match(html, /Contato sem cadastro/)
  assert.match(html, /aria-label="Editar cadastro"/)
  assert.doesNotMatch(html, /Pós-venda|Revisar comanda|Pagamento completo/)
})
provar('pedido com sinal mostra saldo do pedido inteiro incluindo frete', () => {
  const html = renderizar()
  assert.match(html, /<h3>Comanda 4321<\/h3>/)
  assert.match(html, /Aguardando pagamento/)
  assert.match(html, /Sem cobrança/)
  assert.doesNotMatch(html, /Cobrança pendente|Próximo: Pago/)
  assert.match(html, /81,00/)
  assert.match(html, /43,00 a receber/)
  assert.match(html, /Revisar comanda/)
  assert.doesNotMatch(html, /Lasanha da compra anterior|<dialog|Histórico financeiro/)
})
provar('só uma cobrança criada e em aberto aparece como pendente no resumo', () => {
  const cobranca = { expiraEm: Date.parse('2026-10-14T12:30:00Z') }
  assert.match(renderizar({ pedido: { ...pedido, cobranca } }), /Cobrança pendente/)
  for (const detalhe of [{ expiraEm: 0 }, { comprovanteEm: 1 }, { pagaEm: 1 }, { canceladaEm: 1 }, { estornadaEm: 1 }]) {
    assert.doesNotMatch(renderizar({ pedido: { ...pedido, cobranca: { ...cobranca, ...detalhe } } }), /Cobrança pendente/)
  }
  assert.doesNotMatch(renderizar({ pedido: { ...pedido, estado: 'pago', cobranca } }), /Cobrança pendente/)
})
provar('rascunho com cliente cadastrado e endereço permite calcular o frete antes de criar pedido', () => {
  const html = renderizar({ pedido: { numero: 'local', estado: 'aguardando', itens: [{ sku: 'lasanha', qtd: 1 }] } })
  assert.match(html, /<strong>Entrega<\/strong>/)
  assert.equal((html.match(/<strong>Entrega<\/strong>/g) ?? []).length, 1, 'um único acesso à entrega antes de criar pedido')
  assert.doesNotMatch(html, /<h3>Comanda compartilhada/)
  assert.match(html, /Em montagem/)
  assert.doesNotMatch(html, /Cobrança pendente|Próximo: Pago/)
})
provar('pedido já criado conserva o acesso ao frete e à entrega do pedido', () => {
  const html = renderizar()
  assert.match(html, /<strong>Entrega<\/strong>/)
  assert.doesNotMatch(html, /aria-label="Consultar frete"/)
})
for (const estado of ['entregue', 'cancelado']) {
  provar(`pedido ${estado} fica no pós-venda com acesso a ocorrências`, () => {
    const html = renderizar({ pedido: { ...pedido, estado } })
    assert.match(html, /Pós-venda/)
    assert.match(html, /Ocorrências e devoluções/)
    assert.doesNotMatch(html, /Novo pedido|Em montagem/)
  })
}
provar('cliente seguinte não herda nome, comanda nem saldo do anterior', () => {
  const html = renderizar({ id: 'bruno', nome: 'Bruno Lima', pedido: null })
  assert.match(html, /<h3>Comanda/)
  assert.match(html, /Bruno Lima/)
  assert.doesNotMatch(html, /Ana|4321|43,00|81,00/)
})
for (const fonteApi of [true, false]) provar(`seleção local ${fonteApi ? 'API' : 'demo'} não aparece como pedido criado ou pagamento pendente`, () => {
  const html = renderizar({ pedido: { numero: 'local', estado: 'aguardando', itens: [{ sku: 'lasanha', qtd: 1 }] } }, fonteApi)
  assert.match(html, /<h3>Comanda/)
  assert.match(html, /Em montagem/)
  assert.match(html, /Revisar comanda/)
  assert.equal((html.match(/aria-label="Adicionar itens"/g) ?? []).length, 1, 'montagem tem um único atalho direto para itens')
  assert.ok(html.indexOf('aria-label="Adicionar itens"') < html.indexOf('aria-label="Detalhes do atendimento"'))
  assert.ok(html.indexOf('Revisar comanda') < html.indexOf('Notas internas do cliente'))
  assert.ok(html.indexOf('Notas internas do cliente') < html.indexOf('Histórico do cliente'))
  assert.doesNotMatch(html, /Pedido criado|Pagamento completo|a receber|Ocorrências e devoluções/)
})
provar('restrições configuradas aparecem antes das preferências e não somem após a quarta tag', () => {
  const html = renderizar({ cliente: { ...conversa.cliente, tags: ['Molho à parte', 'Recorrente', 'Retirada', 'Contato confirmado', 'Lactose', 'Glúten'] } })
  assert.ok(html.indexOf('Lactose') < html.indexOf('Molho à parte'))
  assert.ok(html.indexOf('Glúten') < html.indexOf('Molho à parte'))
  assert.equal((html.match(/class="tagRestricao"/g) ?? []).length, 2)
  const muitas = renderizar({ cliente: { ...conversa.cliente, tags: Array.from({ length: 6 }, (_, i) => `Lactose ${i + 1}`) } })
  assert.equal((muitas.match(/class="tagRestricao"/g) ?? []).length, 6, 'nenhuma restrição configurada é truncada')
})
const { BlocoOcorrenciasApi } = await import('../src/features/ficha-cliente/BlocoOcorrenciasApi.jsx')
for (const gerente of [true, false]) provar(`abertura manual de ocorrência ${gerente ? 'disponível à gerente' : 'oculta para operador'}`, () => {
  const html = renderToStaticMarkup(h(ContextoAcessoModulos.Provider, { value: { acoes: { gerenciarOcorrencias: gerente } } },
    h(ContextoAcoes.Provider, { value: {} }, h(BlocoOcorrenciasApi, { pedidoId: 'pedido-ana', conversaId: 'ana' }))))
  assert.equal(html.includes('Registrar ocorrência'), gerente)
  assert.doesNotMatch(html, /O que aconteceu\?|Salvar ocorrência/)
})
console.log(`${total} cenários reais de renderização da ficha aprovados.`)
