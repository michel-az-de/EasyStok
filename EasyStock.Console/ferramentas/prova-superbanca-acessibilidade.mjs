// Compila componentes reais; exercita seus handlers com DOM/efeitos controlados.
// Pixels, Tab nativo, dialog e leitores de tela precisam de conferência no navegador.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { execFileSync } from 'node:child_process'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'

const fontes = new Map()
const base = process.argv.includes('--base') ? process.argv[process.argv.indexOf('--base') + 1] : null
for (const nome of ['AlcaLargura', 'Popover', 'Modal', 'Chip', 'Icone', 'Campo', 'Botao', 'BarraDeAbas', 'EscolhaDeEntregador', 'EscolhaDeProvedorEntrega', 'InterruptorTema']) {
  const url = new URL(`../src/componentes/${nome}.jsx`, import.meta.url)
  const fonte = base ? execFileSync('git', ['show', `${base}:EasyStock.Console/src/componentes/${nome}.jsx`], { encoding: 'utf8' }) : readFileSync(url, 'utf8')
  fontes.set(url.href, (await transformWithOxc(fonte, url.pathname, { jsx: { runtime: 'automatic' } })).code)
}
const urlSeletor = new URL('../src/features/atendimento/SeletorRespostas.jsx', import.meta.url)
const fonteSeletor = base ? execFileSync('git', ['show', `${base}:EasyStock.Console/src/features/atendimento/SeletorRespostas.jsx`], { encoding: 'utf8' }) : readFileSync(urlSeletor, 'utf8')
fontes.set(urlSeletor.href, (await transformWithOxc(fonteSeletor, urlSeletor.pathname, { jsx: { runtime: 'automatic' } })).code)
let host
const hookRef = valor => host.valores[host.indice++] ??= { current: valor }
const hooks = {
  useRef: hookRef, useId: () => `prova-${host.indice++}`,
  useState(valor) {
    const atual = host, indice = atual.indice++
    if (!(indice in atual.valores)) atual.valores[indice] = typeof valor === 'function' ? valor() : valor
    return [atual.valores[indice], proximo => { atual.valores[indice] = typeof proximo === 'function' ? proximo(atual.valores[indice]) : proximo }]
  },
  useEffect: fn => host.efeitos.push(fn), useLayoutEffect: fn => host.layout.push(fn),
  forwardRef: fn => props => fn(props, props.ref),
}
globalThis.__provaSuperbancaAcessibilidade = { hooks }
const dados = codigo => 'data:text/javascript,' + encodeURIComponent(codigo)
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec === 'react') return { url: dados('const h=globalThis.__provaSuperbancaAcessibilidade.hooks; export const useRef=h.useRef,useId=h.useId,useState=h.useState,useEffect=h.useEffect,useLayoutEffect=h.useLayoutEffect,forwardRef=h.forwardRef;'), shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: dados('export const jsx=(type,props)=>({type,props}),jsxs=jsx,Fragment="fragment";'), shortCircuit: true }
    if (spec === 'react-dom') return { url: dados('export const createPortal=(elemento)=>elemento;'), shortCircuit: true }
    if (spec === '../hooks/useTema') return { url: dados('export const useTema=()=>["light", globalThis.__provaSuperbancaAcessibilidade.trocarTema];'), shortCircuit: true }
    if (spec.endsWith('.css')) return { url: dados('export default new Proxy({}, {get:(_,k)=>k});'), shortCircuit: true }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const extensao of ['.js', '.jsx']) { try { return proximo(spec + extensao, contexto) } catch { /* próxima extensão */ } }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) { return fontes.has(url) ? { source: fontes.get(url), format: 'module', shortCircuit: true } : proximo(url, contexto) },
})
const { AlcaLargura } = await import('../src/componentes/AlcaLargura.jsx')
const { Popover } = await import('../src/componentes/Popover.jsx')
const { Modal } = await import('../src/componentes/Modal.jsx')
const { Chip } = await import('../src/componentes/Chip.jsx')
const { BarraDeAbas } = await import('../src/componentes/BarraDeAbas.jsx')
const { EscolhaDeEntregador } = await import('../src/componentes/EscolhaDeEntregador.jsx')
const { EscolhaDeProvedorEntrega } = await import('../src/componentes/EscolhaDeProvedorEntrega.jsx')
const { InterruptorTema } = await import('../src/componentes/InterruptorTema.jsx')
const { SeletorRespostas } = await import('../src/features/atendimento/SeletorRespostas.jsx')
const ouvintes = new Map()
globalThis.window = {
  addEventListener(tipo, fn) { if (!ouvintes.has(tipo)) ouvintes.set(tipo, new Set()); ouvintes.get(tipo).add(fn) },
  removeEventListener(tipo, fn) { ouvintes.get(tipo)?.delete(fn) },
}
globalThis.document = { activeElement: null, addEventListener() {}, removeEventListener() {} }
const emitir = (tipo, evento = {}) => [...(ouvintes.get(tipo) ?? [])].forEach(fn => fn(evento))
const montar = (componente, props) => {
  host = { valores: [], indice: 0, efeitos: [], layout: [], limpezas: [] }
  const atual = host, elemento = componente(props)
  return { elemento,
    renderizar() { host = atual; atual.indice = 0; return componente(props) },
    efeitos() { [...atual.layout, ...atual.efeitos].forEach(fn => { const limpar = fn(); if (limpar) atual.limpezas.push(limpar) }) },
    desmontar() { atual.limpezas.forEach(fn => fn()) },
  }
}
const andar = (elemento, teste) => {
  if (!elemento || typeof elemento !== 'object') return []
  return [...(teste(elemento) ? [elemento] : []), ...[elemento.props?.children].flat(Infinity).flatMap(filho => andar(filho, teste))]
}
const no = (role, disabled = false, ariaDisabled = false, checked = false) => ({
  role, disabled, isConnected: true, tabIndex: 0, clicado: 0,
  focus() { if (!this.disabled) document.activeElement = this },
  click() { if (!this.disabled) this.clicado++ },
  getAttribute(nome) { return nome === 'aria-disabled' ? String(ariaDisabled) : nome === 'aria-checked' ? String(checked) : nome === 'role' ? role : null },
  matches(seletor) { return seletor === 'button' || seletor.includes(`[role="${role}"]`) || seletor.includes(`[role='${role}']`) },
  closest(seletor) { return seletor.includes('dialog') ? null : seletor.includes('radiogroup') ? this.grupo : this.matches(seletor) ? this : null },
  contains(alvo) { return this === alvo },
})
const tecla = (key, target, extra = {}) => ({ key, target, prevenido: false, preventDefault() { this.prevenido = true; this.defaultPrevented = true }, stopPropagation() {}, ...extra })
const origem = no('button')
const menu = (itens, props = {}) => {
  document.activeElement = origem
  const montagem = montar(Popover, { rotulo: 'Opções', grupos: [], aoFechar() {}, ...props })
  const caixa = {
    closest() { return null },
    querySelectorAll(seletor) { return itens.filter(item => item.matches(seletor) && (!seletor.includes(':not(:disabled)') || !item.disabled)) },
    querySelector() { return itens[0] }, contains(alvo) { return itens.includes(alvo) },
  }
  montagem.elemento.props.ref.current = caixa
  montagem.efeitos()
  return montagem
}
let aprovados = 0
const falhas = []
const provar = (nome, executar) => {
  ouvintes.clear()
  try { executar(); aprovados++; console.log('ok ' + nome) }
  catch (erro) { falhas.push({ nome, erro: erro.message }); console.log('FALHA ' + nome + ': ' + erro.message.split('\n')[0]) }
}

for (const role of ['menuitem', 'menuitemradio', 'menuitemcheckbox']) {
  provar(`Popover: setas/Home/End navegam ${role} sem ativar opções`, () => {
    const itens = [no(role), no(role), no(role)]
    const m = menu(itens)
    try {
      assert.equal(document.activeElement, itens[0])
      for (const [key, esperado] of [['ArrowDown', 1], ['End', 2], ['ArrowDown', 0], ['ArrowUp', 2], ['Home', 0]]) {
        const evento = tecla(key, document.activeElement)
        m.elemento.props.onKeyDown(evento)
        assert.equal(evento.prevenido, true)
        assert.equal(document.activeElement, itens[esperado])
      }
      assert.deepEqual(itens.map(item => item.clicado), [0, 0, 0])
    } finally { m.desmontar() }
  })
}
provar('Popover: foco e setas ignoram opções disabled e aria-disabled', () => {
  const itens = [no('menuitem', true), no('menuitem'), no('menuitem', false, true), no('menuitem')]
  const m = menu(itens)
  try {
    assert.equal(document.activeElement, itens[1])
    m.elemento.props.onKeyDown(tecla('ArrowDown', itens[1]))
    assert.equal(document.activeElement, itens[3])
  } finally { m.desmontar() }
})
provar('Popover: Tab fecha e prepara saída pelo acionador, sem sequestrar Tab', () => {
  let fechou = 0
  const m = menu([no('menuitem')], { aoFechar: () => fechou++ })
  try {
    const evento = tecla('Tab', document.activeElement)
    m.elemento.props.onKeyDown(evento)
    assert.equal(fechou, 1); assert.equal(document.activeElement, origem); assert.equal(evento.prevenido, false)
  } finally { m.desmontar() }
})
provar('Popover: grupo de busca/listbox conserva sua semântica e teclado', () => {
  const m = menu([no('button')], { papel: 'group', semAutoFoco: true })
  try { assert.equal(m.elemento.props.role, 'group'); assert.equal(document.activeElement, origem) }
  finally { m.desmontar() }
})
provar('Popover: combinação Alt+seta preserva o comando do navegador', () => {
  const itens = [no('menuitem'), no('menuitem')], m = menu(itens)
  try {
    const evento = tecla('ArrowDown', itens[0], { altKey: true })
    m.elemento.props.onKeyDown(evento)
    assert.equal(evento.prevenido, false); assert.equal(document.activeElement, itens[0])
  } finally { m.desmontar() }
})

const gruposComStatus = [
  { titulo: 'Ver', itens: [{ chave: 'modulos', titulo: 'Módulos' }, { chave: 'ajuda', titulo: 'Ajuda e diagnóstico' }] },
  { titulo: 'Hoje', itens: [{ chave: 'hoje', titulo: '3 em andamento', detalhe: '1 na esteira', informativo: true }] },
]
provar('Popover: resumo informativo conserva texto sem virar botao ou receber click', () => {
  const m = montar(Popover, { grupos: gruposComStatus, aoEscolher() {}, aoFechar() {} })
  const linhas = andar(m.elemento, elemento => elemento.type === 'button' || elemento.props.className?.split(' ').includes('informativo'))
  assert.equal(linhas.filter(elemento => elemento.type === 'button').length, 2)
  const status = linhas.at(-1)
  assert.equal(status.type, 'div')
  assert.equal(status.props.role, undefined)
  assert.equal(status.props.tabIndex, undefined)
  assert.equal(status.props.onClick, undefined)
  assert.deepEqual(status.props.children.map(elemento => elemento.props.children), ['3 em andamento', '1 na esteira'])
})
provar('Popover: End e setas pulam resumo do dia e chegam a ultima acao real', () => {
  const escolhidas = [], m = montar(Popover, { grupos: gruposComStatus, aoEscolher: item => escolhidas.push(item.chave), aoFechar() {} })
  const linhas = andar(m.elemento, elemento => elemento.type === 'button' || elemento.props.className?.split(' ').includes('informativo'))
  const itens = linhas.map(elemento => no(elemento.props.role))
  m.elemento.props.ref.current = {
    closest: () => null,
    querySelectorAll: seletor => itens.filter(item => item.matches(seletor)),
    querySelector: seletor => itens.find(item => item.matches(seletor)),
    contains: alvo => itens.includes(alvo),
  }
  document.activeElement = origem
  m.efeitos()
  try {
    assert.equal(document.activeElement, itens[0])
    for (const [key, esperado] of [['End', 1], ['ArrowDown', 0], ['ArrowUp', 1]]) {
      m.elemento.props.onKeyDown(tecla(key, document.activeElement))
      assert.equal(document.activeElement, itens[esperado])
    }
    m.elemento.props.onClickCapture({ target: itens[2] })
    assert.equal(document.activeElement, itens[1], 'texto informativo nao devolve foco nem aciona o menu')
    assert.deepEqual(escolhidas, [])
    linhas[1].props.onClick()
    assert.deepEqual(escolhidas, ['ajuda'])
  } finally { m.desmontar() }
})

for (const protegido of [true, false]) {
  provar(`Modal: ${protegido ? 'fechamento indisponível bloqueia Escape/backdrop/X sem exceção' : 'Escape e backdrop usam o callback disponível'}`, () => {
    let fechou = 0
    const eventos = new Map(), m = montar(Modal, { titulo: 'Nota', aoFechar: protegido ? undefined : () => fechou++ })
    const dialogo = { open: false, parentElement: { closest: () => null },
      showModal() { this.open = true }, close() { this.open = false },
      addEventListener(nome, fn) { eventos.set(nome, fn) }, removeEventListener(nome) { eventos.delete(nome) },
    }
    document.activeElement = origem
    m.elemento.props.ref.current = dialogo
    m.efeitos()
    try {
      const fechar = andar(m.elemento, elemento => elemento.props['aria-label'] === 'Fechar janela')[0]
      const cancelamento = tecla('Escape', dialogo)
      assert.doesNotThrow(() => eventos.get('cancel')(cancelamento))
      assert.equal(cancelamento.prevenido, true)
      assert.doesNotThrow(() => eventos.get('click')({ target: dialogo }))
      assert.equal(Boolean(fechar.props.disabled), protegido)
      assert.equal(fechou, protegido ? 0 : 2)
      assert.equal(dialogo.open, true, 'showModal continua sob controle do pai')
    } finally { m.desmontar() }
    assert.equal(dialogo.open, false); assert.equal(eventos.size, 0)
  })
}

// #2078 (B11/E4): modal de dinheiro. Opt-in: sem as props, nenhum modal muda (o laço acima).
const dialogoFalso = (eventos, campo = null) => ({ open: false, parentElement: { closest: () => null }, buscas: [],
  showModal() { this.open = true }, close() { this.open = false },
  addEventListener(nome, fn) { eventos.set(nome, fn) }, removeEventListener(nome) { eventos.delete(nome) },
  querySelector(seletor) { this.buscas.push(seletor); return campo },
})
provar('Modal: segurarFechamento ignora Escape e fundo com valor digitado; o X e o Cancelar continuam fechando', () => {
  let fechou = 0
  const eventos = new Map(), m = montar(Modal, { titulo: 'Fechar o caixa', segurarFechamento: true, aoFechar: () => fechou++ })
  const dialogo = dialogoFalso(eventos)
  m.elemento.props.ref.current = dialogo
  m.efeitos()
  try {
    const cancelamento = tecla('Escape', dialogo)
    eventos.get('cancel')(cancelamento)
    assert.equal(cancelamento.prevenido, true, 'o navegador não fecha o dialog sozinho')
    eventos.get('click')({ target: dialogo })
    assert.equal(fechou, 0, 'Escape e clique no fundo não perdem o valor digitado')
    const fechar = andar(m.elemento, elemento => elemento.props['aria-label'] === 'Fechar janela')[0]
    assert.equal(Boolean(fechar.props.disabled), false)
    fechar.props.onClick()
    assert.equal(fechou, 1, 'o X é um gesto explícito e fecha')
  } finally { m.desmontar() }
})
// #2078 (revisão R2): no Chromium o 2º Esc seguido, sem ativação do usuário, não pode ser cancelado:
// o dialog fecha sozinho e o React não sabia. O `close` nativo agora é ouvido.
for (const caso of ['segurando', 'livre', 'protegido']) {
  provar(`Modal: close nativo (Esc não cancelável) ${caso === 'livre' ? 'avisa o pai' : 'reabre a folha sem perder o valor'}`, () => {
    let fechou = 0
    const props = { titulo: 'Fechar o caixa', segurarFechamento: caso === 'segurando', aoFechar: caso === 'protegido' ? undefined : () => fechou++ }
    const eventos = new Map(), m = montar(Modal, props)
    const dialogo = dialogoFalso(eventos)
    let reaberto = 0
    dialogo.showModal = function () { this.open = true; reaberto++ }
    m.elemento.props.ref.current = dialogo
    m.efeitos()
    try {
      assert.ok(eventos.has('close'), 'o modal ouve o close nativo')
      dialogo.open = false
      eventos.get('close')({ target: dialogo })
      if (caso === 'livre') { assert.equal(fechou, 1); assert.equal(reaberto, 1, 'só a abertura') }
      else { assert.equal(fechou, 0); assert.equal(dialogo.open, true); assert.equal(reaberto, 2) }
    } finally { m.desmontar() }
    assert.equal(eventos.size, 0, 'desmontar tira o ouvinte antes do close() da limpeza')
  })
}
provar('Modal: focarCampo põe o foco no campo marcado (ou no primeiro campo), sem rolar a página; sem a prop, o foco é o do navegador', () => {
  const focos = []
  const campo = { focus(opcoes) { focos.push(opcoes) } }
  const eventos = new Map(), m = montar(Modal, { titulo: 'Lançar', focarCampo: true, aoFechar() {} })
  const dialogo = dialogoFalso(eventos, campo)
  m.elemento.props.ref.current = dialogo
  m.efeitos()
  try {
    assert.deepEqual(focos, [{ preventScroll: true }])
    assert.deepEqual(dialogo.buscas, ['[data-foco-inicial]:not(:disabled)'], 'o campo marcado vem antes de qualquer outro')
  } finally { m.desmontar() }
  // Sem campo marcado, o primeiro campo do modal.
  const focosSemMarca = []
  const eventosSemMarca = new Map(), semMarca = montar(Modal, { titulo: 'Lançar', focarCampo: true, aoFechar() {} })
  const dialogoSemMarca = dialogoFalso(eventosSemMarca, null)
  dialogoSemMarca.querySelector = function (seletor) { this.buscas.push(seletor); return seletor.startsWith('input') ? { focus: (o) => focosSemMarca.push(o) } : null }
  semMarca.elemento.props.ref.current = dialogoSemMarca
  semMarca.efeitos()
  try {
    assert.deepEqual(focosSemMarca, [{ preventScroll: true }])
    assert.match(dialogoSemMarca.buscas[1], /^input:not\(:disabled\), select:not\(:disabled\), textarea:not\(:disabled\)$/)
  } finally { m.desmontar() }
  const semProp = new Map(), outro = montar(Modal, { titulo: 'Nota', aoFechar() {} })
  const dialogoOutro = dialogoFalso(semProp, campo)
  outro.elemento.props.ref.current = dialogoOutro
  outro.efeitos()
  try { assert.equal(dialogoOutro.buscas.length, 0, 'os outros modais seguem com o foco que o showModal dá') } finally { outro.desmontar() }
})
provar('Modal: a folha não é posicionada como relativa no top layer (vira absoluta e rola com a página em 390 px)', () => {
  const cssModal = readFileSync(new URL('../src/componentes/Modal.module.css', import.meta.url), 'utf8')
  const posicoes = [...cssModal.matchAll(/\.caixa\s*\{[^}]*?position:\s*([a-z]+)/g)].map(m => m[1])
  assert.ok(posicoes.length > 0, 'a folha declara a posição')
  assert.ok(posicoes.every(p => p === 'fixed'), `posições da .caixa: ${posicoes.join(', ')}`)
})

const propsAlca = aoMudar => ({ rotulo: 'Largura do Balcão', valor: 320, min: 280, max: 440, padrao: 360, aoMudar })
const iniciar = montagem => montagem.elemento.props.onPointerDown({ button: 0, pointerId: 1, clientX: 100, preventDefault() {}, currentTarget: origem })
provar('Alça: Enter restaura a largura e anuncia valor/unidade/instruções', () => {
  const valores = [], m = montar(AlcaLargura, propsAlca(valor => valores.push(valor)))
  const evento = tecla('Enter', origem)
  m.elemento.props.onKeyDown(evento)
  assert.deepEqual(valores, [360]); assert.equal(evento.prevenido, true)
  assert.equal(m.elemento.props['aria-valuetext'], '320 pixels')
  assert.ok(m.elemento.props['aria-describedby'])
})
for (const fim of ['pointerup', 'pointercancel', 'blur', 'desmontar']) {
  provar(`Alça: ${fim} encerra arrasto e remove ouvintes`, () => {
    const valores = [], m = montar(AlcaLargura, propsAlca(valor => valores.push(valor)))
    m.efeitos(); iniciar(m)
    emitir('pointermove', { pointerId: 1, clientX: 116 })
    assert.deepEqual(valores, [336])
    if (fim === 'desmontar') m.desmontar(); else emitir(fim, fim === 'blur' ? {} : { pointerId: 1 })
    emitir('pointermove', { pointerId: 1, clientX: 180 })
    assert.deepEqual(valores, [336]); assert.equal([...ouvintes.values()].reduce((n, itens) => n + itens.size, 0), 0)
    m.desmontar()
  })
}
provar('Alça: outro ponteiro não move ou encerra o arrasto ativo', () => {
  const valores = [], m = montar(AlcaLargura, propsAlca(valor => valores.push(valor)))
  m.efeitos(); iniciar(m)
  emitir('pointermove', { pointerId: 2, clientX: 180 }); emitir('pointerup', { pointerId: 2 })
  emitir('pointermove', { pointerId: 1, clientX: 116 })
  assert.deepEqual(valores, [336]); m.desmontar()
})
provar('Alça: setas invertidas, Shift, Home/End e reset respeitam limites', () => {
  const valores = [], m = montar(AlcaLargura, { ...propsAlca(valor => valores.push(valor)), invertida: true, padrao: 500 })
  for (const [key, shiftKey] of [['ArrowRight', false], ['ArrowLeft', true], ['Home', false], ['End', false], ['Enter', false]]) m.elemento.props.onKeyDown(tecla(key, origem, { shiftKey }))
  assert.deepEqual(valores, [304, 384, 280, 440, 440])
  m.elemento.props.onDoubleClick(); assert.equal(valores.at(-1), 440)
})
provar('Entregador: ações imediatas são botões, sem rádios órfãos/desmarcados', () => {
  const m = montar(EscolhaDeEntregador, { conhecidos: [{ chave: 'ana', rotulo: 'Ana', entregador: { nome: 'Ana' } }], aoEscolher() {} })
  const chips = andar(m.elemento, elemento => elemento.type === Chip)
  for (const chip of chips.slice(0, 2)) {
    const botao = Chip(chip.props)
    assert.equal(botao.type, 'button'); assert.equal(botao.props.role, undefined); assert.equal(botao.props['aria-pressed'], undefined)
  }
})
provar('Provedor: radiogroup tem uma entrada Tab e setas escolhem/focam', () => {
  const escolhas = [], m = montar(EscolhaDeProvedorEntrega, {
    provedores: [{ chave: 'proprio', rotulo: 'Próprio' }, { chave: 'outro', rotulo: 'Outro' }],
    provedorEscolhido: 'proprio', aoEscolherProvedor: valor => escolhas.push(valor),
  })
  const chips = andar(m.elemento, elemento => elemento.type === Chip).map(el => Chip(el.props))
  assert.deepEqual(chips.map(el => el.props.tabIndex), [0, -1])
  const itens = [no('radio', false, false, true), no('radio')]
  const grupo = { querySelectorAll: () => itens }
  itens.forEach((item, i) => { item.grupo = grupo; item.click = () => chips[i].props.onClick() })
  const evento = tecla('ArrowRight', itens[0]); evento.currentTarget = itens[0]
  chips[0].props.onKeyDown(evento)
  assert.equal(evento.prevenido, true); assert.equal(document.activeElement, itens[1]); assert.deepEqual(escolhas, ['outro'])
})
for (const selecionado of [null, 1]) {
  provar(`Chip radio: entrada Tab ${selecionado === null ? 'primeira sem selecao' : 'selecionada'} e navegacao ignora indisponiveis`, () => {
    const itens = [no('radio'), no('radio', false, false, selecionado === 1), no('radio', true), no('radio', false, true)]
    const grupo = { querySelectorAll: () => itens }
    const montagens = itens.map((item, i) => {
      item.grupo = grupo
      const m = montar(Chip, { papel: 'escolha', ativo: i === selecionado, disabled: item.disabled, emBreve: i === 3, children: `Radio ${i}` })
      item.tabIndex = m.elemento.props.tabIndex ?? 0
      if (m.elemento.props.ref) m.elemento.props.ref.current = item
      return m
    })
    montagens.forEach(m => m.efeitos())
    try {
      assert.deepEqual(itens.map(item => item.tabIndex), [selecionado === null ? 0 : -1, selecionado === 1 ? 0 : -1, -1, -1])
      const evento = tecla('ArrowLeft', itens[0]); evento.currentTarget = itens[0]
      montagens[0].elemento.props.onKeyDown(evento)
      assert.equal(document.activeElement, itens[1]); assert.equal(itens[1].clicado, 1)
      assert.deepEqual(itens.slice(2).map(item => item.clicado), [0, 0])
    } finally { montagens.forEach(m => m.desmontar()) }
  })
}

const abasHistorico = ['pedidos', 'atendimentos', 'notas', 'automaticas'].map(id => ({ id, rotulo: id }))
provar('Abas: bloqueio temporario torna todas indisponiveis e libera sem mudar selecao', () => {
  const props = { abas: abasHistorico, ativa: 'notas', aoTrocar() {} }
  const m = montar(BarraDeAbas, props)
  const botoes = () => andar(m.renderizar(), elemento => elemento.props.role === 'tab')
  assert.deepEqual(botoes().map(botao => Boolean(botao.props.disabled)), [false, false, false, false])
  props.desabilitada = true
  const ocupadas = botoes()
  assert.deepEqual(ocupadas.map(botao => Boolean(botao.props.disabled)), [true, true, true, true])
  assert.deepEqual(ocupadas.map(botao => botao.props['aria-selected']), [false, false, true, false])
  props.desabilitada = false
  assert.deepEqual(botoes().map(botao => Boolean(botao.props.disabled)), [false, false, false, false])
})
provar('Abas: consumidores normais mantem foco circular, Home/End e ativacao manual', () => {
  const escolhas = [], m = montar(BarraDeAbas, { abas: abasHistorico, ativa: 'notas', aoTrocar: id => escolhas.push(id) })
  const botoes = andar(m.elemento, elemento => elemento.props.role === 'tab')
  const itens = botoes.map(botao => no('tab', Boolean(botao.props.disabled)))
  const grupo = { querySelectorAll: () => itens }
  itens.forEach((item, indice) => {
    item.parentElement = grupo
    item.focus = () => { document.activeElement = item; botoes[indice].props.onFocus() }
  })
  assert.deepEqual(botoes.map(botao => botao.props.tabIndex), [-1, -1, 0, -1])
  itens[2].focus()
  for (const [key, esperado] of [['ArrowRight', 3], ['ArrowRight', 0], ['ArrowLeft', 3], ['Home', 0], ['End', 3]]) {
    const indice = itens.indexOf(document.activeElement), evento = tecla(key, itens[indice])
    evento.currentTarget = itens[indice]
    botoes[indice].props.onKeyDown(evento)
    assert.equal(evento.prevenido, true)
    assert.equal(document.activeElement, itens[esperado])
  }
  assert.deepEqual(escolhas, [], 'mover foco nao ativa uma aba')
  const focadas = andar(m.renderizar(), elemento => elemento.props.role === 'tab')
  assert.deepEqual(focadas.map(botao => botao.props.tabIndex), [-1, -1, -1, 0])
  const evento = tecla('ArrowRight', itens[3], { altKey: true, currentTarget: itens[3] })
  focadas[3].props.onKeyDown(evento)
  assert.equal(evento.prevenido, false)
  assert.equal(document.activeElement, itens[3])
  focadas[3].props.onClick()
  assert.deepEqual(escolhas, ['automaticas'])
})

provar('Tema: atalhos modificados conservam comportamento do navegador sem trocar aparência', () => {
  const escolhas = []
  globalThis.__provaSuperbancaAcessibilidade.trocarTema = tema => escolhas.push(tema)
  const m = montar(InterruptorTema, {})
  const currentTarget = { querySelector: () => origem }
  for (const modificador of ['altKey', 'ctrlKey', 'metaKey']) {
    for (const key of ['ArrowLeft', 'ArrowRight', 'Home', 'End']) {
      const evento = tecla(key, origem, { currentTarget, [modificador]: true })
      m.elemento.props.onKeyDown(evento)
      assert.equal(evento.prevenido, false)
    }
  }
  assert.deepEqual(escolhas, [])
  const evento = tecla('ArrowRight', origem, { currentTarget })
  m.elemento.props.onKeyDown(evento)
  assert.equal(evento.prevenido, true)
  assert.deepEqual(escolhas, ['dark'])
})

provar('Popover portal: acompanha rolagem e resize medindo uma vez por quadro; desmontagem limpa tudo', () => {
  const quadros = new Map()
  let proximoQuadro = 0, medicoes = 0, esquerda = 100, topo = 300, largura = 240
  window.requestAnimationFrame = fn => { const id = ++proximoQuadro; quadros.set(id, fn); return id }
  window.cancelAnimationFrame = id => quadros.delete(id)
  const passarQuadro = () => { const tarefas = [...quadros.values()]; quadros.clear(); tarefas.forEach(fn => fn()) }
  document.body = { corpo: true }
  const ancora = {
    getBoundingClientRect() { medicoes++; return { left: esquerda, top: topo, width: largura, height: 44 } },
    closest: () => null,
  }
  const m = montar(Popover, { rotulo: 'Respostas', grupos: [], aoFechar() {}, portal: true, semAutoFoco: true })
  andar(m.elemento, elemento => elemento.type === 'span')[0].props.ref.current = { parentElement: ancora }
  try {
    m.efeitos()
    const camada = () => andar(m.renderizar(), elemento => elemento.props.style?.position === 'fixed')[0]
    assert.equal(camada().props.style.top, 300)
    const leiturasIniciais = medicoes
    topo = 180
    for (let i = 0; i < 8; i++) emitir('scroll', { target: ancora })
    assert.equal(medicoes, leiturasIniciais, 'a rajada espera um quadro antes de medir')
    passarQuadro()
    assert.equal(camada().props.style.top, 180, 'a âncora rolou 120 px; o portal precisa acompanhá-la')
    assert.equal(medicoes, leiturasIniciais + 1)
    esquerda = 72; largura = 320
    emitir('resize'); passarQuadro()
    assert.equal(camada().props.style.left, 72)
    assert.equal(camada().props.style.width, 320)
    emitir('scroll', { target: ancora })
    assert.equal(quadros.size, 1)
    m.desmontar()
    assert.equal(quadros.size, 0)
    assert.equal([...ouvintes.values()].reduce((n, itens) => n + itens.size, 0), 0)
  } finally {
    m.desmontar()
    delete window.requestAnimationFrame; delete window.cancelAnimationFrame
    delete document.body
  }
})

provar('Respostas: opção ativa e busca compartilham IDs e só a lista rola para revelar o destaque', () => {
  const itens = [{ id: 'r1', titulo: 'Primeira', texto: 'Oi' }, { id: 'r2', titulo: 'Segunda', texto: 'Olá' }]
  for (const [top, bottom, esperado] of [[80, 120, 30], [260, 320, 70], [180, 220, 50]]) {
    const m = montar(SeletorRespostas, { idLista: 'respostas', seletor: { itens, destaque: 1, busca: '', pelaBarra: false } })
    const opcao = andar(m.elemento, elemento => elemento.props.role === 'option' && elemento.props['aria-selected'])[0]
    const caixa = { scrollTop: 50, getBoundingClientRect: () => ({ top: 100, bottom: 300 }) }
    opcao.props.ref.current = { closest: () => caixa, getBoundingClientRect: () => ({ top, bottom }) }
    const campo = andar(m.elemento, elemento => elemento.type === 'input')[0]
    assert.equal(campo.props.role, 'combobox')
    assert.equal(campo.props['aria-controls'], 'respostas')
    assert.equal(campo.props['aria-activedescendant'], opcao.props.id)
    document.activeElement = origem
    m.efeitos()
    assert.equal(caixa.scrollTop, esperado)
    assert.equal(document.activeElement, origem, 'revelar opção não transfere foco do campo')
    m.desmontar()
  }
})

console.log(JSON.stringify({ aprovados, falhas: falhas.length }))
if (falhas.length) process.exitCode = 1
