// Estados reais dos componentes em SSR. Esta prova não substitui a jornada com API e navegador.
import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { transformWithOxc } from 'vite'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map()
for (const arquivo of [
  ...readdirSync(join(raiz, 'componentes')).filter(n => n.endsWith('.jsx')).map(n => join(raiz, 'componentes', n)),
  join(raiz, 'features/ficha-cliente/ComandaDoCliente.jsx'),
  join(raiz, 'features/ficha-cliente/Cabecalho.jsx'),
  join(raiz, 'features/ficha-cliente/BlocoFreteApi.jsx'),
  join(raiz, 'features/ficha-cliente/SeletorJanelaApi.jsx'),
  join(raiz, 'aplicacao/acessoModulos.jsx'),
]) {
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env.DEV', 'false')
  jsx.set(pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code)
}
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
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: "export const FONTE_API=true;export const API_BASE='';", shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})
const { SelecaoCompartilhada, ConfirmacaoPedidoCompartilhado } = await import('../src/features/ficha-cliente/ComandaDoCliente.jsx')
const { ConsultaTransporte } = await import('../src/features/ficha-cliente/BlocoFreteApi.jsx')
const { ContextoRascunhosDosAjustes } = await import('../src/aplicacao/rascunhosDosAjustes.js')
const { ContextoAcessoModulos } = await import('../src/aplicacao/acessoModulos.jsx')
const { ContextoAcoes } = await import('../src/aplicacao/contextos.js')
const comanda = {
  linkId: 'link-a', revisao: 4, podeEditar: true, ajudaSolicitada: true, totalPrevisto: 38,
  rascunho: { itens: [{ cardapioItemId: 'lasanha', qtd: 1 }], endereco: { logradouro: 'Rua A', numero: '10', bairro: 'Centro', cidade: 'São Paulo', uf: 'SP', cep: '01001000' } },
  selecao: [{ cardapioItemId: 'lasanha', nome: 'Lasanha', qtd: 1, preco: 38 }],
}
const renderizar = (extra = {}, edicao = null) => renderToStaticMarkup(createElement(ContextoRascunhosDosAjustes.Provider,
  { value: { rascunhos: { 'comanda-cliente:link-a': edicao }, definir() {} } },
  createElement(ContextoAcoes.Provider, { value: { carregarJanelasComanda: async () => ({ janelas: [], lojaDisponivel: true }) } },
    createElement(SelecaoCompartilhada, { comanda: { ...comanda, ...extra }, cardapio: [], ocupado: false,
      encaminharEntrega() {}, combinarFrete() {}, confirmarPedido() {}, salvar() {}, confirmar() {} }))))

let total = 0
const provar = (nome, fn) => { fn(); total++; console.log('ok ' + nome) }
provar('ajuda e combinar entrega estão disponíveis antes de existir pedido, sem o link da Lalamove (#1891)', () => {
  const html = renderizar()
  assert.match(html, /Pedido de ajuda registrado/)
  assert.doesNotMatch(html, /Lalamove|lalamove\.com/, 'a Lalamove está congelada: nem link nem menção na seleção')
  assert.match(html, /<summary>Combinar entrega<\/summary>/)
  assert.match(html, /<button(?![^>]*disabled)[^>]*>Combinar entrega dos itens<\/button>/)
  assert.equal((html.match(/Quantidade de Lasanha/g) ?? []).length, 1)
  assert.doesNotMatch(html, /Confirmar alteração|Gerar cobrança/)
})
provar('endereço ausente explica o requisito e bloqueia encaminhamento', () => {
  const html = renderizar({ rascunho: { ...comanda.rascunho, endereco: null } })
  assert.match(html, /<button[^>]*disabled=""[^>]*>Combinar entrega dos itens<\/button>/)
  assert.match(html, /registre na Ficha/)
})
provar('edição local preservada exige salvar antes de encaminhar ou combinar frete', () => {
  const edicao = { revisao: 4, rascunho: comanda.rascunho }
  assert.match(renderizar({}, edicao), /<button[^>]*disabled=""[^>]*>Combinar entrega dos itens<\/button>/)
  const html = renderizar({ entregaACombinar: true }, edicao)
  assert.match(html, /<button[^>]*disabled=""[^>]*>Confirmar frete combinado<\/button>/)
  assert.match(html, /Salve os ajustes dos itens/)
})
provar('entrega encaminhada abre o frete e não repete a ação de encaminhar', () => {
  const html = renderizar({ entregaACombinar: true })
  assert.match(html, /class="transporteCompartilhado" open=""/)
  assert.doesNotMatch(html, /class="enderecoCompartilhado" open=""/)
  assert.match(html, /Confirmar frete combinado/)
  assert.ok(html.indexOf('Quantidade de Lasanha') < html.indexOf('class="transporteCompartilhado"'), 'itens devem aparecer antes do formulário de frete')
  assert.ok(html.indexOf('type="date"') >= 0 && html.indexOf('type="date"') < html.indexOf('class="transporteCompartilhado"'), 'horário deve aparecer antes do formulário de frete')
  assert.doesNotMatch(html, /Combinar entrega dos itens/)
})
provar('frete confirmado fica visível sem voltar a oferecer encaminhamento', () => {
  const html = renderizar({ freteCombinado: 17.5 })
  assert.match(html, /Frete combinado: R\$\s*17,50/)
  assert.match(html, /Salvar os itens atualiza o link/)
  assert.doesNotMatch(html, /Combinar entrega dos itens/)
})
provar('entrega completa recolhe campos; horário ou frete pendentes mantêm os campos abertos', () => {
  const completo = { freteCombinado: 0, rascunho: { ...comanda.rascunho, janelaId: 'j1', dataEntrega: '2026-10-14' } }
  assert.match(renderizar(completo), /<details class="entregaDaSelecao"><summary>Entrega e horário/)
  assert.match(renderizar({ ...completo, freteCombinado: null }), /<details class="entregaDaSelecao" open=""/)
  assert.match(renderizar({ freteCombinado: 0 }), /<details class="entregaDaSelecao" open=""/)
  assert.match(renderizar({ ...completo, entregaACombinar: true }), /<details class="entregaDaSelecao" open=""/)
})
provar('pedido persistido não oferece encaminhamento pré-confirmação', () => {
  const html = renderizar({ podeEditar: false, pedido: { total: 38, alocado: 38 } })
  assert.doesNotMatch(html, /Combinar entrega dos itens|Confirmar frete combinado/)
})
// #1891: a consulta à Lalamove está congelada (dominio/congelados.js); o bloco não renderiza nada,
// com ou sem permissão em Entregas. Ao descongelar, volta a orientar o registro da corrida na viagem.
provar('consulta de transporte congelada não renderiza, com ou sem acesso a Entregas', () => {
  const render = permite => renderToStaticMarkup(createElement(ContextoAcessoModulos.Provider, { value: { permite: () => permite } }, createElement(ConsultaTransporte)))
  assert.equal(render(true), '')
  assert.equal(render(false), '')
})
provar('seleção sem janela mostra a criação bloqueada com requisito explícito', () => {
  const html = renderizar({ freteCombinado: 0 })
  assert.match(html, /<button[^>]*disabled=""[^>]*>Revisar e criar pedido<\/button>/)
  assert.match(html, /Escolha uma janela de entrega disponível e salve os itens/)
})
provar('confirmação exibe os valores persistidos, horário e endereço, inclusive frete zero', () => {
  const html = renderToStaticMarkup(createElement(ConfirmacaoPedidoCompartilhado, {
    comanda: { ...comanda, freteCombinado: 0 }, janela: '14/10 · 12:00 às 13:00', aoFechar() {}, aoConfirmar() {},
  }))
  assert.match(html, /1× Lasanha/)
  assert.match(html, /Rua A, 10/)
  assert.match(html, /14\/10 · 12:00 às 13:00/)
  assert.match(html, /Frete combinado: <strong>R\$\s*0,00/)
  assert.match(html, /Total com frete: <strong>R\$\s*38,00/)
  assert.match(html, /Os itens só viram pedido ao tocar em Criar pedido/)
  assert.match(html, /Se houver saldo, ele ficará aguardando pagamento/)
})
provar('revisão alterada impede confirmar resumo antigo', () => {
  const html = renderToStaticMarkup(createElement(ConfirmacaoPedidoCompartilhado, {
    comanda: { ...comanda, freteCombinado: 0 }, janela: '14/10 · 12:00 às 13:00', desatualizado: true, aoFechar() {}, aoConfirmar() {},
  }))
  assert.match(html, /Os itens mudaram/)
  assert.match(html, /<button[^>]*disabled=""[^>]*>Criar pedido<\/button>/)
})
provar('salvar seleção não se apresenta como criação do pedido', () => {
  const html = renderizar({}, { revisao: 4, rascunho: comanda.rascunho })
  assert.match(html, /Não confirmados/)
  assert.match(html, /Salvar itens atualiza o link, sem criar um pedido/)
  assert.match(html, /Salvar itens<\/button>/)
  assert.doesNotMatch(html, /Revisar e criar pedido<\/button>|Pedido criado/)
})
for (const podeEditar of [true, false]) provar(`alteração ${podeEditar ? 'em edição' : 'enviada'} separa o pedido atual da proposta`, () => {
  const html = renderizar({ podeEditar, pedido: { total: 76, alocado: 76, itens: [{ quantidade: 2, nome: 'Lasanha original' }] },
    envio: podeEditar ? null : { situacao: 'alteracao_aguardando_confirmacao', total: 38 } })
  assert.match(html, /Pedido atual mantido/)
  assert.match(html, /Pedido atual · R\$\s*76,00/)
  assert.match(html, /2× Lasanha original/)
  assert.match(html, /Alteração proposta/)
  assert.match(html, /Total proposto: <strong>R\$\s*38,00/)
  assert.match(html, /Após aprovar a alteração: A devolver: R\$\s*38,00/)
  assert.doesNotMatch(html, /Revisar e criar pedido/)
})
provar('alteração recusada é histórico e mantém o total do pedido original', () => {
  const html = renderizar({ podeEditar: false, pedido: { total: 76, alocado: 76, itens: [] }, envio: { situacao: 'alteracao_recusada' } })
  assert.match(html, /Alteração recusada/)
  assert.match(html, /Pedido atual · R\$\s*76,00/)
  assert.match(html, /Itens enviados pelo link/)
  assert.doesNotMatch(html, /Confirmar alteração|Total proposto|Após aprovar/)
})
console.log(`${total} provas de estados da comanda do operador passaram.`)
