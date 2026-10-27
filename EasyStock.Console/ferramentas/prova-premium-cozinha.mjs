// Handlers reais compilados. Sensores/DOM controlados; gestos e pixels exigem navegador.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'

const fontes = new Map()
for (const nome of ['TelaCozinhaApi', 'ConteudoCozinha', 'CartaoKds', 'CartaoCozinha', 'FeedbackArrasto']) {
  const url = new URL(`../src/features/cozinha/${nome}.jsx`, import.meta.url)
  fontes.set(url.href, (await transformWithOxc(readFileSync(url, 'utf8'), url.pathname,
    { jsx: { runtime: 'automatic' } })).code)
}
let host
const hooks = {
  useState(valor) {
    const atual = host, i = atual.i++
    if (!(i in atual.valores)) atual.valores[i] = typeof valor === 'function' ? valor() : valor
    return [atual.valores[i], novo => { atual.valores[i] = typeof novo === 'function' ? novo(atual.valores[i]) : novo }]
  },
  useRef(valor) { return host.valores[host.i++] ??= { current: valor } },
  useEffect(fn, deps) {
    const i = host.i++, anteriores = host.valores[i]
    if (!anteriores || deps?.some((d, j) => !Object.is(d, anteriores[j]))) host.efeitos.push(fn)
    host.valores[i] = deps
  },
}
globalThis.__premiumCozinha = { hooks, api: null, sobre: null }
const inline = source => ({ format: 'module', shortCircuit: true, source })
registerHooks({
  resolve(spec, ctx, next) {
    if (spec === 'react') return { url: 'data:text/javascript,export const {useState,useRef,useEffect}=globalThis.__premiumCozinha.hooks', shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: 'data:text/javascript,export const jsx=(type,props)=>({type,props});export const jsxs=jsx;export const Fragment="fragment"', shortCircuit: true }
    if (spec === '@dnd-kit/core') return { url: 'data:text/javascript,' + encodeURIComponent(`
      export const DndContext='dnd',DragOverlay='overlay',KeyboardSensor='keyboard',MouseSensor='mouse',TouchSensor='touch';
      export const useSensor=(sensor,options)=>({sensor,options}),useSensors=(...s)=>s;
      export const useDroppable=({id})=>({setNodeRef(){},isOver:id===globalThis.__premiumCozinha.sobre});
      export const useDraggable=()=>({attributes:{},listeners:{},setNodeRef(){},setActivatorNodeRef(){},isDragging:false});
      export const pointerWithin=()=>[],rectIntersection=()=>[];`), shortCircuit: true }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return next(spec + ext, ctx) } catch { /* próxima extensão */ } }
    }
    return next(spec, ctx)
  },
  load(url, ctx, next) {
    if (fontes.has(url)) return inline(fontes.get(url))
    if (url.endsWith('/aplicacao/useCozinhaApi.js')) return inline('export const useCozinhaApi=()=>globalThis.__premiumCozinha.api')
    // #1632: a ação Imprimir dos impressos da API chega à API real; aqui só o id importa.
    if (url.endsWith('/aplicacao/impressos.js')) return inline('export const impressaoDoPedido=pedidoId=>({pedidoId})')
    if (url.endsWith('/hooks/useRelogio.js')) return inline('export const useRelogio=()=>Date.parse("2026-10-11T12:00:00Z")')
    if (url.endsWith('.css')) return inline('export default new Proxy({},{get:(_,p)=>p})')
    if (url.endsWith('.json')) return inline('export default ' + readFileSync(new URL(url), 'utf8'))
    // #2079 (C1): a pergunta do saldo usa o CampoSelecao, que mora em `Campo.jsx` com os outros campos.
    if (url.endsWith('/componentes/Campo.jsx')) return inline("export const CampoSelecao='CampoSelecao',CampoTexto='CampoTexto'")
    if (url.endsWith('.jsx')) {
      const nome = url.split('/').at(-1).slice(0, -4)
      return inline(`export const ${nome}='${nome}'`)
    }
    return next(url, ctx)
  },
})
const { TelaCozinhaApi } = await import('../src/features/cozinha/TelaCozinhaApi.jsx')
const { ConteudoCozinha } = await import('../src/features/cozinha/ConteudoCozinha.jsx')
const { CartaoKds } = await import('../src/features/cozinha/CartaoKds.jsx')
const { CartaoCozinha } = await import('../src/features/cozinha/CartaoCozinha.jsx')
const { FeedbackArrasto } = await import('../src/features/cozinha/FeedbackArrasto.jsx')
const { coordenadasEntreColunas } = await import('../src/features/cozinha/arrastoEntreColunas.js')
globalThis.window = { matchMedia: () => ({ matches: false }) }
const timers = new Map(); let timer = 0
globalThis.setTimeout = fn => { timers.set(++timer, fn); return timer }
globalThis.clearTimeout = id => timers.delete(id)
const body = {}, outroControle = {}
globalThis.document = { body, activeElement: body, querySelector: () => null }
const montar = (componente, props) => {
  const atual = { i: 0, valores: [], efeitos: [] }
  return { props, renderizar() {
    host = atual; atual.i = 0; atual.efeitos = []
    const el = componente(props)
    for (const efeito of atual.efeitos) efeito()
    return el
  } }
}
const andar = (el, fn) => !el || typeof el !== 'object' ? [] : [
  ...(fn(el) ? [el] : []), ...[el.props?.children, el.props?.alca].flat(Infinity).flatMap(c => andar(c, fn)),
]
const dnd = el => andar(el, x => x.type === 'dnd')[0]
const feedback = el => andar(el, x => x.type === FeedbackArrasto)[0].props
const linhas = { casa: { rotulo: 'Preparar em casa' }, servir: { rotulo: 'Para servir' } }
const pedido = { id: 'p1', numeroCurto: '0011', clienteNome: 'Ana', status: 'aguardando', statusRotulo: 'Aguardando', itens: [], atrasado: false, janela: null }
let chamadas = []
const api = globalThis.__premiumCozinha.api = { pedidos: [pedido], erro: null, aoVivo: true, movendo: new Set(), novos: new Set(), mudaram: new Set(), saindo: [], impressoes: [], avancar: (...a) => { chamadas.push(a); return Promise.resolve() }, recarregar() {}, limparErro() {} }
const active = { id: 'p1', data: { current: { numero: '0011', nome: 'Ana', status: 'aguardando' } } }
const novoApi = () => { chamadas = []; Object.assign(api, { pedidos: [pedido], erro: null, movendo: new Set() }); return montar(TelaCozinhaApi, { linhas }) }
let provas = 0
const provar = (nome, fn) => { fn(); provas++; console.log(`ok ${nome}`) }

provar('API conserva mouse 6 px, toque longo 250 ms e teclado com rolagem instantânea', () => {
  const sensores = dnd(novoApi().renderizar()).props.sensors
  assert.equal(sensores[0].options.activationConstraint.distance, 6)
  assert.deepEqual(sensores[1].options.activationConstraint, { delay: 250, tolerance: 5 })
  assert.equal(sensores[2].options.coordinateGetter, coordenadasEntreColunas)
  assert.equal(sensores[2].options.scrollBehavior, 'auto')
})
provar('cancelar e soltar fora da fila não enviam PATCH', () => {
  const t = novoApi(); dnd(t.renderizar()).props.onDragCancel(); assert.match(feedback(t.renderizar()).feedback.texto, /cancelado/)
  dnd(t.renderizar()).props.onDragEnd({ active, over: null }); assert.match(feedback(t.renderizar()).feedback.texto, /ficou em Na fila/)
  assert.equal(chamadas.length, 0)
})
provar('pular etapa é recusado pelo domínio e não envia PATCH', () => {
  const t = novoApi(); dnd(t.renderizar()).props.onDragEnd({ active, over: { id: 'pronto' } })
  assert.equal(chamadas.length, 0); assert.match(feedback(t.renderizar()).feedback.texto, /ficou em Na fila/)
})
provar('pedido pendente não dispara segunda operação', () => {
  const t = novoApi(); api.movendo.add('p1'); dnd(t.renderizar()).props.onDragEnd({ active, over: { id: 'preparando' } })
  assert.equal(chamadas.length, 0)
})
provar('Promise encerrada não anuncia sucesso sem novo estado da API', () => {
  const t = novoApi(); dnd(t.renderizar()).props.onDragEnd({ active, over: { id: 'preparando' } })
  assert.deepEqual(chamadas, [['p1', 'preparando']]); assert.match(feedback(t.renderizar()).feedback.texto, /Aguardando confirmação/)
  assert.equal(feedback(t.renderizar()).feedback.confirmado, undefined)
})
provar('falha de escrita ou recarga não instrui repetir o pedido', () => {
  const t = novoApi(); dnd(t.renderizar()).props.onDragEnd({ active, over: { id: 'preparando' } })
  api.erro = 'A fila não carregou: HTTP 503'; const f = feedback(t.renderizar())
  const saida = FeedbackArrasto(f); assert.equal(saida.type, 'output'); assert.equal(saida.props['aria-live'], 'polite')
  assert.match(saida.props.children[1].props.children, /ainda não confirmada/)
  assert.doesNotMatch(saida.props.children[1].props.children, /tente|repita|confirmado em/i)
})
provar('503 seguido de GET sem o destino conserva a conferência sem espera eterna ou nova escrita', () => {
  const t = novoApi(); dnd(t.renderizar()).props.onDragEnd({ active, over: { id: 'preparando' } })
  api.erro = 'O pedido não mudou: HTTP 503'; t.renderizar(); t.renderizar()
  api.erro = null; api.pedidos = [{ ...pedido }]
  const f = feedback(t.renderizar()), texto = FeedbackArrasto(f).props.children[1].props.children
  assert.equal(f.feedback.requerConferencia, true); assert.equal(f.feedback.confirmado, undefined)
  assert.equal(texto, 'Mudança ainda não confirmada. Confira o estado do pedido.')
  assert.doesNotMatch(texto, /Aguardando|aviso|tente|repita/i); assert.equal(chamadas.length, 1)
})
provar('confirmação eventual do mesmo ID vence a conferência persistente', () => {
  const t = novoApi(); dnd(t.renderizar()).props.onDragEnd({ active, over: { id: 'preparando' } })
  api.erro = 'A fila não carregou: HTTP 503'; t.renderizar(); t.renderizar()
  api.erro = null; api.pedidos = [{ ...pedido, id: 'outro', status: 'preparando' }, pedido]
  assert.equal(feedback(t.renderizar()).feedback.requerConferencia, true)
  api.pedidos = [{ ...pedido, status: 'preparando' }]; t.renderizar()
  const f = feedback(t.renderizar()); assert.equal(f.feedback.confirmado, true)
  assert.equal(f.feedback.requerConferencia, false)
  assert.match(FeedbackArrasto(f).props.children[1].props.children, /0011 confirmado em Em preparo/)
  assert.equal(chamadas.length, 1)
})
provar('gesto novo limpa a conferência anterior e mostra espera só durante a nova operação', () => {
  const t = novoApi(); dnd(t.renderizar()).props.onDragEnd({ active, over: { id: 'preparando' } })
  api.erro = 'O pedido não mudou: HTTP 503'; t.renderizar(); t.renderizar()
  api.erro = null; dnd(t.renderizar()).props.onDragStart({ active, activatorEvent: { type: 'keydown' } })
  assert.equal(feedback(t.renderizar()).feedback.requerConferencia, undefined)
  dnd(t.renderizar()).props.onDragEnd({ active, over: { id: 'preparando' } }); api.movendo.add('p1')
  const f = feedback(t.renderizar()); assert.equal(f.feedback.requerConferencia, undefined)
  assert.match(FeedbackArrasto(f).props.children[1].props.children, /Aguardando confirmação/)
  assert.equal(chamadas.length, 2)
})
provar('confirma só o mesmo pedido no destino solicitado, após leitura', () => {
  const t = novoApi(); dnd(t.renderizar()).props.onDragStart({ active, activatorEvent: { type: 'keydown' } })
  dnd(t.renderizar()).props.onDragEnd({ active, over: { id: 'preparando' } })
  api.pedidos = [pedido, { ...pedido, id: 'outro', status: 'preparando' }]; assert.equal(feedback(t.renderizar()).feedback.confirmado, undefined)
  api.pedidos = [{ ...pedido, status: 'preparando' }]; t.renderizar()
  const f = feedback(t.renderizar()).feedback; assert.equal(f.confirmado, true); assert.equal(f.porTeclado, true)
  assert.match(f.texto, /0011 confirmado em Em preparo/)
})
provar('alvo API expõe destino disponível e recusa sem alterar regras', () => {
  const t = novoApi(); dnd(t.renderizar()).props.onDragStart({ active, activatorEvent: { type: 'mousedown' } })
  const coluna = andar(t.renderizar(), x => typeof x.type === 'function' && x.type.name === 'ColunaKds')
  const alvo = coluna.find(x => x.props.coluna.status === 'preparando')
  assert.equal(alvo.type(alvo.props).props['data-destino-arrasto'], 'disponivel')
  const bloqueado = coluna.find(x => x.props.coluna.status === 'pronto')
  assert.equal(bloqueado.type(bloqueado.props).props['data-destino-arrasto'], undefined)
})
provar('SSE confirma durante POST pendente sem consumir foco da alça desabilitada', () => {
  const t = novoApi()
  dnd(t.renderizar()).props.onDragStart({ active, activatorEvent: { type: 'keydown' } })
  dnd(t.renderizar()).props.onDragEnd({ active, over: { id: 'preparando' } })
  api.movendo.add(pedido.id)
  api.pedidos = [{ ...pedido, status: 'preparando' }]
  t.renderizar()
  let el = t.renderizar()
  assert.equal(feedback(el).feedback.confirmado, true)
  assert.equal(feedback(el).feedback.porTeclado, true)
  assert.equal(andar(el, x => x.type === CartaoKds)[0].props.retomarFoco, false)
  api.movendo.delete(pedido.id)
  el = t.renderizar()
  const cartao = andar(el, x => x.type === CartaoKds)[0]
  assert.equal(cartao.props.retomarFoco, true)
  cartao.props.aoConsumirFoco()
  assert.equal(feedback(t.renderizar()).feedback.porTeclado, false)
})
provar('confirmação persiste e o foco consumido não reaparece com SSE posterior', () => {
  const t = novoApi(); dnd(t.renderizar()).props.onDragStart({ active, activatorEvent: { type: 'keydown' } })
  dnd(t.renderizar()).props.onDragEnd({ active, over: { id: 'preparando' } })
  api.pedidos = [{ ...pedido, status: 'preparando' }]; t.renderizar()
  const cartao = andar(t.renderizar(), x => x.type === CartaoKds)[0]
  assert.equal(cartao.props.retomarFoco, true); cartao.props.aoConsumirFoco()
  api.pedidos = [{ ...pedido, status: 'pronto' }]
  const el = t.renderizar(); assert.equal(feedback(el).feedback.confirmado, true)
  assert.equal(andar(el, x => x.type === CartaoKds)[0].props.retomarFoco, false)
})
const conversa = { id: 'c1', nome: 'Ana', pedido: { numero: 11, estado: 'pago', itens: [], janelaId: null }, mensagens: [], bloqueado: false }
const demoProps = () => ({ conversas: [conversa], janelas: [], cardapio: [], linhas, agora: Date.parse('2026-10-11T12:00:00Z'), acoes: { avancarEsteira: (...a) => chamadas.push(a) } })
const ativoDemo = { id: 'c1', data: { current: { numero: '0011', nome: 'Ana', estado: 'pago', bloqueado: false } } }
provar('demo também aguarda o espelho antes de confirmar destino', () => {
  chamadas = []; const t = montar(ConteudoCozinha, demoProps())
  dnd(t.renderizar()).props.onDragStart({ active: ativoDemo, activatorEvent: { type: 'keydown' } })
  dnd(t.renderizar()).props.onDragEnd({ active: ativoDemo, over: { id: 'preparo' } })
  assert.deepEqual(chamadas, [['c1', 'preparo']]); assert.equal(feedback(t.renderizar()).feedback.confirmado, undefined)
  t.props.conversas = [{ ...conversa, pedido: { ...conversa.pedido, estado: 'preparo' } }]; t.renderizar()
  assert.equal(feedback(t.renderizar()).feedback.confirmado, true)
})
provar('demo consome retorno de foco para Entregue recolhida sem armar montagem futura', () => {
  for (const [contexto, controle, modal, esperado] of [
    ['sem outro foco', body, false, 1], ['controle em uso', outroControle, false, 0], ['modal aberto', body, true, 0],
  ]) {
    chamadas = []
    const props = demoProps()
    props.conversas = [{ ...conversa, pedido: { ...conversa.pedido, estado: 'entrega' } }]
    const t = montar(ConteudoCozinha, props)
    const ativo = { ...ativoDemo, data: { current: { ...ativoDemo.data.current, estado: 'entrega' } } }
    document.activeElement = controle
    document.querySelector = () => modal ? {} : null
    dnd(t.renderizar()).props.onDragStart({ active: ativo, activatorEvent: { type: 'keydown' } })
    dnd(t.renderizar()).props.onDragEnd({ active: ativo, over: { id: 'entregue' } })
    props.conversas = [{ ...conversa, pedido: { ...conversa.pedido, estado: 'entregue' } }]
    t.renderizar()
    const el = t.renderizar()
    assert.equal(feedback(el).feedback.confirmado, true)
    assert.equal(andar(el, x => x.type === CartaoCozinha).length, 0)
    const titulo = andar(el, x => x.type === 'button' && x.props['aria-expanded'] === false)[0]
    assert.equal(typeof titulo.props.ref, 'function')
    let focos = 0
    const no = { focus(opcoes) { assert.deepEqual(opcoes, { preventScroll: true }); focos++; document.activeElement = no } }
    titulo.props.ref(null)
    assert.equal(feedback(t.renderizar()).feedback.porTeclado, true)
    titulo.props.ref(no)
    assert.equal(focos, esperado, contexto)
    const confirmado = t.renderizar()
    assert.equal(feedback(confirmado).feedback.confirmado, true)
    assert.equal(feedback(confirmado).feedback.porTeclado, false, contexto)
    andar(confirmado, x => x.type === 'button' && x.props['aria-expanded'] === false)[0].props.ref(no)
    assert.equal(focos, esperado, contexto + ': intenção consumida não reaparece')
  }
  document.activeElement = body
  document.querySelector = () => null
})
provar('demo mantém pergunta de entregador e não avança antes da escolha', () => {
  chamadas = []; const props = demoProps(); props.conversas = [{ ...conversa, pedido: { ...conversa.pedido, estado: 'embalado' } }]
  const t = montar(ConteudoCozinha, props)
  const ativo = { ...ativoDemo, data: { current: { ...ativoDemo.data.current, estado: 'embalado', precisaDeEntregador: true } } }
  dnd(t.renderizar()).props.onDragEnd({ active: ativo, over: { id: 'entrega' } })
  assert.equal(chamadas.length, 0); assert.match(feedback(t.renderizar()).feedback.texto, /Escolha quem leva/)
})
for (const [nome, componente, props] of [
  ['API', CartaoKds, { pedido, agora: 0, linhas }],
  ['demo', CartaoCozinha, { ...demoProps(), conversa, acoes: {}, destacado: false }],
]) {
  provar(`${nome}: foco retorna após gesto de teclado confirmado sem roubar outro controle`, () => {
    let focos = 0; const t = montar(componente, { ...props, retomarFoco: false })
    const alca = andar(t.renderizar(), x => x.type === 'button' && x.props['aria-label']?.startsWith('Mover'))[0]
    assert.ok(alca); alca.props.ref({ focus: () => { focos++ } })
    document.activeElement = outroControle; t.props.retomarFoco = true; t.renderizar(); assert.equal(focos, 0)
    t.props.retomarFoco = false; t.renderizar(); document.activeElement = body
    t.props.retomarFoco = true; t.renderizar(); assert.equal(focos, 1)
  })
  provar(`${nome}: um modal aberto preserva seu foco e consome a tentativa de retorno`, () => {
    let focos = 0, consumidos = 0
    const t = montar(componente, { ...props, retomarFoco: false, aoConsumirFoco: () => { consumidos++ } })
    const alca = andar(t.renderizar(), x => x.type === 'button' && x.props['aria-label']?.startsWith('Mover'))[0]
    alca.props.ref({ focus: () => { focos++ } })
    document.activeElement = body; document.querySelector = () => ({ open: true })
    t.props.retomarFoco = true; t.renderizar(); assert.equal(focos, 0); assert.equal(consumidos, 1)
    document.querySelector = () => null
  })
}
provar('movimento reduzido não usa animação de volta da API nem da demo', () => {
  window.matchMedia = () => ({ matches: true })
  for (const t of [novoApi(), montar(ConteudoCozinha, demoProps())]) {
    assert.equal(andar(t.renderizar(), x => x.type === 'overlay')[0].props.dropAnimation, null)
  }
})
console.log(`Premium Cozinha: ${provas}/${provas}. DOM e sensores controlados; sem alegação de leitor de tela ou gesto nativo.`)
