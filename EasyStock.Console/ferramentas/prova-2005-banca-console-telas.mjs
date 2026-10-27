/* eslint-disable no-console */
// Prova da #2005, parte das telas: as Fichas montadas de verdade no react-dom, sobre um DOM mínimo escrito
// aqui (o mesmo desenho da prova-1577-balcao-render, com campo e seleção), e as outras por marcação estática.
// A digitação chama o `onChange` que o React guardou no campo: é o código da tela rodando, sem navegador.
// Cada cenário desmonta a tela ao fim.
//
//   04  dinheiro em reais igual em todos os campos operacionais, com o valor sugerido pré-carregado
//   01  Cobrar diferença não aparece no modo API; sinal pago mostra o restante, não "item novo"
//   09  Ver mensagens anteriores no topo do fio
//   22  status que o console não conhece não oferece cobrança nem baixa
//
//   node ferramentas/prova-2005-banca-console-telas.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { transformWithOxc } from 'vite'

// Sem os avisos de desenvolvimento do React (ele pediria `act`); o que se confere é o resultado.
process.env.NODE_ENV = 'production'

// --- DOM mínimo -------------------------------------------------------------------------------
const HTML = 'http://www.w3.org/1999/xhtml'
class No {
  constructor(nodeType, nodeName) {
    this.nodeType = nodeType
    this.nodeName = nodeName
    this.childNodes = []
    this.parentNode = null
    this.nodeValue = null
  }
  get ownerDocument() { return documento }
  get firstChild() { return this.childNodes[0] ?? null }
  get lastChild() { return this.childNodes.at(-1) ?? null }
  get nextSibling() {
    const irmaos = this.parentNode?.childNodes ?? []
    return irmaos[irmaos.indexOf(this) + 1] ?? null
  }
  appendChild(filho) { return this.insertBefore(filho, null) }
  insertBefore(filho, antes) {
    filho.parentNode?.removeChild(filho)
    const i = antes ? this.childNodes.indexOf(antes) : -1
    if (i < 0) this.childNodes.push(filho)
    else this.childNodes.splice(i, 0, filho)
    filho.parentNode = this
    return filho
  }
  removeChild(filho) {
    const i = this.childNodes.indexOf(filho)
    if (i >= 0) this.childNodes.splice(i, 1)
    filho.parentNode = null
    return filho
  }
  contains(outro) { return outro === this || this.childNodes.some((f) => f.contains(outro)) }
  get textContent() { return this.nodeType === 3 ? this.nodeValue : this.childNodes.map((f) => f.textContent).join('') }
  set textContent(texto) {
    if (this.nodeType === 3) { this.nodeValue = texto; return }
    this.childNodes.forEach((f) => { f.parentNode = null })
    this.childNodes = []
    if (texto !== '' && texto != null) this.appendChild(new Texto(texto))
  }
  addEventListener() {}
  removeEventListener() {}
}
class Texto extends No {
  constructor(texto) { super(3, '#text'); this.nodeValue = String(texto) }
}
class Elemento extends No {
  constructor(tag, ns = HTML) {
    super(1, ns === HTML ? tag.toUpperCase() : tag)
    this.tagName = this.nodeName
    this.localName = tag
    this.namespaceURI = ns
    this.atributos = new Map()
    this.style = { setProperty(nome, valor) { this[nome] = valor }, removeProperty(nome) { delete this[nome] } }
  }
  setAttribute(nome, valor) { this.atributos.set(nome, String(valor)) }
  setAttributeNS(_ns, nome, valor) { this.atributos.set(nome, String(valor)) }
  removeAttribute(nome) { this.atributos.delete(nome) }
  removeAttributeNS(_ns, nome) { this.atributos.delete(nome) }
  getAttribute(nome) { return this.atributos.get(nome) ?? null }
  hasAttribute(nome) { return this.atributos.has(nome) }
  focus() { documento.activeElement = this }
  blur() { documento.activeElement = documento.body }
  scrollIntoView() {}
  setSelectionRange() {}
  get parentElement() { return this.parentNode?.nodeType === 1 ? this.parentNode : null }
  get isConnected() { return documento.contains(this) }
  closest() { return null }
}
class Dialogo extends Elemento {
  showModal() { this.open = true }
  close() { this.open = false }
}
// O textarea do navegador espelha `defaultValue` em `value` até alguém escrever nele; o react-dom conta com isso.
class AreaDeTexto extends Elemento {
  get defaultValue() { return this.padrao ?? '' }
  set defaultValue(valor) { this.padrao = String(valor) }
  get value() { return this.escrito ?? this.padrao ?? '' }
  set value(valor) { this.escrito = String(valor) }
  get textContent() { return this.padrao ?? '' }
  set textContent(valor) { this.padrao = String(valor) }
}
class Selecao extends Elemento {
  get options() { return this.childNodes.filter((no) => no.localName === 'option') }
}
class Opcao extends Elemento {
  get value() { return this.getAttribute('value') ?? this.textContent }
}
const criar = (tag, ns) => {
  if (tag === 'textarea') return new AreaDeTexto(tag)
  if (tag === 'select') return new Selecao(tag, ns)
  if (tag === 'option') return new Opcao(tag, ns)
  if (tag === 'dialog') return new Dialogo(tag, ns)
  return new Elemento(tag, ns)
}
const ouvintes = new EventTarget()
const documento = Object.assign(new No(9, '#document'), {
  createElement: (tag) => criar(tag),
  createElementNS: (ns, tag) => criar(tag, ns),
  createTextNode: (texto) => new Texto(texto),
  querySelector: () => null,
  getElementById: () => null,
  visibilityState: 'visible',
  addEventListener: (...a) => ouvintes.addEventListener(...a),
  removeEventListener: (...a) => ouvintes.removeEventListener(...a),
})
documento.documentElement = documento.appendChild(new Elemento('html'))
documento.body = documento.documentElement.appendChild(new Elemento('body'))
documento.activeElement = documento.body
const guardado = new Map()
const armazenamento = {
  getItem: (chave) => guardado.get(chave) ?? null,
  setItem: (chave, valor) => { guardado.set(chave, String(valor)) },
  removeItem: (chave) => { guardado.delete(chave) },
}
const janela = Object.assign(new EventTarget(), {
  document: documento,
  location: { origin: 'https://console.prova', pathname: '/', hash: '', href: 'https://console.prova/' },
  HTMLIFrameElement: class {},
  matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }),
  sessionStorage: armazenamento,
  localStorage: armazenamento,
})
documento.defaultView = janela
for (const [nome, valor] of Object.entries({ window: janela, document: documento, sessionStorage: armazenamento, localStorage: armazenamento })) {
  Object.defineProperty(globalThis, nome, { value: valor, configurable: true, writable: true })
}

// --- Carga dos módulos do console no node -----------------------------------------------------
const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const arquivosDe = (dir) => readdirSync(dir, { withFileTypes: true })
  .flatMap((e) => (e.isDirectory() ? arquivosDe(join(dir, e.name)) : [join(dir, e.name)]))
const jsx = new Map()
await Promise.all(arquivosDe(raiz).filter((a) => a.endsWith('.jsx')).map(async (arquivo) => {
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env.DEV', 'false').replaceAll('import.meta.env', '({ DEV: false })')
  jsx.set(pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code)
}))
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      for (const extensao of ['.js', '.jsx']) {
        try { return proximo(especificador + extensao, contexto) } catch { /* próximo formato */ }
      }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/features/ficha-cliente/MenuDoPedido.jsx')) {
      return {
        format: 'module', shortCircuit: true,
        source: "import { createElement as h } from 'react'\nexport const MenuDoPedido = ({ podeEstornar, aoEscolher }) => (podeEstornar ? h('button', { type: 'button', onClick: () => aoEscolher('estornar') }, 'Marcar estorno') : null)\n",
      }
    }
    if (jsx.has(url)) return { format: 'module', shortCircuit: true, source: jsx.get(url) }
    if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default new Proxy({}, { get: (_, p) => String(p) })' }
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

const { createElement: h, useState } = await import('react')
const { createRoot } = await import('react-dom/client')
const { flushSync } = await import('react-dom')
const { renderToStaticMarkup } = await import('react-dom/server')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}
const umInstante = () => new Promise((resolver) => { setImmediate(resolver) })
const esperar = async () => { for (let i = 0; i < 6; i += 1) await umInstante() }

// --- Ferramentas de tela ----------------------------------------------------------------------
const montar = (elemento) => {
  const recipiente = documento.createElement('div')
  documento.body.appendChild(recipiente)
  const raizReact = createRoot(recipiente)
  flushSync(() => raizReact.render(elemento))
  return { recipiente, desmontar: () => { flushSync(() => raizReact.unmount()); documento.body.removeChild(recipiente) } }
}
const descendentes = (no) => no.childNodes.flatMap((f) => [f, ...descendentes(f)])
const propsDe = (no) => no[Object.keys(no).find((k) => k.startsWith('__reactProps$'))] ?? {}
const campoDe = (tela, rotulo) => {
  const etiqueta = descendentes(tela.recipiente).find((no) => no.localName === 'label' && no.textContent === rotulo)
  assert.ok(etiqueta, `campo "${rotulo}" não está na tela`)
  const id = etiqueta.getAttribute('for')
  return descendentes(tela.recipiente).find((no) => no.getAttribute?.('id') === id)
}
const valorDe = (tela, rotulo) => campoDe(tela, rotulo).value
const digitar = (tela, rotulo, texto) => {
  const campo = campoDe(tela, rotulo)
  flushSync(() => propsDe(campo).onChange({ target: { value: texto } }))
}
const botaoDe = (tela, texto) => {
  const botao = descendentes(tela.recipiente).find((no) => no.localName === 'button' && no.textContent.trim() === texto)
  assert.ok(botao, `botão "${texto}" não está na tela`)
  return botao
}
const clicarUltimo = (tela, texto) => {
  const botoes = descendentes(tela.recipiente).filter((no) => no.localName === 'button' && no.textContent.trim() === texto)
  assert.ok(botoes.length > 0, `botão "${texto}" não está na tela`)
  flushSync(() => propsDe(botoes.at(-1)).onClick({ preventDefault() {} }))
}
const clicar = (tela, texto) => { flushSync(() => propsDe(botaoDe(tela, texto)).onClick({ preventDefault() {} })) }
const textoDe = (tela) => tela.recipiente.textContent.replaceAll(' ', ' ')
const comTela = async (elemento, fn) => {
  const tela = montar(elemento)
  try { return await fn(tela) } finally { tela.desmontar() }
}

const { BaixaAMao } = await import('../src/features/ficha-cliente/BlocoCobranca.jsx')
const { DinheiroFinanceiro } = await import('../src/features/gestao/financeiro/ModaisFinanceiras.jsx')
const { BlocoOcorrencia } = await import('../src/features/ficha-cliente/BlocoOcorrencia.jsx')
const { BlocoDevolucoes } = await import('../src/features/ficha-cliente/BlocoDevolucoes.jsx')
const { EstornosOnline } = await import('../src/features/ficha-cliente/EstornosOnline.jsx')
const { ContextoAcoes, ContextoEstado, ContextoCatalogo } = await import('../src/aplicacao/contextos.js')
const { ContextoAcessoModulos } = await import('../src/aplicacao/acessoModulos.jsx')
const { ESTADOS_OCORRENCIA } = await import('../src/dominio/ocorrencia.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { AbaCaixaApi } = await import('../src/features/gestao/caixa/AbaCaixaApi.jsx')
const { BarraProximoPasso } = await import('../src/features/ficha-cliente/BarraProximoPasso.jsx')
const { CANAIS, LINHAS_PRODUTO } = await import('../src/infra/catalogo.js')

// ================================================================================================
// 04. Dinheiro em reais em todos os campos operacionais ("100" é R$ 100,00; o sugerido continua pré-carregado)
// ================================================================================================
await confere('04. baixa à mão: "Valor recebido" nasce com o sugerido e lê "58" como R$ 58,00', async () => {
  const recebidos = []
  await comTela(h(BaixaAMao, { valorCobrado: 100, aoConfirmar: (valor, metodo) => recebidos.push({ valor, metodo }), aoCancelar() {} }), (tela) => {
    assert.equal(valorDe(tela, 'Valor recebido'), '100,00', 'o valor sugerido continua pré-carregado')
    digitar(tela, 'Valor recebido', '58')
    assert.equal(valorDe(tela, 'Valor recebido'), '58', 'o que ela digitou, sem virar R$ 0,58')
    flushSync(() => propsDe(campoDe(tela, 'Como recebeu')).onChange({ target: { value: 'dinheiro' } }))
    const formulario = descendentes(tela.recipiente).find((no) => no.localName === 'form')
    flushSync(() => propsDe(formulario).onSubmit({ preventDefault() {} }))
    assert.deepEqual(recebidos, [{ valor: 58, metodo: 'dinheiro' }])
  })
})

await confere('04. baixa à mão: apagar o campo trava o botão em vez de mandar R$ 0,00', async () => {
  await comTela(h(BaixaAMao, { valorCobrado: 40, aoConfirmar() {}, aoCancelar() {} }), (tela) => {
    digitar(tela, 'Valor recebido', '')
    assert.equal(propsDe(botaoDe(tela, 'Marcar pago')).disabled, true)
    digitar(tela, 'Valor recebido', '40,5')
    assert.equal(valorDe(tela, 'Valor recebido'), '40,5')
  })
})

await confere('04. financeiro: o valor da conta lê "100" como R$ 100,00', async () => {
  const lidos = []
  function Tela() {
    const [centavos, definir] = useState(0)
    return h(DinheiroFinanceiro, { rotulo: 'Valor total', centavos, aoMudar: (c) => { lidos.push(c); definir(c) } })
  }
  await comTela(h(Tela), (tela) => {
    digitar(tela, 'Valor total', '100')
    assert.equal(lidos.at(-1), 10000)
    digitar(tela, 'Valor total', '1.250,50')
    assert.equal(lidos.at(-1), 125050)
  })
})

const pedidoPago = { cobranca: { pagaEm: 1, valor: 100, valorPago: 100 } }
await confere('04. reembolso da ocorrência: "Valor a devolver" nasce com o pago e lê "58" como R$ 58,00', async () => {
  const reembolsos = []
  const ocorrencia = {
    estado: ESTADOS_OCORRENCIA.EM_APURACAO, historico: [{ autor: 'cliente', texto: 'Veio errado', em: '2026-10-23T12:00:00Z' }], estorno: null, notaInterna: null,
  }
  const acoes = { marcarEstorno: (...a) => reembolsos.push(a), apurarOcorrencia() {}, encerrarOcorrenciaSemEstorno() {}, bloquearCliente() {} }
  await comTela(h(ContextoAcoes.Provider, { value: acoes }, h(BlocoOcorrencia, { ocorrencia, conversaId: 'c1', nomeCliente: 'Ana', pedido: pedidoPago })), (tela) => {
    clicar(tela, 'Confirmar reembolso')
    assert.equal(valorDe(tela, 'Valor a devolver'), '100,00')
    digitar(tela, 'Valor a devolver', '58')
    assert.equal(valorDe(tela, 'Valor a devolver'), '58')
    digitar(tela, 'Motivo do reembolso', 'Veio errado')
    const confirmar = descendentes(tela.recipiente).filter((no) => no.localName === 'button' && no.textContent.trim() === 'Confirmar reembolso').at(-1)
    flushSync(() => propsDe(confirmar).onClick({}))
    assert.deepEqual(reembolsos, [['c1', 'Veio errado', 58]])
  })
})

const NO_PEDIDO = '77777777-7777-7777-7777-777777777777'
const comAcesso = (acoes, filho) => h(ContextoAcessoModulos.Provider, { value: { acoes } }, filho)
function servidorDeEstornos(corpoDaLista) {
  const chamadas = []
  globalThis.fetch = async (url, opcoes = {}) => {
    chamadas.push({ url, ...opcoes })
    return new Response(JSON.stringify({ data: corpoDaLista }), { status: 200 })
  }
  globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 600000, empresa: { id: 'e' }, usuario: { id: 'u' } }), setItem() {}, removeItem() {} }
  return chamadas
}

await confere('04. devolução manual: "Valor devolvido" nasce com o disponível e lê "58" como R$ 58,00', async () => {
  servidorDeEstornos({
    pagamentos: [{ id: 'pg1', valor: 100, manual: true, disponivel: 100, metodo: 'dinheiro', pagoEm: '2026-10-23T12:00:00Z' }], estornos: [],
  })
  await comTela(comAcesso({ devolverPagamentoManual: true }, h(BlocoDevolucoes, { pedidoId: NO_PEDIDO })), async (tela) => {
    await esperar()
    clicar(tela, 'Registrar devolução manual')
    assert.equal(valorDe(tela, 'Valor devolvido'), '100,00')
    digitar(tela, 'Valor devolvido', '58')
    assert.equal(valorDe(tela, 'Valor devolvido'), '58')
  })
})

await confere('04. estorno online: "Valor do estorno online" lê "58" como R$ 58,00 e a confirmação diz o mesmo valor', async () => {
  servidorDeEstornos({
    pagamentos: [{ id: 'pg1', valor: 100, devolvido: 0, disponivel: 100, reservado: 0, metodo: 'pix', pagoEm: '2026-10-23T12:00:00Z' }], estornos: [],
  })
  await comTela(comAcesso({ estornarPagamentoOnline: true }, h(EstornosOnline, { pedidoId: NO_PEDIDO })), async (tela) => {
    await esperar()
    clicar(tela, 'Solicitar estorno pelo Mercado Pago')
    assert.equal(valorDe(tela, 'Valor do estorno online'), '100,00')
    digitar(tela, 'Valor do estorno online', '58')
    assert.equal(valorDe(tela, 'Valor do estorno online'), '58')
    assert.match(textoDe(tela), /devolução de R\$ 58,00 pelo Mercado Pago/)
  })
})

await confere('04. o estorno da Barra do pedido e todos os campos de dinheiro da Ficha usam o campo em reais', () => {
  for (const arquivo of ['features/ficha-cliente/BarraProximoPasso.jsx', 'features/ficha-cliente/BlocoCobranca.jsx',
    'features/ficha-cliente/BlocoDevolucoes.jsx', 'features/ficha-cliente/EstornosOnline.jsx', 'features/ficha-cliente/BlocoOcorrencia.jsx',
    'features/gestao/financeiro/ModaisFinanceiras.jsx']) {
    const tela = readFileSync(new URL(`../src/${arquivo}`, import.meta.url), 'utf8')
    assert.doesNotMatch(tela, /tipo="moeda"/, `${arquivo} ainda usa a máscara de centavos`)
    assert.match(tela, /<CampoReais/, arquivo)
    assert.match(tela, /ler=\{lerMoeda\}/, arquivo)
  }
})

// ================================================================================================
// Marcação estática da Ficha
// ================================================================================================
const { RelogioPix } = await import('../src/features/ficha-cliente/RelogioPix.jsx')
const { BlocoCobranca } = await import('../src/features/ficha-cliente/BlocoCobranca.jsx')
const { PainelFicha } = await import('../src/features/ficha-cliente/PainelFicha.jsx')
const { Thread } = await import('../src/features/atendimento/Thread.jsx')

const noContexto = ({ fonteApi = true, selecionada = null, acoes = {} } = {}, filho) =>
  h(ContextoAcessoModulos.Provider, { value: { acoes: {} } },
    h(ContextoEstado.Provider, { value: { selecionada, visiveis: selecionada ? [selecionada] : [], conversas: selecionada ? [selecionada] : [], alertasEstoque: [], automaticoPausado: {},
      agora: Date.parse('2026-10-23T15:00:00Z'), fonteApi, fidelidade: { ativa: false, cupons: [] }, historico: [] } },
    h(ContextoCatalogo.Provider, { value: { cardapio: [], canais: CANAIS, linhas: LINHAS_PRODUTO, janelas: [], entregadores: [], meiosDePagamento: [] } },
    h(ContextoAcoes.Provider, { value: acoes }, filho))))

const SINAL_PAGO = {
  pedidoId: NO_PEDIDO, status: 'aguardando', total: 65, frete: 5, itens: [], totalPago: 32.5, alocado: 32.5, sinalNaReserva: true,
  pagamentos: [{ id: 'pg1', valor: 32.5, metodo: 'pix', pagoEm: '2026-10-23T14:00:00Z' }],
  cobranca: {
    cobrancaId: 'cb1', provedor: 'mercadopago', status: 'Paga', valor: 32.5, valorPago: 32.5, tentativa: 1,
    criadaEm: '2026-10-23T13:50:00Z', pagaEm: '2026-10-23T14:00:00Z', linkPagamento: 'https://pagamento.exemplo/cb1',
  },
}
const AGORA = Date.parse('2026-10-23T15:00:00Z')

// #2079 (C5): encomenda de R$ 118 com o link do sinal (R$ 59) pendente: a Cobrança dizia "Total R$ 59,00".
await confere('#2079-C5 link do sinal pendente: a Cobrança diz "Sinal agora", não "Total"', () => {
  const pedido = pedidoDaApi({
    pedidoId: NO_PEDIDO, status: 'aguardando_pagamento', total: 118, frete: 12, itens: [], totalPago: 0, alocado: 0, sinalNaReserva: true,
    pagamentos: [],
    cobranca: { cobrancaId: 'cb2', provedor: 'mercadopago', status: 'Pendente', valor: 59, tentativa: 1,
      criadaEm: '2026-10-23T14:50:00Z', expiraEm: '2026-10-23T15:20:00Z', linkPagamento: 'https://pagamento.exemplo/cb2' },
  })
  const html = renderToStaticMarkup(noContexto({}, h(BlocoCobranca, {
    pedido, agora: AGORA, editavel: true, nomeCliente: 'Ana', conversaId: 'c1', aoGerar() {}, aoReenviar() {}, aoMarcarComprovante() {},
    aoAceitarDivergencia() {}, aoAbrirCardapio() {}, aoCancelarPedido() {}, aoEncerrarAtendimento() {}, aoAlterarMeio() {},
  }))).replaceAll(' ', ' ')
  assert.doesNotMatch(html, />Total<\/span><b[^>]*>R\$ 59,00/, 'o valor do sinal vinha rotulado "Total"')
  assert.match(html, />Sinal agora<\/span><b[^>]*>R\$ 59,00/)
  assert.match(html, /Total do pedido: R\$ 118,00\. Restante na entrega: R\$ 59,00\./)
})

await confere('01. sinal pago na Cobrança: diz o restante da entrega, sem "item novo" e sem Cobrar diferença', () => {
  const pedido = pedidoDaApi(SINAL_PAGO)
  const html = renderToStaticMarkup(noContexto({}, h(BlocoCobranca, {
    pedido, agora: AGORA, editavel: true, nomeCliente: 'Ana', conversaId: 'c1', aoGerar() {}, aoReenviar() {}, aoMarcarComprovante() {},
    aoAceitarDivergencia() {}, aoAbrirCardapio() {}, aoCancelarPedido() {}, aoEncerrarAtendimento() {}, aoAlterarMeio() {},
  }))).replaceAll(' ', ' ')
  assert.match(html, /Sinal pago: R\$ 32,50\. Restante na entrega: R\$ 32,50/)
  assert.doesNotMatch(html, /Item novo depois do pagamento|Cobrar diferença/)
})

await confere('01. pagamento a menor no modo API: o relógio oferece Aceitar diferença sem Cobrar diferença; na demonstração oferece as duas', () => {
  const cobranca = { id: 'cb', meio: 'pix', valor: 50, valorPago: 30, pagaEm: AGORA - 60000, criadaEm: AGORA - 600000, expiraEm: AGORA + 600000, diferenca: false }
  const relogio = (fonteApi) => renderToStaticMarkup(noContexto({ fonteApi }, h(RelogioPix, {
    cobranca, agora: AGORA, editavel: true, numeroPedido: '1', nomeCliente: 'Ana', aoReenviar() {}, aoCancelarPedido() {},
    aoEncerrarAtendimento() {}, aoAceitarDivergencia() {}, aoCobrarDiferenca() {},
  })))
  assert.doesNotMatch(relogio(true), /Cobrar diferença/)
  assert.match(relogio(false), /Cobrar diferença/)
})

await confere('09. Ver mensagens anteriores aparece no topo do fio só com a página cheia e a ação ligada', () => {
  const fio = (extra) => renderToStaticMarkup(h(Thread, { mensagens: [], chaveRolagem: 'c1', agora: AGORA, ...extra }))
  assert.match(fio({ temAnteriores: true, aoVerAnteriores() {} }), /Ver mensagens anteriores/)
  assert.match(fio({ temAnteriores: true, abrindoAnteriores: true, aoVerAnteriores() {} }), /Abrindo/)
  assert.doesNotMatch(fio({ temAnteriores: false, aoVerAnteriores() {} }), /Ver mensagens anteriores/)
  assert.doesNotMatch(fio({ temAnteriores: true }), /Ver mensagens anteriores/, 'demonstração: sem a ação, sem botão')
})

await confere('22. pedido em status desconhecido: a Ficha mostra o status cru e não oferece cobrança nem baixa', () => {
  const pedido = pedidoDaApi({ pedidoId: NO_PEDIDO, status: 'em_analise_de_fraude', total: 50, frete: 0, itens: [], totalPago: 0 })
  const conversa = {
    id: 'c1', nome: 'Ana Ribeiro', estado: 'Em atendimento', canal: 'WhatsApp', conta: 'cliente', clienteId: 'cli', pedido,
    cliente: { telefone: '11999990001', endereco: 'Rua de teste, 10', pedidos: 1, tags: [], notas: [] }, mensagens: [],
  }
  const html = renderToStaticMarkup(noContexto({ selecionada: conversa }, h(PainelFicha, { aoAbrirCardapio() {} }))).replaceAll(' ', ' ')
  assert.match(html, /em_analise_de_fraude/)
  assert.match(html, /Status desconhecido/)
  assert.doesNotMatch(html, /Gerar cobrança|Marcar pago|Registrar sinal|Confirmar e enviar/)
})

// ================================================================================================
// 02 e 04, pelo comportamento: a aba Caixa e o estorno da Barra montados de verdade
// ================================================================================================
const corpo = (data) => new Response(JSON.stringify({ data }), { status: 200 })
const DIA_DO_CAIXA = {
  data: '2026-10-23', saldoInicial: 100, totalVendas: 0, totalPagamentosPedidos: 0, totalEntradasExtras: 0, totalSaidasExtras: 0,
  saldoEsperado: 100, aberto: true, fechado: false, movimentos: [], linhasExtras: [],
}
function servidorDoCaixa(estado) {
  const leituras = { dia: 0 }
  globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 600000, empresa: { id: 'e' }, usuario: { id: 'u' } }), setItem() {}, removeItem() {} }
  globalThis.fetch = async (url) => {
    if (/\/api\/caixa\/dia$/.test(url)) {
      leituras.dia += 1
      return estado.semRede ? new Response(JSON.stringify({ error: { message: 'Fora do ar' } }), { status: 503 }) : corpo(estado.dia)
    }
    if (url.includes('/api/caixa/dia?data=')) return corpo({ ...DIA_DO_CAIXA, aberto: false })
    return corpo([])
  }
  return leituras
}
const aba = (acoes) => h(ContextoAcessoModulos.Provider, { value: { acoes: { gerenciarCaixa: true } } },
  h(ContextoEstado.Provider, { value: { agora: AGORA, fonteApi: true } }, h(ContextoAcoes.Provider, { value: acoes }, h(AbaCaixaApi))))
const CAMPO_CONTADO = 'Quanto você contou na gaveta (só dinheiro)'

await confere('02. fechar o caixa relê o dia e, se entrou dinheiro enquanto ela contava, não fecha com o valor velho', async () => {
  const estado = { dia: DIA_DO_CAIXA }
  const leituras = servidorDoCaixa(estado)
  const fechamentos = []
  await comTela(aba({ fecharCaixa: (...a) => { fechamentos.push(a); return Promise.resolve() } }), async (tela) => {
    await esperar()
    const antes = leituras.dia
    clicar(tela, 'Fechar o caixa')
    await esperar()
    assert.equal(leituras.dia, antes + 1, 'abrir o fechamento relê o dia')
    digitar(tela, CAMPO_CONTADO, '100')
    estado.dia = { ...DIA_DO_CAIXA, linhasExtras: [{ hora: '2026-10-23T15:00:00Z', tipo: 'entrada', valor: 50, metodo: 'dinheiro', origem: 'pedido' }] }
    clicarUltimo(tela, 'Fechar o caixa')
    await esperar()
    assert.deepEqual(fechamentos, [], 'o esperado mudou: nada foi fechado com o de antes')
    assert.match(textoDe(tela), /O esperado na gaveta agora é R\$ 150,00/)
    clicarUltimo(tela, 'Fechar o caixa')
    await esperar()
    assert.equal(fechamentos.length, 1)
    assert.deepEqual([fechamentos[0][1], fechamentos[0][2]], [100, 150], 'contado e o esperado relido, não o da carga da manhã')
  })
})

await confere('02. leitura que falha ao abrir o fechamento não abre o modal nem fecha o caixa', async () => {
  const estado = { dia: DIA_DO_CAIXA }
  servidorDoCaixa(estado)
  const fechamentos = []
  await comTela(aba({ fecharCaixa: (...a) => { fechamentos.push(a); return Promise.resolve() } }), async (tela) => {
    await esperar()
    estado.semRede = true
    clicar(tela, 'Fechar o caixa')
    await esperar()
    assert.match(textoDe(tela), /Não deu para conferir o caixa agora/)
    assert.equal(descendentes(tela.recipiente).some((no) => no.localName === 'label' && no.textContent === CAMPO_CONTADO), false)
    assert.deepEqual(fechamentos, [])
  })
})

await confere('04. estorno da Barra do pedido: "Valor a devolver" nasce com o pago, lê "58" como R$ 58,00 e manda 58', async () => {
  const estornos = []
  const pedido = {
    estado: 'pago', itens: [], pagamentos: [], meio: 'pix', numero: '1',
    cobranca: { id: 'cb', meio: 'pix', pagaEm: 1, valor: 100, valorPago: 100, criadaEm: 1, expiraEm: 2 },
  }
  const barra = h(ContextoAcessoModulos.Provider, { value: { acoes: {} } },
    h(ContextoEstado.Provider, { value: { agora: AGORA, fonteApi: false, conversas: [], selecionada: null } },
      h(ContextoCatalogo.Provider, { value: { entregadores: [], cardapio: [], meiosDePagamento: [] } },
        h(ContextoAcoes.Provider, { value: {} }, h(BarraProximoPasso, {
          pedido, conversaId: 'c1', nomeCliente: 'Ana', agora: AGORA, emDesfazer: false, aoAvancar() {}, aoDesfazer() {},
          aoMarcarEstorno: (...a) => estornos.push(a),
        })))))
  await comTela(barra, async (tela) => {
    clicar(tela, 'Marcar estorno')
    assert.equal(valorDe(tela, 'Valor a devolver'), '100,00')
    digitar(tela, 'Valor a devolver', '58')
    assert.equal(valorDe(tela, 'Valor a devolver'), '58')
    digitar(tela, 'Motivo do estorno', 'Cliente desistiu')
    const formulario = descendentes(tela.recipiente).find((no) => no.localName === 'form')
    flushSync(() => propsDe(formulario).onSubmit({ preventDefault() {} }))
    await esperar()
    assert.deepEqual(estornos, [['Cliente desistiu', 58]])
  })
})

console.log(`\n${passou} passaram, ${falhas.length} falharam.`)
if (falhas.length > 0) {
  console.log('Falhas:\n  - ' + falhas.join('\n  - '))
  process.exitCode = 1
}
