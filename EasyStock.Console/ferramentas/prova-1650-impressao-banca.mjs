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
// O gatilho e o primário do modal dizem "Imprimir": o do modal é o último na árvore.
const botao = (t, nome) => nos(t.arvore).filter(n => n.type?.name === 'Botao' && texto(n) === nome).at(-1)
const clicar = async (t, nome) => { const b = botao(t, nome); assert.ok(b, nome); assert.ok(!b.props.disabled, nome); await b.props.onClick(); await t.estabilizar() }
const campo = (t, rotulo) => nos(t.arvore).find(n => n.type?.name === 'CampoSelecao' && n.props.rotulo === rotulo)
const papelNaTela = t => nos(t.arvore).find(n => n.type?.name === 'PreviaImpresso')?.props
const original = { id: 'original-58', documento: 'cozinha', modelo: 'cupom-58', codigo: 'barras', perfil: 'Escolher no diálogo do sistema', criadaEm: '2026-10-12T12:00:00Z', versao: 'anterior', status: 'impressa' }
const props = extra => ({
  documentos: [{ valor: 'pedido', rotulo: 'Pedido' }, { valor: 'cozinha', rotulo: 'Canhoto de cozinha' }],
  papeis: [{ valor: 'a4', rotulo: 'A4' }, { valor: 'cupom-58', rotulo: 'Bobina 58 mm' }], papelPadrao: () => 'a4',
  obterPrevia: async o => `<p>${o.documento}-${o.modelo}-${o.codigo}</p>`,
  preparar: async o => ({ ...o, id: 'nova', html: o.originalId ? '<h1>Original guardado</h1>' : '<h1>x</h1>' }),
  historico: async () => [], confirmar: async () => {}, abrirImpressao: async () => 'dialogo', ...extra,
})
let passou = 0
const provar = async (nome, fn) => { await fn(); passou++; console.log('ok ' + nome) }
await provar('abre com o papel na tela; um clique imprime a prévia e grava a tentativa (#1839)', async () => {
  const preparadas = [], impressas = []
  let confirmadas = 0, callbacks = 0
  const t = new Tela(props({ preparar: async o => { preparadas.push(o); return { ...o, id: 'nova', html: '<h1>x</h1>' } }, abrirImpressao: async html => { impressas.push(html); return 'dialogo' }, confirmar: async () => { confirmadas++ }, aoImprimir: async () => { callbacks++ } }))
  await clicar(t, 'Imprimir')
  assert.equal(papelNaTela(t).html, '<p>pedido-a4-qr</p>'); assert.equal(papelNaTela(t).modelo, 'a4')
  assert.equal(preparadas.length, 0, 'abrir não grava tentativa')
  await clicar(t, 'Imprimir')
  assert.deepEqual(impressas, ['<h1>x</h1>'], 'imprime o documento guardado, o mesmo do histórico')
  assert.equal(preparadas.length, 1); assert.deepEqual([preparadas[0].documento, preparadas[0].modelo, preparadas[0].codigo], ['pedido', 'a4', 'qr'])
  assert.ok(conteudo(t).includes('Saiu certo?')); assert.ok(botao(t, 'Fechar'))
  await clicar(t, 'Sim, saiu')
  assert.equal(confirmadas, 1); assert.equal(callbacks, 1); assert.ok(conteudo(t).includes('Saída registrada no histórico'))
  t.encerrar()
})
await provar('trocar o papel pede outra prévia e a resposta atrasada da anterior não aparece', async () => {
  const pendentes = []
  const t = new Tela(props({ obterPrevia: o => new Promise(ok => pendentes.push(() => ok(`<p>${o.modelo}</p>`))) }))
  await clicar(t, 'Imprimir')
  assert.equal(papelNaTela(t), undefined); assert.ok(conteudo(t).includes('Montando o papel'))
  assert.equal(botao(t, 'Imprimir').props.disabled, true, 'sem papel na tela não imprime')
  campo(t, 'Papel').props.onChange({ target: { value: 'cupom-58' } }); await t.estabilizar()
  pendentes[1](); await t.estabilizar(); pendentes[0](); await t.estabilizar()
  assert.equal(papelNaTela(t).html, '<p>cupom-58</p>')
  t.encerrar()
})
await provar('falha da prévia mostra o motivo e tenta de novo', async () => {
  let chamadas = 0
  const t = new Tela(props({ obterPrevia: async () => { if (++chamadas === 1) throw new Error('API fora'); return '<p>ok</p>' } }))
  await clicar(t, 'Imprimir')
  assert.ok(conteudo(t).includes('API fora'))
  await clicar(t, 'Tentar de novo')
  assert.equal(papelNaTela(t).html, '<p>ok</p>')
  t.encerrar()
})
await provar('reimprimir original mostra o guardado e imprime sem gravar outra tentativa nem acionar a fila', async () => {
  const preparadas = [], impressas = []
  let confirmadas = 0, callbacks = 0
  const t = new Tela(props({ historico: async () => [original], preparar: async o => { preparadas.push(o); return { ...o, id: 'nova', html: '<h1>Original guardado</h1>' } }, abrirImpressao: async html => { impressas.push(html); return 'dialogo' }, confirmar: async () => { confirmadas++ }, aoImprimir: async () => { callbacks++ } }))
  await clicar(t, 'Imprimir'); await clicar(t, 'Reimprimir original')
  assert.ok(botao(t, 'Reimprimir original').props['aria-label'].startsWith('Reimprimir original: Canhoto de cozinha'))
  assert.equal(preparadas[0].originalId, original.id)
  assert.equal(papelNaTela(t).html, '<h1>Original guardado</h1>'); assert.equal(papelNaTela(t).modelo, 'cupom-58')
  assert.ok(texto(t.arvore).includes('Canhoto de cozinha · Bobina 58 mm'))
  await clicar(t, 'Imprimir')
  assert.deepEqual(impressas, ['<h1>Original guardado</h1>']); assert.equal(preparadas.length, 1)
  await clicar(t, 'Sim, saiu')
  assert.equal(confirmadas, 1); assert.equal(callbacks, 0)
  t.encerrar()
})
await provar('falha ao registrar a confirmação repete só a confirmação, sem imprimir outra via', async () => {
  let impressas = 0, confirmadas = 0, callbacks = 0
  const t = new Tela(props({ abrirImpressao: async () => { impressas++; return 'dialogo' }, confirmar: async () => { if (++confirmadas === 1) throw new Error('Sem conexão') }, aoImprimir: async () => { callbacks++ } }))
  await clicar(t, 'Imprimir'); await clicar(t, 'Imprimir'); await clicar(t, 'Sim, saiu')
  assert.equal(callbacks, 0); await clicar(t, 'Registrar novamente')
  assert.equal(impressas, 1); assert.equal(confirmadas, 2); assert.equal(callbacks, 1); assert.ok(conteudo(t).includes('Saída registrada no histórico'))
  t.encerrar()
})
await provar('sem gravar não imprime; tentar de novo repete a mesma solicitação e a próxima impressão é outra', async () => {
  const ids = []
  let impressas = 0
  const t = new Tela(props({ preparar: async o => { ids.push(o.solicitacaoId); if (ids.length === 1) throw new Error('Sem conexão'); return { ...o, id: 'r' + ids.length, html: '<h1>x</h1>' } }, abrirImpressao: async () => { impressas++; return 'dialogo' } }))
  await clicar(t, 'Imprimir'); await clicar(t, 'Imprimir')
  assert.equal(impressas, 0); assert.ok(conteudo(t).includes('Sem conexão'))
  await clicar(t, 'Imprimir')
  assert.equal(impressas, 1); assert.equal(ids[1], ids[0], 'mesma solicitação na repetição')
  await clicar(t, 'Imprimir de novo')
  assert.notEqual(ids[2], ids[1], 'nova impressão, nova solicitação')
  t.encerrar()
})
await provar('a conferência é sempre da última impressão: "Não saiu" e nova impressão não confirmam a anterior', async () => {
  const confirmados = []
  let n = 0
  const t = new Tela(props({ preparar: async o => ({ ...o, id: 'r' + (++n), html: '<h1>x</h1>' }), confirmar: async r => { confirmados.push(r.id) } }))
  await clicar(t, 'Imprimir'); await clicar(t, 'Imprimir'); await clicar(t, 'Não saiu')
  await clicar(t, 'Imprimir'); await clicar(t, 'Sim, saiu')
  assert.deepEqual(confirmados, ['r2'])
  t.encerrar()
})
await provar('a fila anda quando o diálogo abre (aoAbrirImpressao), sem esperar a conferência', async () => {
  const abertos = []
  const t = new Tela(props({ documentoInicial: 'cozinha', aoAbrirImpressao: d => { abertos.push(d) } }))
  await clicar(t, 'Imprimir'); await clicar(t, 'Imprimir')
  assert.deepEqual(abertos, ['cozinha'])
  t.encerrar()
})
await provar('confirmação registrada continua registrada quando só a atualização da lista falha', async () => {
  let confirmadas = 0
  const t = new Tela(props({ confirmar: async () => { confirmadas++ }, aoImprimir: async () => { throw new Error('Lista indisponível') } }))
  await clicar(t, 'Imprimir'); await clicar(t, 'Imprimir'); await clicar(t, 'Sim, saiu')
  assert.equal(confirmadas, 1); assert.equal(botao(t, 'Registrar novamente'), undefined); assert.ok(conteudo(t).includes('Saída registrada no histórico'))
  t.encerrar()
})
await provar('perfil do aparelho por documento escolhe o papel, filtra os papéis e vai ao histórico pelo nome (#1756)', async () => {
  const preparadas = [], previas = []
  const perfis = [{ valor: 'sistema', rotulo: 'Escolher no diálogo do sistema', papeis: ['a4', 'cupom-58'] }, { valor: 'termica-58', rotulo: 'Térmica · bobina 58 mm', papeis: ['cupom-58'] }]
  const t = new Tela(props({ documentoInicial: 'cozinha', perfis, perfilPadrao: d => d === 'cozinha' ? 'termica-58' : 'sistema', papelPadrao: d => d === 'cozinha' ? 'cupom-58' : 'a4', orientacao: p => `Orientação ${p}`, obterPrevia: async o => { previas.push(o); return '<p>x</p>' }, preparar: async o => { preparadas.push(o); return { ...o, id: 'nova', html: '<h1>x</h1>' } } }))
  await clicar(t, 'Imprimir')
  assert.equal(campo(t, 'Perfil de impressão').props.value, 'termica-58')
  assert.deepEqual(campo(t, 'Papel').props.opcoes.map(p => p.valor), ['cupom-58'])
  assert.ok(conteudo(t).includes('Orientação cupom-58'))
  assert.deepEqual([previas[0].documento, previas[0].modelo], ['cozinha', 'cupom-58'])
  campo(t, 'Documento').props.onChange({ target: { value: 'pedido' } }); await t.estabilizar()
  assert.deepEqual([campo(t, 'Perfil de impressão').props.value, campo(t, 'Papel').props.value], ['sistema', 'a4'])
  assert.deepEqual(campo(t, 'Papel').props.opcoes.map(p => p.valor), ['a4', 'cupom-58'])
  campo(t, 'Perfil de impressão').props.onChange({ target: { value: 'termica-58' } }); await t.estabilizar()
  assert.equal(campo(t, 'Papel').props.value, 'cupom-58', 'perfil dedicado puxa o papel dele')
  await clicar(t, 'Imprimir')
  assert.deepEqual([preparadas[0].documento, preparadas[0].modelo, preparadas[0].perfil], ['pedido', 'cupom-58', 'Térmica · bobina 58 mm'])
  t.encerrar()
})
await provar('sem gatilho abre direto no documento pedido e o Fechar devolve à tela que chamou (Ver canhoto)', async () => {
  let fechou = 0
  const t = new Tela(props({ semGatilho: true, documentoInicial: 'cozinha', papelPadrao: d => d === 'cozinha' ? 'cupom-58' : 'a4', aoFechar: () => { fechou++ } }))
  await t.estabilizar()
  assert.equal(papelNaTela(t).html, '<p>cozinha-cupom-58-qr</p>')
  assert.equal(nos(t.arvore).filter(n => n.type?.name === 'Botao' && texto(n) === 'Imprimir').length, 1, 'só o primário do modal')
  await clicar(t, 'Fechar')
  assert.equal(fechou, 1)
  t.encerrar()
})
await provar('resumo sem histórico: não lê histórico, não grava tentativa e não pede conferência (#1839)', async () => {
  let historicos = 0, preparadas = 0
  const impressas = []
  const t = new Tela(props({ semHistorico: true, documentos: [{ valor: 'resumo', rotulo: 'Resumo do atendimento' }], historico: async () => { historicos++; return [] }, preparar: async () => { preparadas++; return null }, abrirImpressao: async html => { impressas.push(html); return 'dialogo' } }))
  await clicar(t, 'Imprimir')
  assert.equal(papelNaTela(t).html, '<p>resumo-a4-qr</p>')
  await clicar(t, 'Imprimir')
  assert.deepEqual(impressas, ['<p>resumo-a4-qr</p>']); assert.equal(preparadas, 0); assert.equal(historicos, 0)
  assert.ok(!conteudo(t).includes('Saiu certo?')); assert.ok(!conteudo(t).includes('Histórico deste pedido'))
  t.encerrar()
})
const { resumoParaImpresso } = await import('../src/dominio/resumoAtendimento.js')
await provar('o corpo do resumo leva o que a folha mostra, com hora local e sem avaliação quando a loja não guarda', async () => {
  const resumo = { numero: 12, encerradoEm: new Date(2026, 9, 15, 9, 58).toISOString(), ate: null, duracaoTexto: '13 d', faixaHorario: '20:09 a 10:00',
    mensagens: { cliente: 10, voce: 13, automatico: 8, total: 31 }, receita: { liquido: 263, estorno: 0, faltaReceber: 0 }, converteu: 'sim',
    vendas: [{ numero: 5, itensTexto: '1x Lasanha', meio: 'Pix', valor: 263, situacao: 'Entregue' }], vendasTotal: 263,
    marcos: [{ em: new Date(2026, 9, 13, 10, 19).toISOString(), rotulo: 'Pago' }], avaliacaoCliente: 'positiva', autoavaliacao: 'bom', anotacao: 'Tarde' }
  const corpo = resumoParaImpresso({ nomeCliente: 'Felipe', canalNome: 'WhatsApp', telefone: '+55 11', resumo })
  assert.deepEqual([corpo.numero, corpo.data, corpo.contato, corpo.converteu, corpo.virouCliente], ['12', '2026-10-15T09:58:00', 'WhatsApp · +55 11', 'Sim', true])
  assert.deepEqual(corpo.vendas[0], { numero: '5', itens: '1x Lasanha', meio: 'Pix', valor: 263, situacao: 'Entregue' })
  assert.deepEqual(corpo.marcos, [{ hora: '10:19', rotulo: 'Pago' }])
  assert.deepEqual([corpo.avaliacaoCliente, corpo.autoavaliacao, corpo.anotacao], ['Positiva', 'Bom', 'Tarde'])
  const semAvaliacao = resumoParaImpresso({ nomeCliente: 'Felipe', resumo, comAvaliacaoEAnotacao: false })
  assert.deepEqual([semAvaliacao.avaliacaoCliente, semAvaliacao.autoavaliacao, semAvaliacao.anotacao, semAvaliacao.contato], [null, null, null, null])
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
