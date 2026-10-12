// Handlers e chamadas reais. Fronteiras controladas não comprovam pixels nem saída física.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'

const componente = new URL('../src/componentes/ImprimirPedido.jsx', import.meta.url)
const compilado = (await transformWithOxc(readFileSync(componente, 'utf8'), componente.pathname, { jsx: { runtime: 'automatic' } })).code
let ativo
const mesmas = (a, b) => a?.length === b?.length && a.every((v, i) => Object.is(v, b[i]))
const hooks = {
  useState(inicial) {
    const host = ativo, i = host.indice++
    if (!(i in host.valores)) host.valores[i] = inicial
    return [host.valores[i], v => { const novo = typeof v === 'function' ? v(host.valores[i]) : v; if (!Object.is(novo, host.valores[i])) { host.valores[i] = novo; host.sujo = true } }]
  },
  useRef(inicial) { const host = ativo, i = host.indice++; return host.valores[i] ??= { current: inicial } },
  useEffect(fn, deps) {
    const host = ativo, i = host.indice++, anterior = host.efeitos.get(i)
    if (!mesmas(anterior?.deps, deps)) host.pendentes.push(() => { anterior?.limpar?.(); host.efeitos.set(i, { deps, limpar: fn() }) })
  },
}
globalThis.__bancaImpressao = hooks
const codigo = s => 'data:text/javascript,' + encodeURIComponent(s)
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec === 'react') return { shortCircuit: true, url: codigo('const h=globalThis.__bancaImpressao; export const useState=h.useState,useRef=h.useRef,useEffect=h.useEffect,useLayoutEffect=h.useEffect') }
    if (spec === 'react/jsx-runtime') return { shortCircuit: true, url: codigo('export const Fragment="fragment"; export const jsx=(type,props,key)=>({type,props,key}); export const jsxs=jsx') }
    if (contexto.parentURL === componente.href && spec.startsWith('.')) {
      const nomes = { './Botao': 'Botao', './Campo': 'CampoSelecao', './Modal': 'Modal', './PreviaImpresso': 'PreviaImpresso', './htmlNoNavegador': 'imprimirHtml' }
      return { shortCircuit: true, url: codigo(spec.endsWith('.css') ? 'export default new Proxy({},{get:(_,k)=>String(k)})' : `export function ${nomes[spec]}(){}`) }
    }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return proximo(spec + '.js', contexto)
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (url === componente.href) return { format: 'module', shortCircuit: true, source: compilado }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: 'export const API_BASE=""' }
    if (url.endsWith('/api/sessao.js')) return { format: 'module', shortCircuit: true, source: 'export const lerSessao=()=>null;export const identidadeDaSessao=()=>"-:-";export function atualizarTokens(){};export function empresaDoToken(){};export function gravarSessao(){};export function limparSessao(){}' }
    return proximo(url, contexto)
  },
})
const { ImprimirPedido } = await import(componente.href)
class Tela {
  constructor(props) { this.props = props; this.valores = []; this.efeitos = new Map(); this.renderizar() }
  renderizar() {
    let voltas = 0
    do { assert.ok(++voltas < 12); ativo = this; this.sujo = false; this.indice = 0; this.pendentes = []; this.arvore = ImprimirPedido(this.props); this.pendentes.forEach(fn => fn()) } while (this.sujo)
  }
  async estabilizar() { for (let i = 0; i < 8; i++) { await Promise.resolve(); this.renderizar() } }
  encerrar() { for (const efeito of this.efeitos.values()) efeito.limpar?.() }
}
const nos = a => Array.isArray(a) ? a.flatMap(nos) : !a || typeof a !== 'object' ? [] : [a, ...nos(a.props?.children), ...nos(a.props?.rodape)]
const texto = a => Array.isArray(a) ? a.map(texto).join('') : !a || typeof a !== 'object' ? String(a ?? '') : texto(a.props?.children)
const conteudo = t => nos(t.arvore).map(texto).join('')
const botao = (t, nome) => nos(t.arvore).find(n => n.type?.name === 'Botao' && texto(n) === nome)
const clicar = async (t, nome) => { const b = botao(t, nome); assert.ok(b, nome); assert.ok(!b.props.disabled, nome); await b.props.onClick(); await t.estabilizar() }
const original = { id: 'original-58', documento: 'cozinha', modelo: 'cupom-58', codigo: 'barras', perfil: 'Escolher no diálogo do sistema', criadaEm: '2026-10-12T12:00:00Z', versao: 'anterior', status: 'impressa' }
const props = extra => ({ documentos: [{ valor: 'pedido', rotulo: 'Pedido' }, { valor: 'cozinha', rotulo: 'Canhoto de cozinha' }], papeis: [{ valor: 'a4', rotulo: 'A4' }, { valor: 'cupom-58', rotulo: 'Bobina 58 mm' }], papelPadrao: () => 'a4', preparar: async o => ({ ...o, id: 'nova', html: '<h1>Original guardado</h1>' }), historico: async () => [], confirmar: async () => {}, abrirImpressao: async () => 'dialogo', ...extra })
let passou = 0
const provar = async (nome, fn) => { await fn(); passou++; console.log('ok ' + nome) }
await provar('retentativa preserva o original, todas as opções e o identificador da solicitação', async () => {
  const chamadas = []
  const t = new Tela(props({ historico: async () => [original], preparar: async o => { chamadas.push(o); if (chamadas.length === 1) throw new Error('Sem resposta'); return { ...o, id: 'nova', html: '<h1>Original guardado</h1>' } } }))
  await clicar(t, 'Imprimir'); await clicar(t, 'Reimprimir original')
  assert.ok(botao(t, 'Reimprimir original').props['aria-label'].startsWith('Reimprimir original: Canhoto de cozinha'))
  assert.ok(texto(t.arvore).includes('Canhoto de cozinha · Bobina 58 mm'))
  await clicar(t, 'Tentar preparar novamente')
  assert.deepEqual(chamadas[1], chamadas[0]); assert.equal(chamadas[1].originalId, original.id)
  const previa = nos(t.arvore).find(n => n.type?.name === 'PreviaImpresso')
  assert.equal(previa.props.html, '<h1>Original guardado</h1>'); assert.equal(previa.props.modelo, 'cupom-58')
  t.encerrar()
})
await provar('falha do histórico oferece nova leitura sem perder a prévia', async () => {
  let leituras = 0
  const t = new Tela(props({ historico: async () => { if (++leituras <= 2) throw new Error('Histórico indisponível'); return [original] } }))
  await clicar(t, 'Imprimir'); await clicar(t, 'Preparar prévia')
  const previa = nos(t.arvore).find(n => n.type?.name === 'PreviaImpresso').props.html
  await clicar(t, 'Tentar carregar histórico novamente')
  assert.equal(nos(t.arvore).find(n => n.type?.name === 'PreviaImpresso').props.html, previa)
  assert.ok(botao(t, 'Reimprimir original')); assert.equal(botao(t, 'Tentar carregar histórico novamente'), undefined)
  t.encerrar()
})
await provar('falha ao registrar permite repetir apenas a confirmação, sem imprimir outra via', async () => {
  let impressas = 0, confirmadas = 0, callbacks = 0
  const t = new Tela(props({ abrirImpressao: async () => { impressas++; return 'dialogo' }, confirmar: async () => { if (++confirmadas === 1) throw new Error('Sem conexão') }, aoImprimir: async () => { callbacks++ } }))
  await clicar(t, 'Imprimir'); await clicar(t, 'Preparar prévia'); await clicar(t, 'Abrir impressão'); await clicar(t, 'Sim, o papel saiu')
  assert.equal(callbacks, 0); await clicar(t, 'Registrar novamente')
  assert.equal(impressas, 1); assert.equal(confirmadas, 2); assert.equal(callbacks, 1); assert.ok(conteudo(t).includes('Saída confirmada no histórico'))
  t.encerrar()
})
await provar('ACK confirmado continua confirmado quando só a atualização da lista falha', async () => {
  let confirmadas = 0
  const t = new Tela(props({ confirmar: async () => { confirmadas++ }, aoImprimir: async () => { throw new Error('Lista indisponível') } }))
  await clicar(t, 'Imprimir'); await clicar(t, 'Preparar prévia'); await clicar(t, 'Abrir impressão'); await clicar(t, 'Sim, o papel saiu')
  assert.equal(confirmadas, 1); assert.equal(botao(t, 'Registrar novamente'), undefined); assert.ok(conteudo(t).includes('Saída confirmada no histórico'))
  t.encerrar()
})
await provar('cancelar não confirma e reimprimir não aciona a fila operacional novamente', async () => {
  let confirmadas = 0, callbacks = 0
  const t = new Tela(props({ historico: async () => [original], confirmar: async () => { confirmadas++ }, aoImprimir: async () => { callbacks++ } }))
  await clicar(t, 'Imprimir'); await clicar(t, 'Reimprimir original'); await clicar(t, 'Abrir impressão'); await clicar(t, 'Não saiu / cancelei')
  assert.equal(confirmadas, 0); await clicar(t, 'Abrir impressão'); await clicar(t, 'Sim, o papel saiu')
  assert.equal(confirmadas, 1); assert.equal(callbacks, 0); t.encerrar()
})
const { historicoDoPedido, obterOriginalImpresso } = await import('../src/infra/api/impressosApi.js')
await provar('leituras do histórico e original respeitam 20 segundos mesmo com sinal de fechamento', async () => {
  const timeout = AbortSignal.timeout, fetchOriginal = globalThis.fetch
  const prazos = []
  AbortSignal.timeout = ms => { assert.equal(ms, 20000); const c = new AbortController(); prazos.push(c); return c.signal }
  globalThis.fetch = async (_, { signal }) => new Promise((_, rejeitar) => { signal.addEventListener('abort', () => rejeitar(signal.reason), { once: true }); if (signal.aborted) rejeitar(signal.reason) })
  try {
    for (const ler of [s => historicoDoPedido('pedido', 1, s), s => obterOriginalImpresso('pedido', 'original', s)]) {
      const fechamento = new AbortController(), leitura = ler(fechamento.signal)
      await Promise.resolve(); await Promise.resolve(); prazos.at(-1).abort(new DOMException('Prazo', 'TimeoutError'))
      await assert.rejects(leitura, { codigo: 'SEM_RESPOSTA' }); assert.equal(fechamento.signal.aborted, false)
    }
    const fechamento = new AbortController(), leitura = historicoDoPedido('pedido', 1, fechamento.signal)
    await Promise.resolve(); await Promise.resolve(); fechamento.abort()
    await assert.rejects(leitura, { codigo: 'SEM_CONEXAO' }); assert.equal(prazos.at(-1).signal.aborted, false)
  } finally { AbortSignal.timeout = timeout; globalThis.fetch = fetchOriginal }
})
console.log(`${passou} cenários da banca aprovados.`)
