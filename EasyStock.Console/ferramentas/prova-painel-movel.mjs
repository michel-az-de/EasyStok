// Executa os handlers reais em um host mínimo de hooks e retângulos controlados.
// Arraste visual, foco nativo e modais são homologados também no navegador.
import assert from 'node:assert/strict'
import { readFileSync, existsSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'
import { guardarPainelMovel, lerPainelMovel, limitarPainelMovel } from '../src/hooks/estadoPainelMovel.js'
import { guardarPosicaoPostit } from '../src/aplicacao/posicoesDosPostits.js'

const memoria = new Map()
const storage = { getItem: (k) => memoria.get(k) ?? null, setItem: (k, v) => memoria.set(k, v), removeItem: (k) => memoria.delete(k) }
globalThis.localStorage = storage
for (let i = 0; i < 120; i++) {
  assert.equal(guardarPainelMovel('conversa-a', `nota-${i}`, { solto: true, x: i, y: i + 2, largura: 280, altura: 240, texto: 'privado', autor: 'privado' }, storage), true)
  assert.deepEqual(lerPainelMovel('conversa-a', `nota-${i}`, storage), { solto: true, x: i, y: i + 2, largura: 280, altura: 240 })
}
assert.equal(memoria.size, 120)
assert.doesNotMatch([...memoria.values()].join(''), /texto|autor|privado/)
assert.equal(lerPainelMovel('conversa-b', 'nota-0', storage), null)
assert.equal(guardarPainelMovel('a', 'b', { solto: true, x: Infinity }, storage), false)
assert.equal(guardarPainelMovel('a', 'b', { solto: true, largura: -2 }, storage), false)
assert.equal(lerPainelMovel('a', 'b', { getItem: () => '{inválido' }), null)
console.log('ok 120 notas independentes persistem somente geometria e estado local')
for (const janela of [{ largura: 1440, altura: 900 }, { largura: 390, altura: 844 }, { largura: 320, altura: 568 }]) {
  const p = limitarPainelMovel({ x: 5000, y: -20 }, { largura: 900, altura: 1200 }, janela)
  assert.ok(p.x >= 8 && p.y >= 8 && p.x + p.largura <= janela.largura - 8 && p.y + p.altura <= janela.altura - 8)
}
console.log('ok posição e dimensões permanecem acessíveis em desktop e celulares')

let hostAtual
const iguais = (a, b) => a?.length === b?.length && a.every((x, i) => Object.is(x, b[i]))
const hooks = {
  createContext(valor) { const contexto = { valor }; contexto.Provider = { contexto }; return contexto },
  useContext(contexto) { return contexto.valor },
  useState(inicial) {
    const h = hostAtual, i = h.indice++
    if (!(i in h.valores)) h.valores[i] = typeof inicial === 'function' ? inicial() : inicial
    return [h.valores[i], (novo) => { h.valores[i] = typeof novo === 'function' ? novo(h.valores[i]) : novo; h.sujo = true }]
  },
  useRef(inicial) { const h = hostAtual, i = h.indice++; return h.valores[i] ??= { current: inicial } },
  useId() { const h = hostAtual, i = h.indice++; return h.valores[i] ??= `painel-${i}` },
  useMemo(fn, deps) { const h = hostAtual, i = h.indice++; if (!iguais(h.valores[i]?.deps, deps)) h.valores[i] = { deps, valor: fn() }; return h.valores[i].valor },
  useEffect(fn, deps) { const h = hostAtual, i = h.indice++; if (!iguais(h.efeitos[i]?.deps, deps)) h.pendentes.push(() => { h.efeitos[i]?.limpar?.(); h.efeitos[i] = { deps, limpar: fn() } }) },
}
hooks.useCallback = (fn, deps) => hooks.useMemo(() => fn, deps)
globalThis.__painelProva = hooks
const compilados = new Map()
for (const caminho of ['componentes/PainelMovel.jsx', 'features/ficha-cliente/NotasDoCliente.jsx', 'features/ficha-cliente/MesaDeNotas.jsx']) {
  const arquivo = new URL('../src/' + caminho, import.meta.url)
  compilados.set(arquivo.href, (await transformWithOxc(readFileSync(arquivo, 'utf8'), arquivo.pathname, { jsx: { runtime: 'automatic' } })).code)
}
const codigo = texto => 'data:text/javascript,' + encodeURIComponent(texto)
registerHooks({
  resolve(spec, ctx, proximo) {
    if (spec.endsWith('/aplicacao/contextos')) return { url: codigo('export const useAtendimento=()=>globalThis.__estadoMesa'), shortCircuit: true }
    if (spec.endsWith('/infra/api/cliente')) return { url: codigo('export const chamarApi=(...args)=>globalThis.__apiMesa(...args)'), shortCircuit: true }
    if (spec.endsWith('/infra/api/sessao')) return { url: codigo('export const identidadeDaSessao=s=>`${s?.empresa?.id??"-"}:${s?.usuario?.id??"-"}`;export const lerSessao=()=>globalThis.__sessaoMesa;export const EVENTO_SESSAO_ALTERADA="sessao-alterada"'), shortCircuit: true }
    if (spec === 'react') return { url: codigo('const h=globalThis.__painelProva; export const createContext=h.createContext,useContext=h.useContext,useState=h.useState,useRef=h.useRef,useId=h.useId,useMemo=h.useMemo,useEffect=h.useEffect,useLayoutEffect=h.useEffect,useCallback=h.useCallback'), shortCircuit: true }
    if (spec === 'react-dom') return { url: codigo('export const createPortal=(elemento)=>elemento'), shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: codigo('export const Fragment="fragment";export const jsx=(type,props,key)=>({type,props,key});export const jsxs=jsx'), shortCircuit: true }
    if (spec.endsWith('/Icone')) return { url: codigo('export function Icone(){return null}'), shortCircuit: true }
    if (spec.endsWith('/Botao')) return { url: codigo('export function Botao(props){return {type:"button",props}}'), shortCircuit: true }
    if (spec.endsWith('.css')) return { url: codigo('export default new Proxy({},{get:(_,p)=>String(p)})'), shortCircuit: true }
    if (spec.startsWith('.') && ctx.parentURL?.startsWith('file:') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) if (existsSync(new URL(spec + ext, ctx.parentURL))) return proximo(spec + ext, ctx)
    }
    return proximo(spec, ctx)
  },
  load(url, ctx, proximo) { return compilados.has(url) ? { format: 'module', source: compilados.get(url), shortCircuit: true } : proximo(url, ctx) },
})
const { PainelMovel, usePainelMovel } = await import('../src/componentes/PainelMovel.jsx')
const listeners = new Map()
globalThis.window = { innerWidth: 1440, innerHeight: 900, matchMedia: () => ({ matches: true }), addEventListener: (nome, fn) => listeners.set(nome, fn), removeEventListener: (nome) => listeners.delete(nome) }
globalThis.document = { body: {} }
const eventosDocumento = new Map()
document.addEventListener = (nome, fn) => eventosDocumento.set(nome, fn)
document.removeEventListener = (nome) => eventosDocumento.delete(nome)
globalThis.ResizeObserver = class { observe() {} disconnect() {} }
function visitar(no, acao) { if (Array.isArray(no)) { no.forEach(n => visitar(n, acao)); return }; if (!no || typeof no !== 'object') return; acao(no); visitar(no.props?.children, acao) }
function montar(fn, props) {
  const h = { valores: [], efeitos: [], props, pendentes: [], sujo: true }
  h.render = () => {
    let limite = 0
    do {
      assert.ok(limite++ < 15, 'sem ciclo de renderização')
      h.sujo = false; h.indice = 0; h.pendentes = []; hostAtual = h
      h.tree = fn(h.props)
      visitar(h.tree, n => {
        if (n.type?.contexto) n.type.contexto.valor = n.props.value
        const alvo = n.props?.ref
        if (alvo && typeof alvo === 'object') alvo.current = {
          focus() {}, getBoundingClientRect: () => ({ width: h.tree.props.style.width, height: h.tree.props.style.height ?? 180 }),
          contains: no => no?.__host === h,
        }
      })
      h.pendentes.forEach(f => f())
    } while (h.sujo)
    return h.tree
  }
  h.find = (rotulo) => { let resultado; visitar(h.tree, n => { if (n.props?.['aria-label'] === rotulo) resultado = n }); assert.ok(resultado, rotulo); return resultado }
  h.unmount = () => h.efeitos.forEach(e => e?.limpar?.())
  h.render(); return h
}
let escopo = 'teste-conversa-a'
const controle = montar(() => usePainelMovel(escopo, 'comanda'), {})
assert.equal(controle.tree.foco, 0, 'painel fechado não pede foco')
controle.tree.soltar({ x: 100, y: 120 }); controle.render()
assert.equal(controle.tree.solto, true)
const portal = PainelMovel({ controle: controle.tree, titulo: 'Comanda', larguraInicial: 360, alturaInicial: 300, children: 'conteúdo real' })
assert.ok(portal.props.focar > 0, 'abertura explícita pede foco no painel')
const primeiraAbertura = portal.props.focar
controle.tree.soltar(); controle.render()
assert.ok(controle.tree.foco > primeiraAbertura, 'abrir painel já solto renova o pedido de foco')
const restaurado = montar(() => usePainelMovel(escopo, 'comanda'), {})
assert.equal(restaurado.tree.solto, true)
assert.equal(restaurado.tree.foco, 0, 'restaurar posição salva não rouba foco')
restaurado.unmount()
console.log('ok abertura explícita pede foco e restauração mantém o foco atual')
const painel = montar(portal.type, portal.props)
const capturas = new Set()
const alvo = { focus() {}, setPointerCapture: id => capturas.add(id), hasPointerCapture: id => capturas.has(id), releasePointerCapture: id => capturas.delete(id) }
const evento = (campos = {}) => ({ pointerType: 'mouse', button: 0, pointerId: 3, clientX: 10, clientY: 20, currentTarget: alvo, preventDefault() {}, stopPropagation() {}, ...campos })
painel.find('Mover Comanda').props.onPointerDown(evento())
painel.find('Mover Comanda').props.onPointerMove(evento({ clientX: 90, clientY: 70 }))
painel.find('Mover Comanda').props.onPointerUp(evento()); painel.render(); controle.render()
assert.equal(controle.tree.x, 180); assert.equal(controle.tree.y, 170); assert.equal(capturas.size, 0)
console.log('ok alça real arrasta com ponteiro e guarda a posição ao soltar')
painel.find('Mover Comanda').props.onFocus()
painel.find('Mover Comanda').props.onKeyDown(evento({ key: 'ArrowRight', shiftKey: true })); painel.render(); controle.render()
assert.equal(controle.tree.x, 204)
painel.find('Mover Comanda').props.onKeyDown(evento({ key: 'Escape' })); painel.render(); controle.render()
assert.equal(controle.tree.x, 180)
console.log('ok teclado move com Shift e Escape desfaz o movimento')
painel.find('Redimensionar Comanda').props.onPointerDown(evento())
painel.find('Redimensionar Comanda').props.onPointerMove(evento({ clientX: 130, clientY: 100 }))
painel.find('Redimensionar Comanda').props.onPointerUp(evento()); painel.render(); controle.render()
assert.equal(controle.tree.largura, 480); assert.equal(controle.tree.altura, 380)
painel.find('Redimensionar Comanda').props.onFocus()
painel.find('Redimensionar Comanda').props.onKeyDown(evento({ key: 'ArrowDown' })); painel.render(); controle.render()
assert.equal(controle.tree.altura, 388)
painel.find('Restaurar posição de Comanda').props.onClick(); painel.render(); controle.render()
assert.equal(controle.tree.largura, 360); assert.equal(controle.tree.altura, 300)
console.log('ok resize real por ponteiro e teclado, com restauração do tamanho')
const zAntes = painel.tree.props.style.zIndex
painel.tree.props.onFocusCapture(); painel.render()
assert.ok(painel.tree.props.style.zIndex > zAntes)
escopo = 'teste-conversa-b'; controle.render(); assert.equal(controle.tree.solto, false)
assert.equal(controle.tree.foco, 0)
escopo = 'teste-conversa-a'; controle.render(); assert.equal(controle.tree.solto, true)
assert.equal(controle.tree.foco, 0, 'retornar à conversa restaura sem repetir o foco')
console.log('ok frente responde ao foco e troca de contexto não herda o painel anterior')
painel.find('Recolher Comanda').props.onClick()
await new Promise(resolve => setTimeout(resolve, 5)); controle.render()
assert.equal(controle.tree.solto, false)
assert.equal(lerPainelMovel('teste-conversa-a', 'comanda').solto, false)
assert.doesNotMatch([...memoria.values()].join(''), /conteúdo real/)
console.log('ok recolher persiste sem conteúdo, com reduced-motion ativo')
const abridor = { isConnected: true, focus() { document.activeElement = this } }
const outroFoco = { isConnected: true, focus() { document.activeElement = this } }
async function provarRetornoDeFoco({ callback, moverFora = false, desmontar = false } = {}) {
  controle.tree.soltar({ currentTarget: abridor }); controle.render()
  const elemento = PainelMovel({ controle: controle.tree, titulo: 'Teste foco', children: 'Conteúdo', aoRecolher: callback })
  const janela = montar(elemento.type, elemento.props)
  document.activeElement = { __host: janela }
  window.matchMedia = () => ({ matches: false })
  janela.find('Recolher Teste foco').props.onClick()
  if (moverFora) { outroFoco.focus(); eventosDocumento.get('focusin')?.({ target: outroFoco }) }
  if (desmontar) { janela.unmount(); outroFoco.focus() }
  await new Promise(resolve => setTimeout(resolve, 165)); controle.render(); janela.unmount()
  window.matchMedia = () => ({ matches: true })
}
await provarRetornoDeFoco(); assert.equal(document.activeElement, abridor)
await provarRetornoDeFoco({ callback: () => outroFoco.focus() }); assert.equal(document.activeElement, outroFoco)
await provarRetornoDeFoco({ moverFora: true }); assert.equal(document.activeElement, outroFoco)
await provarRetornoDeFoco({ desmontar: true }); assert.equal(document.activeElement, outroFoco)
abridor.isConnected = false
const reserva = { isConnected: true, getAttribute: () => controle.tree.chave, focus() { document.activeElement = this } }
document.querySelectorAll = () => [{ ...reserva, getAttribute: () => 'outro-escopo' }, reserva]
controle.tree.devolverFoco(); assert.equal(document.activeElement, reserva)
document.querySelectorAll = () => []
console.log('ok fechar devolve foco, respeita callback/ação externa, reserva e troca de contexto')
const { NotasDoCliente } = await import('../src/features/ficha-cliente/NotasDoCliente.jsx')
const notas = Array.from({ length: 120 }, (_, i) => ({ id: `real-${i}`, texto: `Conteúdo confirmado ${i}`, autor: 'Operador' }))
const notasMontadas = montar(NotasDoCliente, { conversaId: 'muitas-notas', notas })
let areaNotas, abrirNotas
visitar(notasMontadas.tree, n => {
  if (n.props?.id === 'notas-muitas-notas') areaNotas = n
  if (n.type === 'button' && n.props?.['aria-controls'] === 'notas-muitas-notas') abrirNotas = n
})
assert.equal(areaNotas.props.hidden, true, 'notas recolhidas não ocupam a ficha')
abrirNotas.props.onClick(); notasMontadas.render()
visitar(notasMontadas.tree, n => { if (n.props?.id === 'notas-muitas-notas') areaNotas = n })
assert.equal(areaNotas.props.hidden, false, 'o controle revela todas as notas')
const lista = areaNotas.props.children[0]
assert.equal(lista.length, 120)
for (const item of lista) {
  const nota = montar(item.type, item.props)
  let soltar
  visitar(nota.tree, n => { if (n.type === 'button') soltar = n })
  soltar.props.onClick({ currentTarget: { closest: () => ({ getBoundingClientRect: () => ({ left: 500, top: 300 }) }) } })
  nota.render()
  let janela
  visitar(nota.tree, n => { if (n.type === PainelMovel) janela = n })
  assert.equal(janela.props.controle.solto, true)
  assert.equal(janela.props.atributos['data-postit-solto'], item.props.nota.id)
}
assert.equal([...memoria.keys()].filter(k => k.includes('muitas-notas')).length, 120)
assert.doesNotMatch([...memoria.values()].join(''), /Conteúdo confirmado|Operador/)
console.log('ok componente real lista e solta 120 notas confirmadas sem limite de duas')

const { MesaDeNotas } = await import('../src/features/ficha-cliente/MesaDeNotas.jsx')
const { guardarReferenciasDaMesa, lerReferenciasDaMesa } = await import('../src/aplicacao/mesaDeNotas.js')
const clienteId = '11111111-1111-4111-8111-111111111111'
const notaId = '22222222-2222-4222-8222-222222222222'
const sessaoA = { empresa: { id: 'empresa-a' }, usuario: { id: 'usuario-a' } }
globalThis.__sessaoMesa = sessaoA
globalThis.__estadoMesa = { sessao: sessaoA, fonteApi: true, conversas: [], selecionada: { id: 'conversa-a' } }
const escopoA = 'sessao:empresa-a:usuario-a'
const outraReferencia = { clienteId: '33333333-3333-4333-8333-333333333333', notaId: '44444444-4444-4444-8444-444444444444', conversaId: 'conversa-c' }
guardarReferenciasDaMesa(escopoA, [{ clienteId, notaId, conversaId: 'conversa-a', texto: 'não gravar', clienteNome: 'não gravar' }, outraReferencia])
assert.deepEqual(lerReferenciasDaMesa(escopoA), [{ clienteId, notaId, conversaId: 'conversa-a' }, outraReferencia])
const chamadas = []
let responder
globalThis.__apiMesa = (url, opcoes) => { chamadas.push({ url, opcoes }); return new Promise(resolve => { responder = resolve }) }
const elementoMesa = MesaDeNotas({ children: 'ficha da conversa' })
const mesa = montar(elementoMesa.type, elementoMesa.props)
assert.equal(mesa.tree.props.children[1].length, 0, 'não há texto local para exibir antes da resposta privada')
assert.equal(chamadas[0].url, `/api/clientes/${clienteId}/notas/${notaId}`)
assert.equal(chamadas[0].opcoes.identidadeEsperada, 'empresa-a:usuario-a')
assert.equal(chamadas.length, 1, 'reload não busca a referência de outra ficha')
responder({ clienteId, clienteNome: 'Nome atual do cadastro', nota: { id: notaId, texto: 'Somente do servidor', autor: 'Equipe', criadoEm: '2026-10-29T12:00:00Z' } })
await new Promise(resolve => setImmediate(resolve)); mesa.render()
assert.equal(mesa.tree.props.children[1][0].props.registro.clienteNome, 'Nome atual do cadastro')
assert.equal(mesa.tree.props.children[1][0].props.registro.nota.texto, 'Somente do servidor')
mesa.props.conversas = [{ id: 'outra-conversa' }]; mesa.render()
assert.equal(mesa.tree.props.children[1].length, 1, 'poll das conversas preserva a nota selecionada')
assert.equal(chamadas.length, 1, 'poll da conversa não refaz todos os GETs de restauração')
mesa.tree.props.value.soltar(mesa.tree.props.children[1][0].props.registro, { x: 930, y: 399 }); mesa.render()
assert.equal(mesa.tree.props.children[1][0].props.registro.focar, true, 'ação explícita pode focar a nota')
const geometriaMovida = { solto: true, x: 471, y: 157, largura: 347, altura: 224 }
guardarPainelMovel(escopoA, `nota:${clienteId}:${notaId}`, geometriaMovida)
function conferirPainelDaNota(elemento) {
  const notaHost = montar(elemento.type, elemento.props)
  const portal = notaHost.tree.type(notaHost.tree.props)
  const superficie = montar(portal.type, portal.props)
  assert.deepEqual({ x: superficie.tree.props.style.left, y: superficie.tree.props.style.top,
    largura: superficie.tree.props.style.width, altura: superficie.tree.props.style.height },
  { x: 471, y: 157, largura: 347, altura: 224 }, 'NotaNaMesa + hook + superfície conservam geometria salva, sem reaplicar origem930/399')
  superficie.unmount(); notaHost.unmount()
}
conferirPainelDaNota(mesa.tree.props.children[1][0])
const notaCompartilhada = mesa.tree.props.children[1][0].props.registro.nota
function conferirFonteDaNota(conversaId, solta, destacar = false, mesaHost = mesa) {
  // Mesma nota confirmada e mesmo cliente em duas conversas. A lista real usa
  // o contexto da Mesa; só a paginação HTTP, já coberta abaixo, fica desligada.
  const listaHost = montar(NotasDoCliente, { conversaId, clienteId, clienteNome: 'Nome atual do cadastro', notas: [notaCompartilhada] })
  let abrir, lista
  visitar(listaHost.tree, n => { if (n.type === 'button' && n.props?.['aria-controls'] === `notas-${conversaId}`) abrir = n })
  abrir.props.onClick(); listaHost.render()
  visitar(listaHost.tree, n => { if (n.props?.id === `notas-${conversaId}`) lista = n })
  assert.equal(lista.props.hidden, false)
  const elemento = lista.props.children[0].find(n => n.props.nota.id === notaId)
  const fonteHost = montar(elemento.type, elemento.props)
  let botao, texto
  visitar(fonteHost.tree, n => {
    if (n.type === 'button') botao = n
    if (n.type === 'p') texto = n.props.children
  })
  assert.equal(mesaHost.tree.props.value.estaSolta(clienteId, notaId), solta, 'referência solta pertence apenas à conversa selecionada')
  if (solta) {
    assert.equal(botao.props['aria-label'], 'Recolher nota à ficha')
    assert.equal(texto, undefined, 'texto completo fica no post-it desta ficha')
  } else {
    assert.equal(botao.props.children, 'Destacar nota')
    assert.equal(texto, notaCompartilhada.texto, 'outra conversa do mesmo cliente mantém a nota legível na lista')
  }
  if (destacar) botao.props.onClick({ currentTarget: { getBoundingClientRect: () => ({ left: 500, top: 300 }) } })
  fonteHost.unmount(); listaHost.unmount()
}
conferirFonteDaNota('conversa-a', true)
const referenciasAntesDaTroca = lerReferenciasDaMesa(escopoA)
mesa.props.selecionadaId = 'conversa-b'; mesa.render()
assert.equal(mesa.tree.props.children[1].length, 0, 'trocar de ficha esconde as notas anteriores')
conferirFonteDaNota('conversa-b', false)
assert.deepEqual(lerReferenciasDaMesa(escopoA), referenciasAntesDaTroca, 'visitar outra conversa não transfere nem recolhe a nota de A')
assert.deepEqual(lerPainelMovel(escopoA, `nota:${clienteId}:${notaId}`), geometriaMovida)
mesa.props.selecionadaId = 'conversa-a'; mesa.render()
assert.equal(mesa.tree.props.children[1].length, 1, 'voltar à ficha restaura sua nota')
assert.equal(mesa.tree.props.children[1][0].props.registro.focar, false, 'voltar à ficha não repete foco da soltura original')
conferirFonteDaNota('conversa-a', true)
assert.equal(mesa.tree.props.children[1][0].props.registro.conversaId, 'conversa-a', 'A continua dona do post-it ao voltar')
conferirPainelDaNota(mesa.tree.props.children[1][0])
conferirPainelDaNota({ ...mesa.tree.props.children[1][0], props: { ...mesa.tree.props.children[1][0].props,
  registro: { ...mesa.tree.props.children[1][0].props.registro, posicao: undefined, focar: false } } })
assert.deepEqual(lerPainelMovel(escopoA, `nota:${clienteId}:${notaId}`), geometriaMovida)
assert.equal(chamadas.length, 1, 'voltar à ficha já lida não repete GET nem perde posição')
console.log('ok mesa A → B do mesmo cliente → A preserva texto, referência, posição, persistência e foco')
// Transferência acontece só após Destacar nota, nunca por navegar para B.
mesa.props.selecionadaId = 'conversa-b'; mesa.render()
conferirFonteDaNota('conversa-b', false, true); mesa.render()
assert.equal(mesa.tree.props.children[1].length, 1, 'destacar a mesma nota não cria duplicata')
assert.equal(mesa.tree.props.children[1][0].props.registro.conversaId, 'conversa-b')
assert.equal(mesa.tree.props.children[1][0].props.registro.clienteId, clienteId)
assert.equal(lerReferenciasDaMesa(escopoA).find(r => r.notaId === notaId).conversaId, 'conversa-b', 'a transferência explícita persiste B junto da dona em memória')
const reloadB = montar(elementoMesa.type, { ...elementoMesa.props, selecionadaId: 'conversa-b' })
assert.equal(chamadas.length, 2, 'reload lê apenas a nota agora fixada em B')
assert.equal(chamadas[1].url, `/api/clientes/${clienteId}/notas/${notaId}`)
responder({ clienteId, clienteNome: 'Nome atual do cadastro', nota: { id: notaId, texto: 'Somente do servidor', autor: 'Equipe', criadoEm: '2026-10-29T12:00:00Z' } })
await new Promise(resolve => setImmediate(resolve)); reloadB.render()
assert.equal(reloadB.tree.props.children[1].length, 1)
assert.equal(reloadB.tree.props.children[1][0].props.registro.conversaId, 'conversa-b')
assert.equal(reloadB.tree.props.children[1][0].props.registro.focar, false, 'reload não rouba o foco')
conferirPainelDaNota(reloadB.tree.props.children[1][0])
reloadB.props.selecionadaId = 'conversa-a'; reloadB.render()
assert.equal(reloadB.tree.props.children[1].length, 0, 'após transferir, A não exibe o post-it de B')
conferirFonteDaNota('conversa-a', false, false, reloadB)
assert.deepEqual(lerPainelMovel(escopoA, `nota:${clienteId}:${notaId}`), geometriaMovida)
assert.ok(chamadas.every(c => !c.opcoes.metodo || c.opcoes.metodo === 'GET'), 'posição não escreve nem cria nota no servidor')
reloadB.unmount()
mesa.props.selecionadaId = 'conversa-a'; mesa.render()
conferirFonteDaNota('conversa-a', false, true); mesa.render()
assert.equal(lerReferenciasDaMesa(escopoA).find(r => r.notaId === notaId).conversaId, 'conversa-a')
console.log('ok Destacar em B transfere uma única referência, restaura em reload e mantém A legível sem nova nota')
mesa.tree.props.value.soltar({ clienteId, conversaId: 'conversa-a', clienteNome: 'Nome atual do cadastro',
  nota: { id: 'nova-nota-confirmada', texto: 'Segunda nota confirmada', autor: 'Equipe' } }, { x: 200, y: 100 }); mesa.render()
assert.equal(mesa.tree.props.children[1].length, 2)
assert.doesNotMatch([...memoria.values()].join(''), /Somente do servidor|Segunda nota|Nome atual|Equipe|não gravar/)
assert.equal(lerReferenciasDaMesa('sessao:empresa-a:usuario-b').length, 0)
assert.equal(lerReferenciasDaMesa('sessao:empresa-b:usuario-a').length, 0)
console.log('ok mesa grava só IDs e separa preferências de usuários e empresas')
const notaLegada = { id: 'nota-legada-confirmada', texto: 'Texto só do cadastro', autor: 'Equipe' }
guardarPosicaoPostit('conversa-legada', notaLegada.id, { x: 71, y: 83 })
mesa.props.selecionadaId = 'conversa-legada'; mesa.render()
mesa.tree.props.value.atualizar(clienteId, 'Nome atual do cadastro', [notaLegada], 'conversa-legada'); mesa.render()
const painelLegado = `nota:${clienteId}:${notaLegada.id}`
assert.deepEqual(lerPainelMovel(escopoA, painelLegado), { solto: true, x: 71, y: 83 })
assert.equal(mesa.tree.props.value.estaSolta(clienteId, notaLegada.id), true)
guardarPainelMovel(escopoA, painelLegado, { solto: true, x: 300, y: 400 })
mesa.tree.props.value.atualizar(clienteId, 'Nome atual do cadastro', [notaLegada], 'conversa-legada'); mesa.render()
assert.equal(lerPainelMovel(escopoA, painelLegado).x, 300, 'posição nova prevalece sobre coordenadas legadas')
mesa.tree.props.value.recolher(clienteId, notaLegada.id); mesa.render()
mesa.tree.props.value.atualizar(clienteId, 'Nome atual do cadastro', [notaLegada], 'conversa-legada'); mesa.render()
assert.equal(mesa.tree.props.value.estaSolta(clienteId, notaLegada.id), false, 'nota recolhida não volta pela migração')
assert.doesNotMatch([...memoria.values()].join(''), /Texto só do cadastro/)
console.log('ok migração preserva coordenadas antigas, respeita a posição nova e não ressuscita nota recolhida')
globalThis.__sessaoMesa = null
listeners.get('sessao-alterada')(); mesa.render()
assert.equal(mesa.tree.props.children[1], false, 'logout remove todos os portais e dados visíveis')
mesa.unmount()
globalThis.__estadoMesa = { sessao: { empresa: { id: 'empresa-b' }, usuario: { id: 'usuario-b' } }, fonteApi: true, conversas: [] }
const mesaBElemento = MesaDeNotas({ children: 'outra conta' })
const mesaB = montar(mesaBElemento.type, mesaBElemento.props)
assert.equal(mesaB.tree.props.children[1].length, 0)
mesaB.unmount()
console.log('ok logout limpa a mesa e a conta seguinte não herda texto nem portais')
console.log('15 grupos de comportamento do painel e da mesa aprovados.')
