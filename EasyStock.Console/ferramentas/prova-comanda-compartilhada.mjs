import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'

const arquivo = new URL('../src/features/ficha-cliente/ComandaDoCliente.jsx', import.meta.url)
const compilado = (await transformWithOxc(readFileSync(arquivo, 'utf8'), arquivo.pathname, { jsx: { runtime: 'automatic' } })).code
const modalArquivo = new URL('../src/features/atendimento/ModalEnviarCardapio.jsx', import.meta.url)
const modalCompilado = (await transformWithOxc(readFileSync(modalArquivo, 'utf8'), modalArquivo.pathname, { jsx: { runtime: 'automatic' } })).code
const compartilharArquivo = new URL('../src/features/atendimento/CompartilharComanda.jsx', import.meta.url)
const compartilharCompilado = (await transformWithOxc(readFileSync(compartilharArquivo, 'utf8'), compartilharArquivo.pathname, { jsx: { runtime: 'automatic' } })).code
const contexto = { dados: [], erro: '', ocupado: false, estados: [], cursor: 0, acoes: { estornarPagamentoOnline: true },
  salvar: async (...args) => { contexto.salvo = args; return contexto.sucesso !== false },
  confirmar: (...args) => { contexto.confirmado = args } }
globalThis.__comandaCompartilhada = contexto
const codigo = fonte => 'data:text/javascript,' + encodeURIComponent(fonte)
registerHooks({
  resolve(spec, ctx, proximo) {
    if (spec === 'react') return { url: codigo('export function useState(inicial){const c=globalThis.__comandaCompartilhada;const n=c.cursor++;if(!(n in c.estados))c.estados[n]=inicial;return [c.estados[n],v=>c.estados[n]=v]} export const useRef=inicial=>useState({current:inicial})[0]'), shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: codigo('export const Fragment="fragment"; export const jsx=(type,props,key)=>({type,props,key}); export const jsxs=jsx'), shortCircuit: true }
    if (spec.endsWith('.css')) return { url: codigo('export default {}'), shortCircuit: true }
    if (spec.endsWith('useComandasCliente')) return { url: codigo('export const useComandasCliente=()=>globalThis.__comandaCompartilhada'), shortCircuit: true }
    if (spec.endsWith('/contextos')) return { url: codigo('export const useCatalogo=()=>({cardapio:globalThis.__comandaCompartilhada.cardapio,canais:[]});export const useAcoes=()=>globalThis.__comandaCompartilhada;export const useAtendimento=()=>({agora:Date.now()})'), shortCircuit: true }
    if (spec.endsWith('/ModalEnviarCardapio')) return { url: modalArquivo.href, shortCircuit: true }
    if (spec.endsWith('/acessoModulos')) return { url: codigo('export const useAcessoModulos=()=>globalThis.__comandaCompartilhada'), shortCircuit: true }
    if (spec.endsWith('/Botao')) return { url: codigo('export function Botao(){}'), shortCircuit: true }
    if (spec.endsWith('/Modal')) return { url: codigo('export function Modal(){}'), shortCircuit: true }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return proximo(spec + '.js', ctx)
    return proximo(spec, ctx)
  },
  load(url, ctx, proximo) {
    if (url === arquivo.href || url === modalArquivo.href) return { format: 'module', source: url === arquivo.href ? compilado : modalCompilado, shortCircuit: true }
    if (url === compartilharArquivo.href) return { format: 'module', source: compartilharCompilado, shortCircuit: true }
    return proximo(url, ctx)
  },
})
const { ComandaDoCliente } = await import(arquivo.href)
const { faltaPagar, totalPago } = await import('../src/dominio/pagamento.js')
const { partesComLinkDoCardapio } = await import('../src/dominio/cardapioLink.js')
const { combinaBusca } = await import('../src/dominio/conversa.js')
function elementos(arvore) {
  if (Array.isArray(arvore)) return arvore.flatMap(elementos)
  if (!arvore?.props) return []
  if (typeof arvore.type === 'function' && !['Botao', 'Modal'].includes(arvore.type.name)) return elementos(arvore.type(arvore.props))
  return [arvore, ...elementos(arvore.props.children), ...elementos(arvore.props.rodape)]
}
const comanda = { linkId: 'link', numeroComanda: 72, revisao: 7, podeEditar: true, totalPrevisto: 40, pedido: null,
  selecao: [{ cardapioItemId: 'prato', variacaoId: 'porcao', nome: 'Lasanha', qtd: 2, preco: 20 }],
  rascunho: { itens: [{ cardapioItemId: 'prato', variacaoId: 'porcao', qtd: 2 }], observacoes: '', pedidoId: null } }
contexto.dados = [comanda]
contexto.cardapio = [{ sku: 'prato', nome: 'Lasanha', preco: 50 }, { sku: 'novo', nome: 'Ravióli', preco: 15 },
  { sku: 'validacao', nome: 'Em validação', emValidacao: true }, { sku: 'removido', nome: 'Removido', removidoEm: 'hoje' }]
function render() { contexto.cursor = 0; return elementos(ComandaDoCliente({ conversaId: 'conversa', numeroComanda: 72, ativo: true })) }
let arvore = render()
assert.equal(arvore.find(e => e.type === 'span' && Array.isArray(e.props.children) && e.props.children.includes('Lasanha')).props.children.at(-1), 'R$ 40,00', 'preço salvo vence catálogo alterado')
assert.ok(!arvore.some(e => e.type === 'option' && ['validacao', 'removido'].includes(e.props.value)))
arvore.find(e => e.props['aria-label'] === 'Tirar uma unidade de Lasanha').props.onClick()
assert.equal(contexto.salvo, undefined, 'edição local só publica ao salvar')
arvore = render()
arvore.find(e => e.type === 'textarea').props.onChange({ target: { value: 'Molho à parte' } })
comanda.revisao = 8
comanda.rascunho.itens[0].qtd = 3
arvore = render()
assert.equal(arvore.find(e => e.type === 'textarea').props.value, 'Molho à parte', 'SSE não apaga digitação')
assert.ok(arvore.some(e => e.props.role === 'alert'), 'conflito exige decisão explícita')
assert.ok(!arvore.some(e => e.props.children === 'Salvar seleção'), 'não sobrescreve seleção remota sem conferir')
arvore.find(e => e.props.children === 'Manter minha edição para conferir').props.onClick()
arvore = render()
contexto.sucesso = false
await arvore.find(e => e.props.children === 'Salvar seleção').props.onClick()
assert.equal(contexto.salvo[0].revisao, 8)
assert.equal(contexto.salvo[1].itens[0].qtd, 1)
assert.equal(contexto.salvo[1].observacoes, 'Molho à parte')
assert.equal(render().find(e => e.type === 'textarea').props.value, 'Molho à parte', 'erro de API preserva edição')
contexto.sucesso = true
await render().find(e => e.props.children === 'Salvar seleção').props.onClick()
assert.equal(contexto.estados[0], null)
arvore = render()
arvore.find(e => e.type === 'textarea').props.onChange({ target: { value: 'Sem queijo' } })
comanda.rascunho.pedidoAtualizadoEm = '2026-10-11T22:00:00Z'
arvore = render()
assert.ok(arvore.some(e => e.props.role === 'alert'), 'pedido pode mudar sem revisão nova do link')
arvore.find(e => e.props.children === 'Manter minha edição para conferir').props.onClick()
await render().find(e => e.props.children === 'Salvar seleção').props.onClick()
assert.equal(contexto.salvo[1].pedidoAtualizadoEm, '2026-10-11T22:00:00Z')
arvore = render()
arvore.find(e => e.type === 'select').props.onChange({ target: { value: 'novo' } })
render().find(e => e.props.children === 'Adicionar').props.onClick()
assert.equal(contexto.estados[0].rascunho.itens.at(-1).cardapioItemId, 'novo')
comanda.podeEditar = false; comanda.envio = { situacao: 'alteracao_aguardando_confirmacao', saldo: -20 }
comanda.pedido = { total: 60, alocado: 60, itens: [{ quantidade: 3, nome: 'Lasanha' }] }
arvore = render()
assert.ok(!arvore.some(e => e.type === 'textarea'), 'link consumido só acompanha proposta salva')
arvore.find(e => e.props.children === 'Manter pedido original').props.onClick()
assert.deepEqual(contexto.confirmado, ['link', false])
contexto.acoes.estornarPagamentoOnline = false
assert.equal(render().find(e => e.props.children === 'Confirmar alteração').props.disabled, true)
comanda.envio = { situacao: 'pedido_confirmado', saldo: 10 }
comanda.pedido = { total: 40, alocado: 40 }
assert.ok(render().some(e => e.type === 'p' && e.props.children === 'Sem diferença de pagamento.'), 'pagamento complementar elimina saldo histórico do envio')
comanda.financeiro = { recebido: 25, devolvido: 10, convertidoEmCupom: 0, cupomAplicado: 0, reservado: 0, alocado: 15, dinheiroDisponivel: 15 }
comanda.acerto = { tipo: 'estorno', valor: 10, situacao: 'pendente', devolucoes: [{ operacaoId: 'estorno', valor: 10, meio: 'Pix', situacao: 'pendente' }] }
arvore = render()
assert.ok(arvore.some(e => e.type === 'p' && e.props.children?.[0] === 'Dinheiro recebido: ' && e.props.children[1] === 'R$ 25,00'), 'recebimento original preservado')
assert.ok(arvore.some(e => e.type === 'p' && e.props.children?.[0] === 'Cobertura atual do pedido: ' && e.props.children[1] === 'R$ 15,00'), 'cobertura líquida distinta do dinheiro bruto')
assert.ok(arvore.some(e => e.type === 'b' && e.props.children.includes('Em acompanhamento') && e.props.children.includes('R$ 10,00')), 'estorno pendente conserva valor sem confirmação prematura')
comanda.acerto.situacao = 'confirmado'; comanda.acerto.devolucoes[0].situacao = 'confirmado'
arvore = render()
assert.ok(arvore.some(e => e.type === 'b' && e.props.children.includes('Confirmado') && e.props.children.includes('R$ 10,00')), 'estorno confirmado conserva valor da operação')
assert.ok(arvore.some(e => e.type === 'p' && typeof e.props.children === 'string' && e.props.children.includes('prazo para aparecer na conta')), 'confirmação do provedor não promete crédito bancário observado')
comanda.acerto = { tipo: 'cupom', valor: 10, situacao: 'confirmado', codigoCupom: 'CASA10', saldoCupom: 0, devolucoes: [] }
assert.ok(render().some(e => e.type === 'p' && e.props.children?.[0] === 'Código do cupom: ' && e.props.children[1] === 'CASA10'), 'cupom tem código e valor próprios')
assert.ok(render().some(e => e.type === 'p' && e.props.children?.[0] === 'Saldo atual do cupom: ' && e.props.children[1] === 'R$ 0,00'), 'cupom consumido conserva código e saldo zero autoritativo')
comanda.acerto = { tipo: 'estorno', valor: 20, situacao: 'recusado', devolucoes: [{ operacaoId: 'confirmado', valor: 10, meio: 'Pix', situacao: 'confirmado' }, { operacaoId: 'recusado', valor: 10, meio: 'Cartão', situacao: 'recusado' }] }
assert.ok(render().some(e => e.type === 'p' && typeof e.props.children === 'string' && e.props.children.includes('valores confirmados e não confirmados')), 'recusa permite conferir devoluções parcialmente confirmadas')
assert.ok(render().some(e => e.type === 'b' && e.props.children.includes('Precisa de conferência')), 'acerto parcial não esconde a operação já confirmada')
for (const [meio, status] of [['Pix', 'Confirmado'], ['Cartão', 'Não confirmado']]) assert.ok(render().some(e => e.type === 'p' && e.props.children?.includes?.(meio) && e.props.children.includes(status)), 'cada devolução mantém seu estado: ' + meio)
comanda.acerto = null
comanda.financeiro.cupomAplicado = 10; comanda.financeiro.alocado = 25
comanda.envio.cupomAplicado = { usoId: 'uso', codigo: 'CASA10', valor: 10, saldoCupom: 0 }
arvore = render()
assert.ok(arvore.some(e => e.type === 'b' && e.props.children === 'Cupom aplicado neste envio'), 'uso do cupom tem recibo nesta edição')
assert.ok(arvore.some(e => e.type === 'p' && e.props.children?.[0] === 'CASA10' && e.props.children.includes('R$ 10,00') && e.props.children.includes('R$ 0,00')), 'valor utilizado e saldo atual vêm do uso persistido')
delete comanda.envio.cupomAplicado
delete comanda.financeiro; delete comanda.acerto
const pedido = { pedidoId: 'pedido', totalApi: 50, alocadoApi: 30, pagamentosApi: [{ valor: 40, coberto: 40 }], frete: 0, itens: [] }
assert.equal(totalPago(pedido), 40)
assert.equal(faltaPagar(pedido, []), 20)
const link = 'https://casadababa.com/comanda.html?pedido_conversa=TOKEN'
assert.equal(partesComLinkDoCardapio(link, 'https://app.easystok.online')[0].tipo, 'link')
assert.equal(partesComLinkDoCardapio(link.replace('casadababa.com', 'malicioso.com'), 'https://app.easystok.online')[0].tipo, 'texto')
assert.equal(partesComLinkDoCardapio(link, null)[0].tipo, 'texto')
assert.equal(combinaBusca({ nome: 'Maria', numeroComanda: 72, mensagens: [] }, 'comanda 72'), true)
console.log('Comanda: conflito preserva edição, falha API preserva dados, adição de prato, permissão de devolução, saldo líquido, link seguro e busca por número aprovados.')
const { ModalEnviarCardapio } = await import(modalArquivo.href)
contexto.estados = []
let concluirEnvio, envios = 0, fechamentos = 0
const props = { nomeCliente: 'Maria', canalNome: 'WhatsApp', link, numeroComanda: 72, texto: 'Convite',
  aoEnviar: () => { envios++; return new Promise(resolve => { concluirEnvio = resolve }) }, aoFechar: () => { fechamentos++ } }
function renderModal() { contexto.cursor = 0; return elementos(ModalEnviarCardapio(props)) }
const botao = renderModal().find(e => e.props.children === 'Enviar link')
const primeiroEnvio = botao.props.onClick()
await botao.props.onClick()
assert.equal(envios, 1, 'cliques repetidos não repetem convite')
assert.equal(fechamentos, 0, 'modal aguarda resposta do envio')
assert.equal(renderModal().find(e => e.props.children === 'Enviando…').props.disabled, true)
concluirEnvio(false); await primeiroEnvio
assert.equal(fechamentos, 0, 'falha mantém link e modal acessíveis')
assert.ok(renderModal().some(e => e.props.role === 'alert'))
const novaTentativa = renderModal().find(e => e.props.children === 'Enviar link').props.onClick()
await Promise.resolve()
concluirEnvio(true); await novaTentativa
assert.equal(fechamentos, 1)
console.log('Compartilhamento: espera servidor, bloqueia envio repetido, mantém modal na falha e fecha somente com sucesso.')

const { CompartilharComanda } = await import(compartilharArquivo.href)
contexto.estados = []
let geracoes = 0
contexto.obterLinkCardapio = async () => { geracoes++; return { url: link, numeroComanda: 72, daLoja: true } }
function renderCompartilhar() {
  contexto.cursor = 0
  return CompartilharComanda({ conversa: { id: 'conversa', nome: 'Maria', numeroComanda: 72, mensagens: [] } }).props.children
}
renderCompartilhar()[0].props.onClick()
let modal = renderCompartilhar()[1]
assert.equal(geracoes, 0, 'abrir compartilhar não substitui o link ativo do cliente')
modal.props.aoFechar()
assert.equal(renderCompartilhar()[1], false)
assert.equal(geracoes, 0, 'cancelar não gera edição nem invalida o link ativo')
renderCompartilhar()[0].props.onClick()
modal = renderCompartilhar()[1]
const gerado = await modal.props.aoGerar()
assert.equal(gerado.link, link)
assert.ok(gerado.texto.includes(link), 'convite usa somente o endereço retornado pela API')

contexto.estados = []
geracoes = 0; fechamentos = 0
let liberarGeracao, mensagemEnviada
Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { clipboard: {
  writeText: async () => { throw new Error('Permissão negada') },
} } })
const propsNovo = { nomeCliente: 'Maria', canalNome: 'WhatsApp', numeroComanda: 72,
  aoGerar: () => { geracoes++; return new Promise(resolve => { liberarGeracao = resolve }) },
  aoEnviar: async mensagem => { mensagemEnviada = mensagem; return true }, aoFechar: () => { fechamentos++ } }
function renderNovo() { contexto.cursor = 0; return elementos(ModalEnviarCardapio(propsNovo)) }
assert.ok(!renderNovo().some(e => e.type === 'a'), 'prévia não inventa URL antes da geração')
const copia = renderNovo().find(e => e.props.children === 'Gerar e copiar').props.onClick()
await renderNovo().find(e => e.props.children === 'Gerar e enviar').props.onClick()
renderNovo().find(e => e.type.name === 'Modal').props.aoFechar()
assert.equal(geracoes, 1, 'cópia e envio concorrentes compartilham a trava de geração')
assert.equal(fechamentos, 0, 'não fecha no meio da geração')
liberarGeracao(gerado); await copia
assert.ok(renderNovo().some(e => e.props.role === 'alert'))
assert.equal(renderNovo().find(e => e.type === 'a').props.href, link, 'falha de clipboard mantém a URL real para cópia manual')
await renderNovo().find(e => e.props.children === 'Enviar link').props.onClick()
assert.equal(geracoes, 1, 'envio reutiliza o link já gerado, mesmo após falha na cópia')
assert.equal(mensagemEnviada, gerado.texto)
assert.equal(fechamentos, 1)
console.log('Compartilhamento: abrir/cancelar sem geração, uma ação explícita, convite real e reutilização após falha de cópia aprovados.')
