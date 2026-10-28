/* eslint-disable no-console */
// Prova da #1577 (segunda parte): o Balcão não refaz render à toa.
//   A. rascunho: a tecla fica no campo de escrever; o estado global recebe o texto na pausa de
//      400 ms, ou na hora em blur, envio, troca de conversa, saída da tela e aba oculta;
//   B. cartão da lista e balão da conversa em `memo`: muda uma conversa, re-renderiza um cartão;
//      chega mensagem, os balões antigos ficam como estão.
// As conferências de tela montam os componentes de verdade no react-dom, sobre um DOM mínimo
// escrito aqui (sem pacote novo). Cursor e teclado do aparelho continuam sendo coisa de navegador.
//
//   node ferramentas/prova-1577-balcao-render.mjs

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
const ouvintes = new Map()
const ouvir = (alvo) => ({
  addEventListener(tipo, fn) { ouvintes.set(`${alvo}:${tipo}`, [...(ouvintes.get(`${alvo}:${tipo}`) ?? []), fn]) },
  removeEventListener(tipo, fn) { ouvintes.set(`${alvo}:${tipo}`, (ouvintes.get(`${alvo}:${tipo}`) ?? []).filter((f) => f !== fn)) },
})
const disparar = (alvo, tipo) => (ouvintes.get(`${alvo}:${tipo}`) ?? []).forEach((fn) => fn({ type: tipo }))

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
const criar = (tag, ns) => (tag === 'textarea' ? new AreaDeTexto(tag) : new Elemento(tag, ns))
const documento = Object.assign(new No(9, '#document'), {
  createElement: (tag) => criar(tag),
  createElementNS: (ns, tag) => criar(tag, ns),
  createTextNode: (texto) => new Texto(texto),
  querySelector: () => null,
  getElementById: () => null,
  visibilityState: 'visible',
  ...ouvir('document'),
})
documento.documentElement = documento.appendChild(new Elemento('html'))
documento.body = documento.documentElement.appendChild(new Elemento('body'))
documento.activeElement = documento.body
const guardado = new Map()
const janela = {
  document: documento,
  // `?massa=0`: a semente curta de 5 conversas da demonstração.
  location: { origin: 'https://console.prova', pathname: '/', hash: '', href: 'https://console.prova/?massa=0' },
  HTMLIFrameElement: class {},
  matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }),
  sessionStorage: {
    getItem: (chave) => guardado.get(chave) ?? null,
    setItem: (chave, valor) => { guardado.set(chave, String(valor)) },
    removeItem: (chave) => { guardado.delete(chave) },
  },
  ...ouvir('window'),
}
documento.defaultView = janela
for (const [nome, valor] of Object.entries({ window: janela, document: documento, sessionStorage: janela.sessionStorage })) {
  Object.defineProperty(globalThis, nome, { value: valor, configurable: true, writable: true })
}

// --- Carga dos módulos do console no node -----------------------------------------------------
const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const arquivosDe = (dir) => readdirSync(dir, { withFileTypes: true })
  .flatMap((e) => (e.isDirectory() ? arquivosDe(join(dir, e.name)) : [join(dir, e.name)]))
const jsx = new Map()
await Promise.all(arquivosDe(raiz).filter((a) => a.endsWith('.jsx')).map(async (arquivo) => {
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env.DEV', 'false')
  jsx.set(pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code)
}))

// Espiões de render: a função que cada componente chama uma vez por render passa a anotar a chamada.
// O cartão chama `previaDaConversa(conversa)`; o balão de texto, `partesComLinkDoCardapio(texto)` (a regex de link).
const renders = { cartoes: [], baloes: [], vizinho: 0 }
globalThis.__prova1577 = renders
const ESPIOES = [
  { de: '/features/caixa-de-entrada/CartaoConversa.jsx', modulo: '../../dominio/conversa', funcao: 'previaDaConversa', anota: 'cartoes.push(a[0].id)' },
  { de: '/features/atendimento/Balao.jsx', modulo: '../../dominio/cardapioLink', funcao: 'partesComLinkDoCardapio', anota: 'baloes.push(a[0])' },
]

registerHooks({
  resolve(especificador, contexto, proximo) {
    const espiao = ESPIOES.find((e) => contexto.parentURL?.endsWith(e.de) && especificador === e.modulo)
    if (espiao) return { url: `${proximo(especificador + '.js', contexto).url}?espiao=${espiao.funcao}`, shortCircuit: true }
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      for (const extensao of ['.js', '.jsx']) {
        try { return proximo(especificador + extensao, contexto) } catch { /* próximo formato */ }
      }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    const espiao = ESPIOES.find((e) => url.endsWith(`?espiao=${e.funcao}`))
    if (espiao) {
      const real = JSON.stringify(url.split('?')[0])
      return {
        format: 'module', shortCircuit: true,
        source: `export * from ${real}\nimport { ${espiao.funcao} as real } from ${real}\n`
          + `export const ${espiao.funcao} = (...a) => { globalThis.__prova1577.${espiao.anota}; return real(...a) }\n`,
      }
    }
    if (jsx.has(url)) return { format: 'module', shortCircuit: true, source: jsx.get(url) }
    if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default new Proxy({}, { get: (_, p) => String(p) })' }
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = false\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

const { createElement: h, useLayoutEffect, useMemo, useReducer, useRef, useState } = await import('react')
const { createRoot } = await import('react-dom/client')
const { flushSync } = await import('react-dom')
const { mock } = await import('node:test')
const acao = await import('../src/aplicacao/acoes.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const { criarAcoes } = await import('../src/aplicacao/criarAcoes.js')
const { ContextoAcoes, ContextoCatalogo, ContextoEstado, useAtendimento } = await import('../src/aplicacao/contextos.js')
const { carregarConversas } = await import('../src/infra/repositorioConversas.js')
const { carregarCatalogo } = await import('../src/infra/repositorioCatalogo.js')
const { INSTANTE_INICIAL } = await import('../src/infra/catalogo.js')
const { conversasDoBalcao, ordenarBalcao } = await import('../src/dominio/conversa.js')
const { gravarRascunhos, lerRascunhos } = await import('../src/infra/api/rascunhosDaSessao.js')
const { PainelAtendimento } = await import('../src/features/atendimento/PainelAtendimento.jsx')
const { PainelConversas } = await import('../src/features/caixa-de-entrada/PainelConversas.jsx')
const { Thread } = await import('../src/features/atendimento/Thread.jsx')
// Módulos novos desta entrega: antes dela não existem, e as conferências que dependem deles falham dizendo isso.
const novo = async (caminho) => import(caminho).catch(() => ({}))
const rascunhoLocal = await novo('../src/aplicacao/rascunhoLocal.js')
const igualdade = await novo('../src/aplicacao/igualdade.js')
const existe = (modulo, nome) => assert.equal(typeof modulo[nome], 'function', `${nome} ainda não existe`)

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

// ================================================================================================
// A. Rascunho: regra pura e o campo fora da tela
// ================================================================================================
const baseDoEstado = () => ({ rascunhos: { 'c-1': 'oi' }, escritasDeFora: {}, outro: 1 })

await confere('A. reducer: o que vem de fora soma o carimbo da conversa; o que vem do campo, não', () => {
  existe(rascunhoLocal, 'comRascunhoDeFora')
  const deFora = rascunhoLocal.comRascunhoDeFora(baseDoEstado(), 'c-1', 'sugestão')
  assert.equal(deFora.rascunhos['c-1'], 'sugestão')
  assert.equal(deFora.escritasDeFora['c-1'], 1)
  assert.equal(deFora.escritasDeFora['c-2'], undefined, 'o carimbo é por conversa')
  const doCampo = rascunhoLocal.comRascunhoDoCampo(deFora, { id: 'c-1', texto: 'sugestão!', escritaVista: 1 })
  assert.equal(doCampo.rascunhos['c-1'], 'sugestão!')
  assert.equal(doCampo.escritasDeFora, deFora.escritasDeFora)
})

await confere('A. reducer: despacho do campo que não viu a última escrita de fora é descartado', () => {
  existe(rascunhoLocal, 'comRascunhoDeFora')
  const deFora = rascunhoLocal.comRascunhoDeFora(baseDoEstado(), 'c-1', 'sugestão')
  assert.equal(rascunhoLocal.comRascunhoDoCampo(deFora, { id: 'c-1', texto: 'oi tud', escritaVista: 0 }), deFora)
})

await confere('A. reducer: despacho do campo com o texto que o global já tem devolve o mesmo estado', () => {
  existe(rascunhoLocal, 'comRascunhoDoCampo')
  const estado = baseDoEstado()
  assert.equal(rascunhoLocal.comRascunhoDoCampo(estado, { id: 'c-1', texto: 'oi', escritaVista: 0 }), estado)
  assert.equal(rascunhoLocal.comRascunhoDoCampo(estado, { id: 'c-9', texto: '', escritaVista: 0 }), estado)
})

await confere('A. reducer de verdade: sugestão soma o carimbo, o campo atrasado perde para ela e o envio limpa somando', () => {
  // #2135: a tela abre sem conversa escolhida; aqui a atendente já abriu a primeira.
  const conversas = carregarConversas()
  const partida = { ...estadoInicial({ conversas, catalogo: carregarCatalogo(), regras: [], modoAgente: 'simulado' }), selecionadaId: conversas[0].id }
  const id = partida.selecionadaId
  assert.deepEqual(partida.escritasDeFora, {})
  const digitou = reducer(partida, { tipo: acao.DEFINIR_RASCUNHO, id, texto: 'vou ver', doCampo: true, escritaVista: 0 })
  assert.equal(digitou.rascunhos[id], 'vou ver')
  assert.deepEqual(digitou.escritasDeFora, {}, 'o campo não soma carimbo')
  const sugeriu = reducer(digitou, { tipo: acao.DEFINIR_RASCUNHO, id, texto: 'Sugestão' })
  assert.equal(sugeriu.escritasDeFora[id], 1)
  const atrasado = reducer(sugeriu, { tipo: acao.DEFINIR_RASCUNHO, id, texto: 'vou ver e', doCampo: true, escritaVista: 0 })
  assert.equal(atrasado, sugeriu, 'o despacho atrasado do campo não desfaz a sugestão')
  const enviou = reducer(sugeriu, { tipo: acao.ENVIAR_MENSAGEM, id, texto: 'Sugestão', mensagemId: 'msg-prova', agora: new Date(INSTANTE_INICIAL).getTime() })
  assert.equal(enviou.rascunhos[id], '')
  assert.equal(enviou.escritasDeFora[id], 2, 'o limpar do envio é escrita de fora: o campo adota o vazio')
})

function campoDeProva(global = { conversaId: 'c-1', rascunho: '', escritaDeFora: 0 }) {
  existe(rascunhoLocal, 'criarRascunhoLocal')
  mock.timers.enable({ apis: ['setTimeout'] })
  const despachos = []
  const campo = rascunhoLocal.criarRascunhoLocal({ ...global, aoDespachar: (despacho, opcoes) => despachos.push({ ...despacho, ...opcoes }) })
  return { campo, despachos, andar: (ms) => mock.timers.tick(ms) }
}
const comCampo = (global, fn) => () => {
  try { return fn(campoDeProva(global)) } finally { mock.timers.reset() }
}

await confere('A. campo: digitação rápida vira um despacho só, 400 ms depois da última tecla', comCampo(undefined, ({ campo, despachos, andar }) => {
  for (const texto of ['b', 'bo', 'bom', 'bom ', 'bom d', 'bom di', 'bom dia']) {
    campo.digitar(texto)
    andar(120)
    assert.equal(campo.texto(), texto, 'a tecla aparece no campo na hora')
  }
  assert.equal(despachos.length, 0, 'nada vai ao estado global enquanto ela digita')
  andar(279)
  assert.equal(despachos.length, 0)
  andar(1)
  assert.deepEqual(despachos, [{ id: 'c-1', texto: 'bom dia', escritaVista: 0 }])
  andar(5000)
  assert.equal(despachos.length, 1, 'parada, não despacha de novo')
}))

await confere('A. campo: blur, envio e texto posto de uma vez descarregam na hora', comCampo(undefined, ({ campo, despachos, andar }) => {
  campo.digitar('a caminho')
  campo.descarregar()
  assert.deepEqual(despachos.map((d) => d.texto), ['a caminho'], 'blur e envio não esperam a pausa')
  andar(400)
  assert.equal(despachos.length, 1, 'o atraso que estava armado não despacha de novo')
  campo.escrever('Seu pedido saiu para entrega.')
  assert.deepEqual(despachos.map((d) => d.texto), ['a caminho', 'Seu pedido saiu para entrega.'], 'resposta pronta e limpar vão na hora')
  campo.descarregar()
  assert.equal(despachos.length, 2, 'sem texto novo, nada a descarregar')
}))

await confere('A. campo: escrita de fora durante a digitação vence e o despacho atrasado não a desfaz', comCampo(undefined, ({ campo, despachos, andar }) => {
  campo.digitar('vou ver')
  andar(200)
  campo.ver({ conversaId: 'c-1', rascunho: 'Sugestão do agente', escritaDeFora: 1 })
  assert.equal(campo.texto(), 'Sugestão do agente')
  andar(1000)
  assert.equal(despachos.length, 0, 'o texto que ela digitava não volta por cima da sugestão')
  campo.digitar('Sugestão do agente!')
  andar(400)
  assert.deepEqual(despachos, [{ id: 'c-1', texto: 'Sugestão do agente!', escritaVista: 1 }])
}))

await confere('A. campo: escrita de fora vence mesmo sem mudar o texto global (limpar o que o global já tinha vazio)', comCampo(undefined, ({ campo, despachos, andar }) => {
  // Ela digitou e ainda não houve pausa: o global segue vazio. Outra mensagem sai pela conversa e limpa o rascunho.
  campo.digitar('abc')
  campo.ver({ conversaId: 'c-1', rascunho: '', escritaDeFora: 1 })
  assert.equal(campo.texto(), '', 'o campo adota pelo carimbo, não pela diferença de texto')
  andar(1000)
  assert.equal(despachos.length, 0)
}))

await confere('A. campo: o eco do próprio despacho não pisa nas letras digitadas depois', comCampo(undefined, ({ campo, despachos, andar }) => {
  campo.digitar('abc')
  andar(400)
  campo.digitar('abcd')
  // O estado global devolve "abc" (o que o campo mesmo mandou), com o mesmo carimbo de fora.
  campo.ver({ conversaId: 'c-1', rascunho: 'abc', escritaDeFora: 0 })
  assert.equal(campo.texto(), 'abcd')
  andar(400)
  assert.deepEqual(despachos.map((d) => d.texto), ['abc', 'abcd'])
}))

await confere('A. campo: troca de conversa descarrega a anterior e não vaza texto para a nova', comCampo(undefined, ({ campo, despachos, andar }) => {
  campo.digitar('recado da Ana')
  campo.ver({ conversaId: 'c-2', rascunho: 'rascunho do Bruno', escritaDeFora: 0 })
  assert.deepEqual(despachos, [{ id: 'c-1', texto: 'recado da Ana', escritaVista: 0 }])
  assert.equal(campo.texto(), 'rascunho do Bruno')
  andar(1000)
  assert.equal(despachos.length, 1, 'o atraso da conversa anterior não despacha na nova')
  campo.digitar('rascunho do Bruno, ok')
  andar(400)
  assert.deepEqual(despachos.at(-1), { id: 'c-2', texto: 'rascunho do Bruno, ok', escritaVista: 0 })
}))

await confere('A. campo: sair da tela ou esconder a aba descarrega na hora e pede a gravação direta', comCampo(undefined, ({ campo, despachos, andar }) => {
  campo.digitar('no meio da fra')
  campo.descarregar({ ja: true })
  assert.deepEqual(despachos, [{ id: 'c-1', texto: 'no meio da fra', escritaVista: 0, ja: true }])
  andar(1000)
  assert.equal(despachos.length, 1)
}))

await confere('A. campo: digitação sem pausa de 400 ms descarrega no teto de 2 s', comCampo(undefined, ({ campo, despachos, andar }) => {
  let texto = ''
  for (let i = 0; i < 25; i += 1) {
    texto += 'a'
    campo.digitar(texto)
    andar(200)
  }
  assert.deepEqual(despachos.map((d) => d.texto.length), [10, 20], 'cinco segundos digitando sem parar e o estado global não recebeu nada a tempo')
  andar(400)
  assert.deepEqual(despachos.map((d) => d.texto.length), [10, 20, 25])
  andar(5000)
  assert.equal(despachos.length, 3, 'parada, o teto não despacha de novo')
}))

await confere('A. campo: a saída da tela grava mesmo o texto que a pausa já despachou', comCampo(undefined, ({ campo, despachos, andar }) => {
  campo.digitar('quase pronto')
  andar(400)
  assert.equal(despachos.length, 1)
  // O despacho da pausa pode não ter rendido quando a tela sai (401 logo depois): a gravação direta vai de novo.
  campo.descarregar({ ja: true })
  assert.deepEqual(despachos[1], { id: 'c-1', texto: 'quase pronto', escritaVista: 0, ja: true })
  campo.descarregar()
  assert.equal(despachos.length, 2, 'sem urgência, texto já despachado não despacha de novo')
}))

await confere('A. campo: a tela troca de arranjo no meio da digitação (girar o tablet) e o campo novo adota o texto', () => {
  existe(rascunhoLocal, 'criarRascunhoLocal')
  mock.timers.enable({ apis: ['setTimeout'] })
  try {
    const id = 'c-1'
    let estado = { rascunhos: {}, escritasDeFora: {} }
    const aoDespachar = (despacho, opcoes) => {
      estado = reducer(estado, { tipo: acao.DEFINIR_RASCUNHO, ...despacho, doCampo: true, adotar: opcoes?.desmontando === true })
    }
    const global = () => ({ conversaId: id, rascunho: estado.rascunhos[id] ?? '', escritaDeFora: estado.escritasDeFora[id] ?? 0 })
    const velho = rascunhoLocal.criarRascunhoLocal({ ...global(), aoDespachar })
    velho.digitar('Oi, seu pedido sai as 19h')
    // O campo novo monta no mesmo commit, com o global ainda sem o texto; só depois o velho desmonta.
    const campoNovo = rascunhoLocal.criarRascunhoLocal({ ...global(), aoDespachar })
    velho.descarregar({ ja: true, desmontando: true })
    campoNovo.ver(global())
    assert.equal(campoNovo.texto(), 'Oi, seu pedido sai as 19h', 'o texto digitado sumiu do campo na remontagem')
    campoNovo.digitar('Oi, seu pedido sai as 19h!')
    mock.timers.tick(400)
    assert.equal(estado.rascunhos[id], 'Oi, seu pedido sai as 19h!')
  } finally { mock.timers.reset() }
})

await confere('A. reducer: só o despacho de desmontagem soma o carimbo, e escrita de fora mais nova ainda vence', () => {
  const base = baseDoEstado()
  const daPausa = rascunhoLocal.comRascunhoDoCampo(base, { id: 'c-1', texto: 'oi!', escritaVista: 0 })
  assert.equal(daPausa.escritasDeFora['c-1'] ?? 0, 0)
  const daDesmontagem = rascunhoLocal.comRascunhoDoCampo(base, { id: 'c-1', texto: 'oi!', escritaVista: 0, adotar: true })
  assert.equal(daDesmontagem.rascunhos['c-1'], 'oi!')
  assert.equal(daDesmontagem.escritasDeFora['c-1'], 1)
  const comSugestao = rascunhoLocal.comRascunhoDeFora(base, 'c-1', 'Sugestão')
  assert.equal(rascunhoLocal.comRascunhoDoCampo(comSugestao, { id: 'c-1', texto: 'oi!', escritaVista: 0, adotar: true }), comSugestao)
  assert.equal(rascunhoLocal.comRascunhoDoCampo(base, { id: 'c-1', texto: 'oi', escritaVista: 0, adotar: true }), base, 'texto igual não mexe em nada')
})

await confere('A. campo: avisa quem assina a cada mudança do texto (é o que re-renderiza só o campo)', comCampo(undefined, ({ campo }) => {
  let avisos = 0
  const desligar = campo.assinar(() => { avisos += 1 })
  campo.digitar('a')
  campo.digitar('a')
  campo.ver({ conversaId: 'c-1', rascunho: 'de fora', escritaDeFora: 1 })
  campo.ver({ conversaId: 'c-1', rascunho: 'de fora', escritaDeFora: 1 })
  assert.equal(avisos, 2, 'tecla repetida e global já visto não acordam o campo')
  desligar()
  campo.digitar('b')
  assert.equal(avisos, 2)
}))

await confere('A. F07: a descarga urgente grava no sessionStorage sem esperar o estado, e respeita a escrita de fora', () => {
  existe(rascunhoLocal, 'comRascunhoDoCampo')
  const sessao = { empresa: { id: 'e-1' }, usuario: { id: 'u-1' } }
  const estado = { rascunhos: { 'c-2': 'outro' }, escritasDeFora: { 'c-1': 1 } }
  gravarRascunhos(sessao, rascunhoLocal.comRascunhoDoCampo(estado, { id: 'c-1', texto: 'antes do 401', escritaVista: 1 }).rascunhos)
  assert.deepEqual(lerRascunhos(sessao), { 'c-1': 'antes do 401', 'c-2': 'outro' })
  gravarRascunhos(sessao, rascunhoLocal.comRascunhoDoCampo(estado, { id: 'c-1', texto: 'atrasado', escritaVista: 0 }).rascunhos)
  assert.deepEqual(lerRascunhos(sessao), { 'c-2': 'outro' }, 'texto que perdeu para a escrita de fora não vai para o storage')
  guardado.clear()
})

await confere('A. ação: a descarga despacha como texto do campo e só a urgente grava direto', () => {
  const despachos = []
  const guardados = []
  const acoes = criarAcoes({ despachar: (a) => despachos.push(a), guardarRascunhoJa: (d) => guardados.push(d) })
  existe(acoes, 'descarregarRascunho')
  acoes.descarregarRascunho({ id: 'c-1', texto: 'oi', escritaVista: 2 })
  assert.deepEqual(despachos, [{ tipo: acao.DEFINIR_RASCUNHO, id: 'c-1', texto: 'oi', escritaVista: 2, doCampo: true, adotar: false }])
  assert.equal(guardados.length, 0)
  acoes.descarregarRascunho({ id: 'c-1', texto: 'oi!', escritaVista: 2 }, { ja: true })
  assert.deepEqual(guardados, [{ id: 'c-1', texto: 'oi!', escritaVista: 2 }])
  assert.equal(despachos.at(-1).adotar, false, 'aba oculta não é desmontagem: o campo continua na tela')
  acoes.descarregarRascunho({ id: 'c-1', texto: 'oi!!', escritaVista: 2 }, { ja: true, desmontando: true })
  assert.equal(despachos.at(-1).adotar, true, 'a desmontagem pede que o campo novo adote o texto')
  assert.equal(guardados.length, 2)
  acoes.definirRascunho('c-1', 'de fora')
  assert.deepEqual(despachos.at(-1), { tipo: acao.DEFINIR_RASCUNHO, id: 'c-1', texto: 'de fora' }, 'quem escreve de fora despacha como antes')
})

// ================================================================================================
// Tela de verdade: reducer e ações reais, massa da demonstração, painéis reais
// ================================================================================================
const AGORA = new Date(INSTANTE_INICIAL).getTime()
const assentar = async () => { for (let i = 0; i < 3; i += 1) await new Promise((r) => setImmediate(r)) }
const andar = async (ms) => { flushSync(() => mock.timers.tick(ms)); await assentar() }
const propsDe = (no) => no[Object.keys(no).find((chave) => chave.startsWith('__reactProps$'))]
const achar = (no, teste) => (teste(no) ? no : no.childNodes.reduce((achado, filho) => achado ?? achar(filho, teste), null))
const todos = (no, teste) => [...(teste(no) ? [no] : []), ...no.childNodes.flatMap((filho) => todos(filho, teste))]

// Qualquer pedaço do Balcão que lê o estado: se ele re-renderiza, a lista de conversas e a Ficha também.
function Vizinho() {
  useAtendimento()
  renders.vizinho += 1
  return null
}

function Provedor({ banca, children }) {
  const [estado, despacharNoEstado] = useReducer(reducer, null, banca.inicial)
  const [pagamentosNaoVistos, setPagamentosNaoVistos] = useState(() => new Set())
  const [agora, setAgora] = useState(AGORA)
  const estadoRef = useRef(estado)
  useLayoutEffect(() => { estadoRef.current = estado })
  const [acoes] = useState(() => criarAcoes({
    despachar: (despacho) => { banca.despachos.push(despacho); despacharNoEstado(despacho) },
    agoraRef: { current: AGORA }, estadoRef, pendentes: { current: [] },
    guardarRascunhoJa: (despacho) => banca.guardados.push(despacho),
  }))
  useLayoutEffect(() => { Object.assign(banca, { estado, acoes, setPagamentosNaoVistos, setAgora }) })
  const valor = useMemo(() => {
    const opcoes = { automaticoPausado: estado.automaticoPausado, janelas: estado.catalogo.janelas, aberta: true, expediente: null }
    return {
      conversas: estado.conversas,
      selecionadaId: estado.selecionadaId,
      selecionada: estado.conversas.find((c) => c.id === estado.selecionadaId) ?? null,
      filtros: estado.filtros,
      visiveis: ordenarBalcao(
        conversasDoBalcao(estado.conversas, estado.filtros, agora, estado.automaticoPausado, true, estado.catalogo.janelas, null, null),
        agora, { ...opcoes, ordenacao: estado.filtros.ordenacao, aba: estado.filtros.aba },
      ),
      encerradasDoBalcao: [],
      bloqueadasDoBalcao: [],
      automaticoPausado: estado.automaticoPausado,
      rascunho: estado.rascunhos[estado.selecionadaId] ?? '',
      escritaDeFora: estado.escritasDeFora?.[estado.selecionadaId] ?? 0,
      agora, aberta: true, horarioDaLoja: null, fonteApi: false, sessao: null, pagamentosNaoVistos,
    }
  }, [estado, agora, pagamentosNaoVistos])
  return h(ContextoCatalogo.Provider, { value: estado.catalogo },
    h(ContextoAcoes.Provider, { value: acoes },
      h(ContextoEstado.Provider, { value: valor }, children)))
}

async function montar(filhos) {
  mock.timers.enable({ apis: ['setTimeout'] })
  const banca = {
    despachos: [], guardados: [], erros: [],
    // #2135: a tela abre sem conversa escolhida; a prova começa com a primeira aberta, como a atendente faria.
    inicial: () => {
      const conversas = carregarConversas()
      return { ...estadoInicial({ conversas, catalogo: carregarCatalogo(), regras: [], modoAgente: 'simulado' }), selecionadaId: conversas[0].id }
    },
  }
  const recipiente = documento.body.appendChild(new Elemento('div'))
  Object.assign(renders, { cartoes: [], baloes: [], vizinho: 0 })
  const raizReact = createRoot(recipiente, {
    onUncaughtError: (erro) => banca.erros.push(erro), onCaughtError: (erro) => banca.erros.push(erro),
  })
  try {
    flushSync(() => raizReact.render(h(Provedor, { banca }, filhos(banca))))
    await assentar()
    assert.deepEqual(banca.erros.map((e) => e.stack ?? e.message), [], 'a tela montou sem erro')
  } catch (erro) {
    mock.timers.reset()
    throw erro
  }
  const campo = () => achar(recipiente, (no) => no.localName === 'textarea')
  return Object.assign(banca, {
    recipiente,
    campo,
    noCampo: () => propsDe(campo()).value,
    teclar(texto) {
      const no = campo()
      no.value = texto
      flushSync(() => propsDe(no).onChange({ target: no }))
    },
    evento: (nome, evento = {}) => flushSync(() => propsDe(campo())[nome]?.({ preventDefault() {}, ...evento })),
    agir: (fn) => flushSync(fn),
    rascunhosDoCampo: () => banca.despachos.filter((d) => d.tipo === acao.DEFINIR_RASCUNHO),
    desmontar() {
      flushSync(() => raizReact.unmount())
      documento.body.removeChild(recipiente)
      mock.timers.reset()
    },
  })
}
const naTela = (filhos, fn) => async () => {
  const banca = await montar(filhos)
  try { await fn(banca) } finally { if (banca.recipiente.parentNode) banca.desmontar() }
}
const atendimento = () => [h(PainelAtendimento, { key: 'atendimento', aoAbrirBiblioteca() {} }), h(Vizinho, { key: 'vizinho' })]
const FRASE = ['B', 'Bo', 'Bom', 'Bom ', 'Bom d', 'Bom di', 'Bom dia']

await confere('A. tela: sete teclas não acordam o resto do Balcão; a pausa de 400 ms acorda uma vez', naTela(atendimento, async (t) => {
  const id = t.estado.selecionadaId
  renders.vizinho = 0
  for (const texto of FRASE) {
    t.teclar(texto)
    assert.equal(t.noCampo(), texto, 'a tecla aparece no campo na hora')
  }
  assert.equal(renders.vizinho, 0, `o resto do Balcão re-renderizou ${renders.vizinho} vez(es) enquanto ela digitava`)
  assert.equal(t.rascunhosDoCampo().length, 0, 'nenhum despacho no estado global por tecla')
  await andar(399)
  assert.equal(renders.vizinho, 0)
  await andar(1)
  assert.equal(t.estado.rascunhos[id], 'Bom dia')
  assert.equal(renders.vizinho, 1, 'a pausa leva o texto ao estado global uma vez')
  assert.equal(t.noCampo(), 'Bom dia')
}))

await confere('A. tela: nenhuma letra se perde quando o despacho atrasado volta no meio da digitação', naTela(atendimento, async (t) => {
  const texto = 'Oi, Maria! Seu pedido sai às 12h30, tudo bem?'
  for (let i = 1; i <= texto.length; i += 1) {
    t.teclar(texto.slice(0, i))
    assert.equal(t.noCampo(), texto.slice(0, i))
    // A cada dez letras ela para o bastante para o atraso despachar, e a letra seguinte vem colada.
    if (i % 10 === 0) await andar(400)
    assert.equal(t.noCampo(), texto.slice(0, i), 'o eco do despacho não mexe no campo')
  }
  await andar(400)
  assert.equal(t.noCampo(), texto)
  assert.equal(t.estado.rascunhos[t.estado.selecionadaId], texto)
}))

await confere('A. tela: Enter envia o que está no campo sem esperar a pausa, e o campo limpa', naTela(atendimento, async (t) => {
  const id = t.estado.selecionadaId
  for (const texto of FRASE) t.teclar(texto)
  t.evento('onKeyDown', { key: 'Enter', shiftKey: false })
  await assentar()
  const conversa = t.estado.conversas.find((c) => c.id === id)
  assert.equal(conversa.mensagens.at(-1).texto, 'Bom dia')
  assert.equal(conversa.mensagens.at(-1).dir, 'out')
  assert.equal(t.noCampo(), '', 'o campo limpa depois do envio')
  assert.equal(t.estado.rascunhos[id], '')
  await andar(1000)
  assert.equal(t.noCampo(), '', 'o atraso que estava armado não devolve o texto enviado')
  assert.equal(t.estado.rascunhos[id], '')
}))

await confere('A. tela: sair do campo (blur) leva o texto ao estado global na hora', naTela(atendimento, async (t) => {
  t.teclar('já te respondo')
  t.evento('onBlur')
  await assentar()
  assert.equal(t.estado.rascunhos[t.estado.selecionadaId], 'já te respondo')
}))

await confere('A. tela: "usar sugestão" no meio da digitação chega ao campo e não é desfeita depois', naTela(atendimento, async (t) => {
  const id = t.estado.selecionadaId
  t.teclar('vou ver e')
  t.agir(() => t.acoes.definirRascunho(id, 'Sua marmita sai às 12h, Maria.'))
  await assentar()
  assert.equal(t.noCampo(), 'Sua marmita sai às 12h, Maria.')
  await andar(1000)
  assert.equal(t.noCampo(), 'Sua marmita sai às 12h, Maria.')
  assert.equal(t.estado.rascunhos[id], 'Sua marmita sai às 12h, Maria.')
  t.teclar('Sua marmita sai às 12h, Maria. Pode ser?')
  await andar(400)
  assert.equal(t.estado.rascunhos[id], 'Sua marmita sai às 12h, Maria. Pode ser?', 'depois da sugestão ela segue digitando')
}))

await confere('A. tela: mensagem que sai por outro botão limpa o campo, como antes, mesmo no meio da digitação', naTela(atendimento, async (t) => {
  const id = t.estado.selecionadaId
  t.teclar('estava digitan')
  t.agir(() => t.acoes.enviar(id, 'Segue o cardápio de hoje.'))
  await assentar()
  assert.equal(t.noCampo(), '')
  await andar(1000)
  assert.equal(t.noCampo(), '')
  assert.equal(t.estado.rascunhos[id], '')
}))

await confere('A. tela: trocar de conversa guarda o texto de uma e traz o da outra', naTela(atendimento, async (t) => {
  const [a, b] = t.estado.conversas.map((c) => c.id)
  t.teclar('recado da primeira')
  t.agir(() => t.acoes.selecionar(b))
  await assentar()
  assert.equal(t.estado.rascunhos[a], 'recado da primeira', 'o texto da conversa anterior foi para o rascunho dela')
  assert.equal(t.noCampo(), '', 'a conversa nova não herda o texto da anterior')
  t.teclar('recado da segunda')
  t.agir(() => t.acoes.selecionar(a))
  await assentar()
  assert.equal(t.noCampo(), 'recado da primeira')
  assert.equal(t.estado.rascunhos[b], 'recado da segunda')
  await andar(1000)
  assert.equal(t.estado.rascunhos[a], 'recado da primeira', 'o atraso da outra conversa não escreve nesta')
}))

await confere('A. tela: a tela desmonta no meio da digitação (401) e o texto vai para a gravação direta', naTela(atendimento, async (t) => {
  const id = t.estado.selecionadaId
  t.teclar('estava escrev')
  t.desmontar()
  assert.deepEqual(t.guardados.map((d) => [d.id, d.texto]), [[id, 'estava escrev']])
}))

await confere('A. tela: aba oculta ou página saindo no meio da digitação grava na hora', naTela(atendimento, async (t) => {
  const id = t.estado.selecionadaId
  t.teclar('fechando a ab')
  documento.visibilityState = 'hidden'
  try { t.agir(() => disparar('document', 'visibilitychange')) } finally { documento.visibilityState = 'visible' }
  assert.deepEqual(t.guardados.map((d) => [d.id, d.texto]), [[id, 'fechando a ab']])
  t.teclar('fechando a aba')
  t.agir(() => disparar('window', 'pagehide'))
  await assentar()
  assert.equal(t.guardados.at(-1).texto, 'fechando a aba')
  assert.equal(t.estado.rascunhos[id], 'fechando a aba', 'com a tela viva, o estado global também recebe')
  t.agir(() => disparar('document', 'visibilitychange'))
  assert.equal(t.guardados.length, 2, 'aba que voltou a aparecer não grava de novo')
}))

// ================================================================================================
// B. Cartões e balões
// ================================================================================================
const balcao = () => [h(PainelConversas, { key: 'balcao' })]
const cartoesNaTela = (t) => todos(t.recipiente, (no) => no.localName === 'button' && no.atributos.has('aria-current'))

await confere('B. mudou uma conversa: só o cartão dela re-renderiza', naTela(balcao, async (t) => {
  const total = cartoesNaTela(t).length
  assert.ok(total >= 4, `a massa tem ${total} cartões na lista`)
  assert.equal(renders.cartoes.length, total, 'cada cartão renderizou uma vez ao montar')
  const alvo = t.estado.conversas.at(-1)
  renders.cartoes.length = 0
  t.agir(() => t.acoes.definirRascunho(alvo.id, 'rascunho'))
  await assentar()
  assert.deepEqual(renders.cartoes, [], 'mudança que não é de conversa nenhuma não re-renderiza cartão')
  t.agir(() => t.acoes.enviar(alvo.id, 'Bom dia!'))
  await assentar()
  assert.deepEqual(renders.cartoes, [alvo.id], `re-renderizaram ${renders.cartoes.length} de ${total} cartões`)
  const cartao = cartoesNaTela(t).find((no) => no.textContent.includes(alvo.nome))
  assert.match(cartao.textContent, /Bom dia!/, 'o cartão que mudou mostra a mensagem nova')
}))

await confere('B. pagamento novo de uma conversa: só o cartão dela ganha o selo', naTela(balcao, async (t) => {
  const alvo = t.estado.conversas[1]
  const comSelo = () => todos(t.recipiente, (no) => no.atributos?.get('aria-label') === 'Pagamento novo, ainda não visto').length
  assert.equal(comSelo(), 0)
  renders.cartoes.length = 0
  t.agir(() => t.setPagamentosNaoVistos(new Set([alvo.id])))
  await assentar()
  assert.equal(comSelo(), 1)
  assert.deepEqual(renders.cartoes, [alvo.id])
}))

await confere('B. o relógio de 30 s continua atualizando o tempo de todos os cartões', naTela(balcao, async (t) => {
  const total = cartoesNaTela(t).length
  renders.cartoes.length = 0
  t.agir(() => t.setAgora(AGORA + 30000))
  await assentar()
  assert.equal(renders.cartoes.length, total)
}))

await confere('B. clicar no cartão abre a conversa dele, também depois de a lista mudar', naTela(balcao, async (t) => {
  const [primeira, segunda] = t.estado.conversas
  const clicar = (conversa) => t.agir(() => propsDe(cartoesNaTela(t).find((no) => no.textContent.includes(conversa.nome))).onClick())
  clicar(segunda)
  await assentar()
  assert.equal(t.estado.selecionadaId, segunda.id)
  t.agir(() => t.acoes.enviar(primeira.id, 'Oi!'))
  await assentar()
  clicar(primeira)
  await assentar()
  assert.equal(t.estado.selecionadaId, primeira.id)
  const aberto = cartoesNaTela(t).filter((no) => propsDe(no)['aria-current'])
  assert.equal(aberto.length, 1, 'só um cartão marcado como aberto')
  assert.ok(aberto[0].textContent.includes(primeira.nome))
}))

await confere('B. mensagem enviada pela tela: os balões antigos não re-renderizam', naTela(atendimento, async (t) => {
  const id = t.estado.selecionadaId
  const antigos = renders.baloes.length
  assert.ok(antigos >= 2, `a conversa aberta tem ${antigos} balões de texto`)
  renders.baloes.length = 0
  t.agir(() => t.acoes.enviar(id, 'Texto novo da prova'))
  await assentar()
  assert.deepEqual(renders.baloes, ['Texto novo da prova'], `re-renderizaram ${renders.baloes.length} balões para uma mensagem nova`)
  renders.baloes.length = 0
  await andar(900)
  assert.deepEqual(renders.baloes, ['Texto novo da prova'], 'a confirmação de entrega re-renderiza só o balão dela')
}))

// Modo API: a cada leitura com novidade as mensagens chegam como objetos novos, com o mesmo conteúdo.
const mensagemDaLeitura = (n, extra = {}) => ({
  id: `m-${n}`, dir: n % 2 ? 'in' : 'out', texto: `mensagem ${n} da leitura`, em: `2026-10-11T14:${10 + n}:00Z`, status: 'lida', ...extra,
})
const leituraDeMensagens = (quantas, extra = {}) => Array.from({ length: quantas }, (_, i) => mensagemDaLeitura(i + 1, extra[i + 1]))

function FioDaLeitura({ banca }) {
  const [mensagens, setMensagens] = useState(() => leituraDeMensagens(40))
  useLayoutEffect(() => { banca.setMensagens = setMensagens })
  // Handlers novos a cada render, como o PainelAtendimento passa.
  return h(Thread, {
    mensagens, chaveRolagem: 'c-1', agora: AGORA, notas: [],
    aoAbrirDefinicaoAutomatica: () => {}, aoReenviar: (mensagemId) => banca.reenvios.push(mensagemId),
  })
}
const fio = (banca) => { banca.reenvios = []; return [h(FioDaLeitura, { key: 'fio', banca })] }

await confere('B. leitura da API com mensagem nova: os 40 balões antigos não re-renderizam', naTela(fio, async (t) => {
  assert.equal(renders.baloes.length, 40)
  renders.baloes.length = 0
  t.agir(() => t.setMensagens(leituraDeMensagens(41)))
  await assentar()
  assert.deepEqual(renders.baloes, ['mensagem 41 da leitura'], `re-renderizaram ${renders.baloes.length} balões para uma mensagem nova`)
}))

await confere('B. mensagem antiga que mudou (falhou) re-renderiza e o Reenviar chama o handler de agora', naTela(fio, async (t) => {
  renders.baloes.length = 0
  t.agir(() => t.setMensagens(leituraDeMensagens(40, { 4: { status: 'falhou', erro: 'Não enviada: canal fora.' } })))
  await assentar()
  assert.deepEqual(renders.baloes, ['mensagem 4 da leitura'], 'o memo não esconde mudança de conteúdo')
  const reenviar = achar(t.recipiente, (no) => no.localName === 'button' && no.textContent.includes('Reenviar'))
  assert.ok(reenviar, 'o balão que falhou mostra o Reenviar')
  t.agir(() => propsDe(reenviar).onClick())
  assert.deepEqual(t.reenvios, ['m-4'])
}))

await confere('B. props de memo: iguais por identidade ou, na chave pedida, por conteúdo', () => {
  existe(igualdade, 'mesmasProps')
  const aoReenviar = () => {}
  const antes = { mensagem: mensagemDaLeitura(1), trocaDeVoz: false, aoReenviar }
  assert.equal(igualdade.mesmasProps(antes, { mensagem: mensagemDaLeitura(1), trocaDeVoz: false, aoReenviar }, ['mensagem']), true)
  assert.equal(igualdade.mesmasProps(antes, { mensagem: mensagemDaLeitura(1), trocaDeVoz: false, aoReenviar }), false, 'sem a chave, vale a identidade')
  assert.equal(igualdade.mesmasProps(antes, { mensagem: mensagemDaLeitura(1, { status: 'falhou' }), trocaDeVoz: false, aoReenviar }, ['mensagem']), false)
  assert.equal(igualdade.mesmasProps(antes, { ...antes, trocaDeVoz: true }, ['mensagem']), false)
  assert.equal(igualdade.mesmasProps(antes, { ...antes, aoReenviar: () => {} }, ['mensagem']), false)
  assert.equal(igualdade.mesmasProps(antes, { mensagem: antes.mensagem, trocaDeVoz: false }, ['mensagem']), false, 'prop que sumiu é mudança')
})

const { CartaoConversa } = await import('../src/features/caixa-de-entrada/CartaoConversa.jsx')
for (const [descricao, saldo, cobranca, sinalNaReserva, esperado] of [
  ['complemento pendente mostra o saldo devido', 35, { valor: 10, pagaEm: null, diferenca: true }, true, 'Falta R$ 10,00'],
  ['complemento quitado mantém o rótulo pago', 45, { valor: 10, pagaEm: AGORA }, true, 'Pago'],
  ['sinal quitado com saldo futuro comunica sinal pago', 22.5, { valor: 22.5, pagaEm: AGORA }, true, 'Sinal pago'],
  ['devolução reduz cobertura sem chamar o pedido de pago', 25, { valor: 35, pagaEm: AGORA }, true, 'Falta R$ 20,00'],
]) await confere(`B. rótulo financeiro: ${descricao}`, naTela(() => {
  const conversa = carregarConversas().find(c => c.pedido)
  return [h(CartaoConversa, { key: descricao, conversa: { ...conversa, pedido: { ...conversa.pedido, estado: 'pago', totalApi: 45, alocadoApi: saldo, sinalNaReserva, cobranca } }, selecionada: false, agora: AGORA, aoAbrir: () => {} })]
}, async (t) => { assert.ok(t.recipiente.textContent.includes(esperado), `rótulo esperado: ${esperado}; tela: ${t.recipiente.textContent}`) }))

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
