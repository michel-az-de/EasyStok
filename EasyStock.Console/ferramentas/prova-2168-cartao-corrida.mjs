/* eslint-disable no-console */
// Prova da #2168: o cartão da corrida da Lalamove montado de verdade no react-dom, com relógio falso e a API
// de corridas falsa (nenhuma chamada sai daqui). Confere o que a banca achou e o redesenho promete:
//   1. "Corrigir" só na corrida manual; corrida da API encerrada vira uma linha com Selo;
//   2. a cotação conta o tempo e, vencida, risca o preço e troca "Chamar" por "Cotar de novo";
//   3. a chamada leva só o id da cotação; se falha, a lista é relida e a mensagem do servidor aparece inteira;
//   4. cancelar, dar gorjeta e liberar a viagem pedem confirmação antes de chamar a API;
//   5. a gorjeta é total: só os valores acima da atual ficam disponíveis;
//   6. o polling de 15 s só roda com corrida em curso e a aba visível, e para ao desmontar (contagem exata);
//   7. leitura que falha oferece "Tentar de novo"; link de rastreio que não é https não vira link;
//   8. pedido que não está pronto não oferece a Lalamove.
//
//   node ferramentas/prova-2168-cartao-corrida.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { mock } from 'node:test'
import { transformWithOxc } from 'vite'

// Sem os avisos de desenvolvimento do React (ele pediria `act`); o que se confere é o resultado.
process.env.NODE_ENV = 'production'

// --- DOM mínimo (o mesmo desenho da prova-1577-balcao-render) -----------------------------------
const HTML = 'http://www.w3.org/1999/xhtml'
const ouvintes = new Map()
const ouvir = (alvo) => ({
  addEventListener(tipo, fn) { ouvintes.set(`${alvo}:${tipo}`, [...(ouvintes.get(`${alvo}:${tipo}`) ?? []), fn]) },
  removeEventListener(tipo, fn) { ouvintes.set(`${alvo}:${tipo}`, (ouvintes.get(`${alvo}:${tipo}`) ?? []).filter((f) => f !== fn)) },
})
const disparar = (alvo, tipo) => (ouvintes.get(`${alvo}:${tipo}`) ?? []).forEach((fn) => fn({ type: tipo }))

class No {
  constructor(nodeType, nodeName) { this.nodeType = nodeType; this.nodeName = nodeName; this.childNodes = []; this.parentNode = null; this.nodeValue = null }
  get ownerDocument() { return documento }
  get firstChild() { return this.childNodes[0] ?? null }
  get lastChild() { return this.childNodes.at(-1) ?? null }
  get nextSibling() { const irmaos = this.parentNode?.childNodes ?? []; return irmaos[irmaos.indexOf(this) + 1] ?? null }
  appendChild(filho) { return this.insertBefore(filho, null) }
  insertBefore(filho, antes) {
    filho.parentNode?.removeChild(filho)
    const i = antes ? this.childNodes.indexOf(antes) : -1
    if (i < 0) this.childNodes.push(filho)
    else this.childNodes.splice(i, 0, filho)
    filho.parentNode = this
    return filho
  }
  removeChild(filho) { const i = this.childNodes.indexOf(filho); if (i >= 0) this.childNodes.splice(i, 1); filho.parentNode = null; return filho }
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
class Texto extends No { constructor(texto) { super(3, '#text'); this.nodeValue = String(texto) } }
class Elemento extends No {
  constructor(tag, ns = HTML) {
    super(1, ns === HTML ? tag.toUpperCase() : tag)
    this.tagName = this.nodeName; this.localName = tag; this.namespaceURI = ns; this.atributos = new Map()
    this.style = { setProperty(nome, valor) { this[nome] = valor }, removeProperty(nome) { delete this[nome] } }
  }
  setAttribute(nome, valor) { this.atributos.set(nome, String(valor)) }
  setAttributeNS(_ns, nome, valor) { this.atributos.set(nome, String(valor)) }
  removeAttribute(nome) { this.atributos.delete(nome) }
  removeAttributeNS(_ns, nome) { this.atributos.delete(nome) }
  getAttribute(nome) { return this.atributos.get(nome) ?? null }
  hasAttribute(nome) { return this.atributos.has(nome) }
  closest() { return null }
  querySelectorAll() { return [] }
  focus() { documento.activeElement = this }
  blur() { documento.activeElement = documento.body }
  scrollIntoView() {}
}
const documento = Object.assign(new No(9, '#document'), {
  createElement: (tag) => new Elemento(tag), createElementNS: (ns, tag) => new Elemento(tag, ns),
  createTextNode: (texto) => new Texto(texto), querySelector: () => null, getElementById: () => null,
  visibilityState: 'visible', ...ouvir('document'),
})
documento.documentElement = documento.appendChild(new Elemento('html'))
documento.body = documento.documentElement.appendChild(new Elemento('body'))
documento.activeElement = documento.body
const janela = { document: documento, location: { origin: 'https://console.prova', pathname: '/', hash: '', href: 'https://console.prova/' },
  HTMLIFrameElement: class {}, matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }), ...ouvir('window') }
documento.defaultView = janela
for (const [nome, valor] of Object.entries({ window: janela, document: documento })) {
  Object.defineProperty(globalThis, nome, { value: valor, configurable: true, writable: true })
}

// --- Carga dos módulos do console no node: a API de corridas é a falsa desta prova --------------
const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const arquivosDe = (dir) => readdirSync(dir, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? arquivosDe(join(dir, e.name)) : [join(dir, e.name)]))
const jsx = new Map()
await Promise.all(arquivosDe(raiz).filter((a) => a.endsWith('.jsx')).map(async (arquivo) => {
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env.DEV', 'false')
  jsx.set(pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code)
}))
const ROTAS = ['listarCorridas', 'registrarCorrida', 'corrigirCorrida', 'conexaoDespacho', 'cotarCorrida', 'chamarCorrida',
  'atualizarCorrida', 'cancelarCorrida', 'liberarCorrida', 'gorjetaCorrida']
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      for (const extensao of ['.js', '.jsx']) { try { return proximo(especificador + extensao, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', shortCircuit: true, source: jsx.get(url) }
    if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default new Proxy({}, { get: (_, p) => String(p) })' }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    if (url.endsWith('/infra/api/entregasApi.js')) {
      return { format: 'module', shortCircuit: true, source: ROTAS.map((r) => `export const ${r} = (...a) => globalThis.__api2168.chamar('${r}', a)`).join('\n') }
    }
    return proximo(url, contexto)
  },
})

const { createElement: h } = await import('react')
const { createRoot } = await import('react-dom/client')
const { flushSync } = await import('react-dom')
const { CorridasDaViagem } = await import('../src/features/entregas/CorridasDaViagem.jsx')

// --- API falsa ------------------------------------------------------------------------------------
const AGORA = Date.parse('2026-10-31T21:00:00Z')
const erroDaApi = (status, mensagem) => Object.assign(new Error(mensagem), { status })
function novaApi({ corridas = [], conectada = true, respostas = {} } = {}) {
  const api = {
    corridas, chamadas: [],
    conta: (rota) => api.chamadas.filter((c) => c.rota === rota).length,
    async chamar(rota, args) {
      api.chamadas.push({ rota, args })
      const resposta = respostas[rota]
      if (typeof resposta === 'function') return resposta(api, args)
      if (rota === 'listarCorridas') return structuredClone(api.corridas)
      if (rota === 'conexaoDespacho') return { lalamoveConectada: conectada }
      return resposta ?? {}
    },
  }
  globalThis.__api2168 = api
  return api
}
const corridaApi = (status, extra = {}) => ({
  id: 'c1', origem: 'Api', provedor: 'Lalamove', status, veiculo: 'Moto', custoCentavos: 1890, custoCotadoCentavos: 1890,
  ativa: !['Entregue', 'Cancelada', 'Rejeitada', 'Expirada', 'Falha'].includes(status), podeCancelar: false, gorjetaCentavos: 0,
  linkRastreio: 'https://share.lalamove.com/?BR1', ...extra,
})
const viagem = (extra = {}) => ({ id: 'v1', situacao: 'Montando', paradas: [{ pedidoId: 'p1', endereco: 'Rua dos Pinheiros, 100' }], ...extra })

// --- Montagem e utilidades ----------------------------------------------------------------------
const assentar = async () => { for (let i = 0; i < 6; i += 1) await new Promise((r) => setImmediate(r)) }
const propsDe = (no) => no[Object.keys(no).find((chave) => chave.startsWith('__reactProps$'))]
const achar = (no, teste) => (teste(no) ? no : no.childNodes.reduce((achado, filho) => achado ?? achar(filho, teste), null))
const todos = (no, teste) => [...(teste(no) ? [no] : []), ...no.childNodes.flatMap((filho) => todos(filho, teste))]

async function montar(props) {
  mock.timers.enable({ apis: ['setInterval', 'Date'], now: AGORA })
  const recipiente = documento.body.appendChild(new Elemento('div'))
  const erros = []
  const raizReact = createRoot(recipiente, { onUncaughtError: (e) => erros.push(e), onCaughtError: (e) => erros.push(e) })
  flushSync(() => raizReact.render(h(CorridasDaViagem, { viagem: viagem(), entregadores: [], ocupado: false, ...props })))
  await assentar()
  const tela = {
    montada: true,
    texto: () => recipiente.textContent,
    botoes: (rotulo) => todos(recipiente, (n) => n.localName === 'button' && n.textContent.includes(rotulo)),
    botao(rotulo) {
      const achados = tela.botoes(rotulo)
      assert.ok(achados.length > 0, `botão "${rotulo}" na tela`)
      return achados.at(-1)
    },
    async clicar(rotulo) {
      const no = tela.botao(rotulo)
      assert.notEqual(propsDe(no).disabled, true, `"${rotulo}" habilitado`)
      flushSync(() => propsDe(no).onClick({ preventDefault() {}, currentTarget: no, target: no }))
      await assentar()
    },
    async andar(ms) { flushSync(() => mock.timers.tick(ms)); await assentar() },
    links: () => todos(recipiente, (n) => n.localName === 'a').map((a) => propsDe(a).href),
    // `manterRelogio`: o relógio falso segue ligado para medir se algum intervalo sobreviveu à desmontagem.
    desmontar({ manterRelogio = false } = {}) {
      tela.montada = false
      flushSync(() => raizReact.unmount())
      documento.body.removeChild(recipiente)
      if (!manterRelogio) mock.timers.reset()
      assert.deepEqual(erros.map((e) => e.message), [], 'a tela não lançou erro')
    },
  }
  return tela
}

let passou = 0
const falhas = []
async function confere(nome, props, fn) {
  const tela = await montar(props)
  try {
    await fn(tela)
    passou += 1
    console.log(`ok    ${nome}`)
  } catch (erro) {
    falhas.push(nome)
    console.log(`FALHA ${nome}\n      ${erro.message.split('\n')[0]}`)
  } finally {
    if (tela.montada) tela.desmontar()
  }
}

// --- Cenários ------------------------------------------------------------------------------------
let api = novaApi({ corridas: [{ id: 'm1', origem: 'Manual', provedor: 'NoveNove', custoCentavos: 2200 }, corridaApi('Cancelada', { custoCentavos: 0 })] })
await confere('1. "Corrigir" só na corrida manual; a da API encerrada vira linha com Selo', {}, async (t) => {
  assert.equal(t.botoes('Corrigir').length, 1)
  assert.match(t.texto(), /Cancelada/)
  assert.doesNotMatch(t.texto(), /Atualizar agora/, 'corrida encerrada não tem rodapé de atualização')
})

// A cotação vale 5 min a partir de agora (o relógio falso anda; "Cotar de novo" traz uma cotação nova).
api = novaApi({ respostas: { cotarCorrida: (_, [, veiculo]) => ({ cotacaoId: 'q1', veiculo, valorCentavos: 1890, distanciaMetros: 3240,
  expiraEm: new Date(Date.now() + 300_000).toISOString() }) } })
await confere('2. cotação conta o tempo e, vencida, troca "Chamar" por "Cotar de novo"', {}, async (t) => {
  await t.clicar('Carro')
  await t.clicar('Ver preço')
  assert.deepEqual(api.chamadas.find((c) => c.rota === 'cotarCorrida').args, ['v1', 'Carro'])
  assert.match(t.texto(), /Vale por 5:00/)
  assert.match(t.texto(), /3,2 km · de carro/)
  assert.equal(t.botoes('Chamar por').length, 1)
  await t.andar(299_000)
  assert.match(t.texto(), /Vale por 0:01/)
  await t.andar(2_000)
  assert.match(t.texto(), /Cotação vencida/)
  assert.equal(t.botoes('Chamar por').length, 0, 'cotação vencida não chama')
  await t.clicar('Cotar de novo')
  assert.equal(api.conta('cotarCorrida'), 2)
  assert.equal(t.botoes('Chamar por').length, 1)
})

const SEM_CONFIRMACAO = 'A Lalamove não confirmou a corrida. Não chame de novo: confira no painel da Lalamove se ela foi criada.'
api = novaApi({ respostas: {
  cotarCorrida: { cotacaoId: 'q1', valorCentavos: 1890, expiraEm: new Date(AGORA + 300_000).toISOString() },
  chamarCorrida: (a) => { a.corridas = [corridaApi('Solicitando', { linkRastreio: null })]; throw erroDaApi(400, SEM_CONFIRMACAO) },
} })
await confere('3. chamada leva só a cotação; se falha, relê a lista e mostra o servidor sem contradizer', {}, async (t) => {
  await t.clicar('Ver preço')
  const leiturasAntes = api.conta('listarCorridas')
  await t.clicar('Chamar por')
  assert.deepEqual(api.chamadas.find((c) => c.rota === 'chamarCorrida').args, ['v1', { cotacaoId: 'q1', veiculo: 'Moto', observacao: null }])
  assert.equal(api.conta('listarCorridas'), leiturasAntes + 1, 'a corrida pode ter sido criada: a lista é relida')
  assert.ok(t.texto().includes(SEM_CONFIRMACAO))
  assert.doesNotMatch(t.texto(), /não foi chamada/, 'sem prefixo que contradiz o servidor')
  assert.match(t.texto(), /Confirmando com a Lalamove/)
  assert.equal(t.botoes('Ver preço').length, 0, 'com corrida em curso não se cota outra')
})

api = novaApi({ corridas: [corridaApi('MotoristaACaminho', { podeCancelar: true, motorista: 'Carlos Souza', motoristaTelefone: '+5511977776666',
  motoristaPlaca: 'FXT2B47', cancelaSemCustoAte: new Date(AGORA + 180_000).toISOString() })],
respostas: { cancelarCorrida: (a) => { a.corridas = [corridaApi('Cancelada', { custoCentavos: 0 })]; return {} } } })
await confere('4a. cancelar pede confirmação antes de chamar a API', {}, async (t) => {
  assert.match(t.texto(), /Cancela sem custo até 18:03/)
  assert.ok(t.links().includes('tel:+5511977776666'))
  await t.clicar('Cancelar corrida')
  assert.equal(api.conta('cancelarCorrida'), 0, 'o primeiro toque só pergunta')
  assert.match(t.texto(), /Cancelar a corrida\? A Lalamove avisa o motorista/)
  await t.clicar('Cancelar corrida')
  assert.deepEqual(api.chamadas.filter((c) => c.rota === 'cancelarCorrida').map((c) => c.args), [['v1', 'c1']])
  assert.match(t.texto(), /Cancelada/)
})

api = novaApi({ corridas: [corridaApi('ProcurandoMotorista', { podeCancelar: true, gorjetaCentavos: 500, custoCentavos: 2390 })] })
await confere('4b/5. gorjeta é total: só acima da atual, e pede confirmação', {}, async (t) => {
  const disponivel = (valor) => propsDe(t.botao(valor)).disabled !== true
  assert.deepEqual(['3,00', '5,00', '10,00'].map(disponivel), [false, false, true])
  assert.match(t.texto(), /R\$\s5,00 acima da cotação/)
  await t.clicar('10,00')
  assert.equal(api.conta('gorjetaCorrida'), 0)
  assert.match(t.texto(), /É o total, não um acréscimo/)
  await t.clicar('de gorjeta')
  assert.deepEqual(api.chamadas.filter((c) => c.rota === 'gorjetaCorrida').map((c) => c.args), [['v1', 'c1', 1000]])
})

api = novaApi({ corridas: [corridaApi('Solicitando', { linkRastreio: null, conferirNoProvedor: true })],
  respostas: { liberarCorrida: (a) => { a.corridas = [corridaApi('Falha', { custoCentavos: 0 })]; return {} } } })
await confere('4c. sem confirmação da Lalamove: a atendente confere e libera, com confirmação', {}, async (t) => {
  assert.match(t.texto(), /Sem confirmação da Lalamove/)
  assert.doesNotMatch(t.texto(), /Confirmando com a Lalamove/)
  await t.clicar('Conferi no painel')
  assert.equal(api.conta('liberarCorrida'), 0)
  await t.clicar('liberar a viagem')
  assert.deepEqual(api.chamadas.filter((c) => c.rota === 'liberarCorrida').map((c) => c.args), [['v1', 'c1']])
  assert.match(t.texto(), /Não foi criada/)
})

api = novaApi({ corridas: [corridaApi('ProcurandoMotorista')] })
await confere('6. polling de 15 s só com corrida em curso e aba visível; para ao desmontar', {}, async (t) => {
  await t.andar(14_999)
  assert.equal(api.conta('atualizarCorrida'), 0)
  await t.andar(1)
  assert.equal(api.conta('atualizarCorrida'), 1)
  documento.visibilityState = 'hidden'
  await t.andar(30_000)
  assert.equal(api.conta('atualizarCorrida'), 1, 'aba oculta não consulta a Lalamove')
  documento.visibilityState = 'visible'
  disparar('document', 'visibilitychange')
  await assentar()
  assert.equal(api.conta('atualizarCorrida'), 2, 'a volta da aba atualiza na hora')
  t.desmontar({ manterRelogio: true })
  const depois = api.chamadas.length
  mock.timers.tick(60_000)
  await assentar()
  mock.timers.reset()
  assert.equal(api.chamadas.length, depois, 'desmontada, a tela não consulta mais nada')
  assert.equal((ouvintes.get('document:visibilitychange') ?? []).length, 0, 'o ouvinte da aba saiu junto')
})

api = novaApi({ corridas: [corridaApi('Entregue')] })
await confere('6b. corrida encerrada não liga o polling', {}, async (t) => {
  await t.andar(60_000)
  assert.equal(api.conta('atualizarCorrida'), 0)
})

let primeira = true
api = novaApi({ respostas: { listarCorridas: (a) => { if (primeira) { primeira = false; throw erroDaApi(503, 'Sem conexão') } return [] } } })
await confere('7a. leitura que falha oferece "Tentar de novo"', {}, async (t) => {
  assert.match(t.texto(), /As corridas não carregaram/)
  await t.clicar('Tentar de novo')
  assert.equal(api.conta('listarCorridas'), 2)
  assert.doesNotMatch(t.texto(), /não carregaram/)
  assert.equal(t.botoes('Ver preço').length, 1)
})

api = novaApi({ corridas: [corridaApi('ProcurandoMotorista', { linkRastreio: 'javascript:alert(1)' })] })
await confere('7b. link de rastreio que não é https não vira link', {}, async (t) => {
  assert.deepEqual(t.links().filter((href) => /alert/.test(href ?? '')), [])
  assert.doesNotMatch(t.texto(), /Acompanhar no mapa/)
})

api = novaApi()
await confere('8. pedido que não está pronto não oferece a Lalamove', { pedido: { id: 'p1', status: 'preparando' } }, async (t) => {
  assert.equal(t.botoes('Ver preço').length, 0)
  assert.match(t.texto(), /ainda não está pronto/)
})

console.log(`\n${passou} verificações passaram.`)
assert.deepEqual(falhas, [], `falharam: ${falhas.join('; ')}`)
