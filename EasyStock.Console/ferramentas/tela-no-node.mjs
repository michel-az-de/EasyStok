// Apoio das provas que montam telas do console no node, sem navegador e sem pacote novo (#2070).
// O mesmo desenho da prova-1577 e da prova-2005: um DOM mínimo escrito aqui, o JSX pelo transformador do
// Vite, CSS de módulo virando o nome da classe e a fonte de dados no modo API. Interagir é chamar o
// `onChange`/`onClick`/`onSubmit` que o React guardou no elemento: é o código da tela rodando.
// Importe este módulo antes de qualquer arquivo de `src/`.

import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
import { readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { transformWithOxc } from 'vite'

// Sem os avisos de desenvolvimento do React (ele pediria `act`); o que se confere é o resultado.
process.env.NODE_ENV = 'production'

// --- DOM mínimo -------------------------------------------------------------------------------
const HTML = 'http://www.w3.org/1999/xhtml'
export const descendentes = (no) => no.childNodes.flatMap((f) => [f, ...descendentes(f)])

// Seletores que as telas usam: `tag`, `[atributo="valor"]` e `:not(:disabled)`, separados por vírgula.
function casa(no, seletor) {
  const partes = /^([a-z0-9-]*)((?:\[[^\]]+\])*)((?::not\(:disabled\))?)$/i.exec(seletor.trim())
  if (!partes) throw new Error(`seletor que o DOM mínimo não entende: ${seletor}`)
  const [, tag, atributos, habilitado] = partes
  if (no.nodeType !== 1 || (tag && no.localName !== tag.toLowerCase())) return false
  for (const [, nome, valor] of atributos.matchAll(/\[([^=\]]+)(?:="([^"]*)")?\]/g)) {
    if (valor === undefined ? !no.hasAttribute(nome) : no.getAttribute(nome) !== valor) return false
  }
  return !habilitado || !(no.disabled === true || no.hasAttribute('disabled'))
}

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
  replaceChildren(...novos) {
    this.childNodes.forEach((f) => { f.parentNode = null })
    this.childNodes = []
    novos.forEach((n) => this.appendChild(n))
  }
  contains(outro) { return outro === this || this.childNodes.some((f) => f.contains(outro)) }
  querySelectorAll(seletor) { return descendentes(this).filter((no) => seletor.split(',').some((s) => casa(no, s))) }
  querySelector(seletor) { return this.querySelectorAll(seletor)[0] ?? null }
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
class Selecao extends Elemento {
  get options() { return this.childNodes.filter((no) => no.localName === 'option') }
}
class Opcao extends Elemento {
  get value() { return this.getAttribute('value') ?? this.textContent }
}
const criar = (tag, ns) => (tag === 'select' ? new Selecao(tag, ns) : tag === 'option' ? new Opcao(tag, ns) : new Elemento(tag, ns))
const ouvintesDoDocumento = new EventTarget()
export const documento = Object.assign(new No(9, '#document'), {
  createElement: (tag) => criar(tag),
  createElementNS: (ns, tag) => criar(tag, ns),
  createTextNode: (texto) => new Texto(texto),
  getElementById: () => null,
  visibilityState: 'visible',
  title: '',
  addEventListener: (...a) => ouvintesDoDocumento.addEventListener(...a),
  removeEventListener: (...a) => ouvintesDoDocumento.removeEventListener(...a),
})
documento.documentElement = documento.appendChild(new Elemento('html'))
documento.head = documento.documentElement.appendChild(new Elemento('head'))
documento.body = documento.documentElement.appendChild(new Elemento('body'))
documento.activeElement = documento.body

export const guardado = new Map()
const armazenamento = {
  getItem: (chave) => (guardado.has(chave) ? guardado.get(chave) : null),
  setItem: (chave, valor) => { guardado.set(chave, String(valor)) },
  removeItem: (chave) => { guardado.delete(chave) },
  key: (i) => [...guardado.keys()][i] ?? null,
  get length() { return guardado.size },
}

// O endereço da aba: trocar a hash avisa `hashchange` (como o navegador); `replaceState` não avisa.
export const janela = new EventTarget()
let hashAtual = ''
const trocarHash = (nova, avisar) => {
  const valor = !nova || nova === '#' ? '' : nova.startsWith('#') ? nova : `#${nova}`
  if (valor === hashAtual) return
  hashAtual = valor
  if (avisar) queueMicrotask(() => janela.dispatchEvent(new Event('hashchange')))
}
const endereco = {
  origin: 'https://console.prova', pathname: '/', search: '',
  get hash() { return hashAtual },
  set hash(nova) { trocarHash(nova, true) },
  get href() { return `${this.origin}${this.pathname}${hashAtual}` },
}

// O Google Identity Services falso: guarda o `initialize` e desenha um botão; `escolherConta` é o toque nele.
export const google = {
  iniciados: [],
  desenhados: [],
  accounts: {
    id: {
      initialize: (config) => { google.iniciados.push(config) },
      renderButton: (alvo, opcoes) => {
        google.desenhados.push({ alvo, opcoes })
        alvo.appendChild(Object.assign(criar('div'), { textContent: 'Fazer login com o Google' }))
      },
      prompt: () => {},
      disableAutoSelect: () => {},
    },
  },
  escolherConta: (credencial) => google.iniciados.at(-1).callback({ credential: credencial }),
}

Object.assign(janela, {
  document: documento,
  location: endereco,
  history: { state: null, replaceState(estado, _titulo, url) { this.state = estado; if (String(url).startsWith('#')) trocarHash(String(url), false) } },
  HTMLIFrameElement: class {},
  matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }),
  sessionStorage: armazenamento,
  localStorage: armazenamento,
  isSecureContext: true,
  google,
})
documento.defaultView = janela
for (const [nome, valor] of Object.entries({ window: janela, document: documento, sessionStorage: armazenamento, localStorage: armazenamento })) {
  Object.defineProperty(globalThis, nome, { value: valor, configurable: true, writable: true })
}

// --- Carga dos módulos do console no node -----------------------------------------------------
const raiz = fileURLToPath(new URL('../src', import.meta.url))
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter((n) => n.endsWith('.jsx')).map(async (nome) => {
  const arquivo = join(raiz, nome)
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env.DEV', 'false').replaceAll('import.meta.env', '({ DEV: false })')
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
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API = true; export const API_BASE = ""', shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})

// --- Resposta da API no envelope da EasyStock.Api ---------------------------------------------
export const resposta = (status, corpo = null) => ({
  ok: status >= 200 && status < 300, status, text: async () => (corpo === null ? '' : JSON.stringify(corpo)),
})

// --- Ferramentas de tela ----------------------------------------------------------------------
const { createRoot } = await import('react-dom/client')
const { flushSync } = await import('react-dom')

const umInstante = () => new Promise((resolver) => { setImmediate(resolver) })
export const esperar = async () => { for (let i = 0; i < 8; i += 1) await umInstante() }

export const montar = (elemento) => {
  const recipiente = documento.createElement('div')
  documento.body.appendChild(recipiente)
  const raizReact = createRoot(recipiente)
  flushSync(() => raizReact.render(elemento))
  return { recipiente, desmontar: () => { flushSync(() => raizReact.unmount()); documento.body.removeChild(recipiente) } }
}
export const comTela = async (elemento, fn) => {
  const tela = montar(elemento)
  try { return await fn(tela) } finally { tela.desmontar() }
}
export const propsDe = (no) => no[Object.keys(no).find((k) => k.startsWith('__reactProps$'))] ?? {}
export const textoDe = (tela) => tela.recipiente.textContent.replaceAll(' ', ' ')
export const elementos = (tela, filtro) => descendentes(tela.recipiente).filter((no) => no.nodeType === 1 && filtro(no))

export const campoDe = (tela, rotulo) => {
  const etiqueta = elementos(tela, (no) => no.localName === 'label' && no.textContent === rotulo)[0]
  assert.ok(etiqueta, `campo "${rotulo}" não está na tela`)
  return elementos(tela, (no) => no.getAttribute('id') === etiqueta.getAttribute('for'))[0]
}
export const temCampo = (tela, rotulo) => elementos(tela, (no) => no.localName === 'label' && no.textContent === rotulo).length > 0
export const digitar = (tela, rotulo, texto) => { flushSync(() => propsDe(campoDe(tela, rotulo)).onChange({ target: { value: texto } })) }
export const escolher = digitar
// A caixa de marcar fica dentro do rótulo (sem `for`).
export const caixaDe = (tela, texto) => {
  const etiqueta = elementos(tela, (no) => no.localName === 'label' && no.textContent.trim() === texto)[0]
  return etiqueta ? descendentes(etiqueta).find((no) => no.localName === 'input') : null
}
export const marcar = (tela, texto, marcada = true) => {
  const caixa = caixaDe(tela, texto)
  assert.ok(caixa, `caixa "${texto}" não está na tela`)
  flushSync(() => propsDe(caixa).onChange({ target: { checked: marcada } }))
}
export const botoesDe = (tela) => elementos(tela, (no) => no.localName === 'button').map((b) => b.textContent.trim())
export const botaoDe = (tela, texto) => {
  const botao = elementos(tela, (no) => no.localName === 'button' && no.textContent.trim() === texto)[0]
  assert.ok(botao, `botão "${texto}" não está na tela`)
  return botao
}
export const clicar = (tela, texto) => { flushSync(() => propsDe(botaoDe(tela, texto)).onClick({ preventDefault() {} })) }
export const enviar = (tela) => {
  const formulario = elementos(tela, (no) => no.localName === 'form')[0]
  assert.ok(formulario, 'não há formulário na tela')
  flushSync(() => propsDe(formulario).onSubmit({ preventDefault() {} }))
}
