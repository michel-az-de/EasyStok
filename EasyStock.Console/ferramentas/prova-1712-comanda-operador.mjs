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
provar('ajuda e transporte estão disponíveis antes de existir pedido, na mesma seleção', () => {
  const html = renderizar()
  assert.match(html, /Pedido de ajuda registrado/)
  assert.match(html, /Consultar Lalamove/)
  assert.match(html, /href="https:\/\/web.lalamove.com\/" target="_blank" rel="noopener noreferrer"/)
  assert.match(html, /<button(?![^>]*disabled)[^>]*>Combinar entrega desta seleção<\/button>/)
  assert.equal((html.match(/Quantidade de Lasanha/g) ?? []).length, 1)
  assert.doesNotMatch(html, /Confirmar alteração|Gerar cobrança/)
})
provar('endereço ausente explica o requisito e bloqueia encaminhamento', () => {
  const html = renderizar({ rascunho: { ...comanda.rascunho, endereco: null } })
  assert.match(html, /<button[^>]*disabled=""[^>]*>Combinar entrega desta seleção<\/button>/)
  assert.match(html, /informe o endereço na comanda/)
})
provar('edição local preservada exige salvar antes de encaminhar ou combinar frete', () => {
  const edicao = { revisao: 4, rascunho: comanda.rascunho }
  assert.match(renderizar({}, edicao), /<button[^>]*disabled=""[^>]*>Combinar entrega desta seleção<\/button>/)
  const html = renderizar({ entregaACombinar: true }, edicao)
  assert.match(html, /<button[^>]*disabled=""[^>]*>Confirmar frete combinado<\/button>/)
  assert.match(html, /Salve os ajustes da seleção/)
})
provar('entrega encaminhada abre o frete e não repete a ação de encaminhar', () => {
  const html = renderizar({ entregaACombinar: true })
  assert.match(html, /class="transporteCompartilhado" open=""/)
  assert.doesNotMatch(html, /class="enderecoCompartilhado" open=""/)
  assert.match(html, /Confirmar frete combinado/)
  assert.ok(html.indexOf('Quantidade de Lasanha') < html.indexOf('class="transporteCompartilhado"'), 'itens devem aparecer antes do formulário de frete')
  assert.ok(html.indexOf('type="date"') >= 0 && html.indexOf('type="date"') < html.indexOf('class="transporteCompartilhado"'), 'horário deve aparecer antes do formulário de frete')
  assert.doesNotMatch(html, /Combinar entrega desta seleção/)
})
provar('frete confirmado fica visível sem voltar a oferecer encaminhamento', () => {
  const html = renderizar({ freteCombinado: 17.5 })
  assert.match(html, /Frete combinado: R\$\s*17,50/)
  assert.match(html, /Confira o total com o cliente e confirme o pedido aqui ou pelo link/)
  assert.doesNotMatch(html, /Combinar entrega desta seleção/)
})
provar('pedido persistido não oferece encaminhamento pré-confirmação', () => {
  const html = renderizar({ podeEditar: false, pedido: { total: 38, alocado: 38 } })
  assert.doesNotMatch(html, /Combinar entrega desta seleção|Confirmar frete combinado/)
})
provar('acesso a Entregas respeita permissão e orienta registro na viagem', () => {
  const render = permite => renderToStaticMarkup(createElement(ContextoAcessoModulos.Provider, { value: { permite: () => permite } }, createElement(ConsultaTransporte)))
  assert.match(render(true), /href="#\/m\/entregas"/)
  assert.match(render(true), /Registrar corrida/)
  assert.doesNotMatch(render(false), /href="#\/m\/entregas"/)
})
provar('seleção sem janela mostra a criação bloqueada com requisito explícito', () => {
  const html = renderizar({ freteCombinado: 0 })
  assert.match(html, /<button[^>]*disabled=""[^>]*>Confirmar pedido com o cliente<\/button>/)
  assert.match(html, /Escolha uma janela de entrega disponível e salve a seleção/)
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
  assert.match(html, /a cobrança será feita depois pelo atendimento/)
})
provar('revisão alterada impede confirmar resumo antigo', () => {
  const html = renderToStaticMarkup(createElement(ConfirmacaoPedidoCompartilhado, {
    comanda: { ...comanda, freteCombinado: 0 }, janela: '14/10 · 12:00 às 13:00', desatualizado: true, aoFechar() {}, aoConfirmar() {},
  }))
  assert.match(html, /A seleção mudou/)
  assert.match(html, /<button[^>]*disabled=""[^>]*>Criar pedido<\/button>/)
})
console.log(`${total} provas de estados da comanda do operador passaram.`)
