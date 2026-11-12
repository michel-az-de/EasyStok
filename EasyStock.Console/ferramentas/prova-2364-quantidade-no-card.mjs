/* eslint-disable no-console */
// Prova da #2364 (revisão de UX da Produção, item 4). Na Produção do dia, 30 lasanhas eram 30 toques:
//   - tocar no contador do cartão (ou segurar o cartão) abre o campo com a quantidade atual;
//   - confirmar troca o valor de uma vez; 0 tira o prato do lote;
//   - porção fracionária ou acima do teto do lote (9.999, o da API) é recusada com mensagem;
//   - o toque simples continua somando 1.
//   Tela real compilada com hooks de mentira (harness da prova-1624). Toque real pede navegador.
//
//   node ferramentas/prova-2364-quantidade-no-card.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import { transformWithOxc } from 'vite'

const telasCompiladas = new Map()
for (const caminho of ['features/gestao/producao/AbaProducaoDoDiaApi']) {
  const url = new URL(`../src/${caminho}.jsx`, import.meta.url)
  telasCompiladas.set(url.href, (await transformWithOxc(readFileSync(url, 'utf8'), url.pathname, { jsx: { runtime: 'automatic' } })).code)
}

// Hooks de mentira: o estado mora no `host` do componente que está renderizando.
let host = null
const hooks = {
  useState(valor) {
    const atual = host, i = atual.i++
    if (!(i in atual.valores)) atual.valores[i] = typeof valor === 'function' ? valor() : valor
    return [atual.valores[i], (novo) => { atual.valores[i] = typeof novo === 'function' ? novo(atual.valores[i]) : novo }]
  },
  useRef(valor) { return host.valores[host.i++] ??= { current: valor } },
  useId() { return host.valores[host.i++] ??= `prova-${host.i}` },
  useMemo(fn, deps) {
    const atual = host, i = atual.i++, antes = atual.valores[i]
    if (!antes || deps.some((d, j) => !Object.is(d, antes.deps[j]))) atual.valores[i] = { valor: fn(), deps }
    return atual.valores[i].valor
  },
  useCallback: (fn, deps) => hooks.useMemo(() => fn, deps),
  useEffect(fn, deps) {
    const atual = host, i = atual.i++, antes = atual.valores[i]
    if (antes && deps && !deps.some((d, j) => !Object.is(d, antes.deps[j]))) return
    atual.valores[i] = { deps, limpar: antes?.limpar }
    atual.efeitos.push(() => { antes?.limpar?.(); atual.valores[i].limpar = fn() })
  },
}
const AGORA = Date.parse('2026-10-11T15:00:00-03:00')
const gerente = () => ({ acoes: { gerenciarCaixa: true, controlarLoja: true }, permite: () => true })
const acoesDoCaixa = () => ({ abrirCaixa() {}, fecharCaixa() {}, lancarMovimentoCaixa() {}, estornarMovimentoCaixa() {} })
const g = globalThis.__prova2364 = { hooks, catalogo: { cardapio: [] }, caixa: null, acoes: acoesDoCaixa(), acesso: gerente(), agora: AGORA }
const DE_FORA = 'globalThis.__prova2364'
const inline = (source) => ({ format: 'module', shortCircuit: true, source })

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador === 'react') return { url: `data:text/javascript,export const {useState,useRef,useId,useMemo,useCallback,useEffect}=${DE_FORA}.hooks`, shortCircuit: true }
    if (especificador === 'react/jsx-runtime') return { url: 'data:text/javascript,export const jsx=(type,props,key)=>({type,props,key});export const jsxs=jsx;export const Fragment="fragment"', shortCircuit: true }
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(especificador + ext, contexto) } catch { /* próxima extensão */ } }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (telasCompiladas.has(url)) return inline(telasCompiladas.get(url))
    if (url.endsWith('/infra/fonteDados.js')) return inline("export const FONTE_API = true\nexport const API_BASE = ''\n")
    if (url.endsWith('/aplicacao/contextos.js')) return inline(`export const useCatalogo=()=>${DE_FORA}.catalogo;export const useAcoes=()=>${DE_FORA}.acoes;export const useAtendimento=()=>({agora:${DE_FORA}.agora})`)
    if (url.includes('/aplicacao/acessoModulos.')) return inline(`export const useAcessoModulos=()=>${DE_FORA}.acesso`)
    if (url.endsWith('/aplicacao/useCaixaDoDiaApi.js')) return inline(`export const useCaixaDoDiaApi=()=>${DE_FORA}.caixa`)
    if (url.endsWith('/aplicacao/useDevolucoesPendentes.js')) return inline("export const useDevolucoesPendentes=()=>({estado:'ok',total:0,pedidos:0,falhas:0})")
    if (url.endsWith('.css')) return inline('export default new Proxy({},{get:(_,p)=>String(p)})')
    if (url.endsWith('.json')) return inline('export default ' + readFileSync(new URL(url), 'utf8'))
    // Componente de fora das telas (Botao, Chip, CampoTexto) vira o próprio nome: aparece na árvore sem rodar.
    if (url.endsWith('.jsx')) {
      const nomes = [...readFileSync(new URL(url), 'utf8').matchAll(/export (?:function|const) (\w+)/g)].map((m) => m[1])
      return inline(nomes.map((n) => `export const ${n}='${n}'`).join('\n'))
    }
    return proximo(url, contexto)
  },
})

let passou = 0
const falhas = []
// Cada cenário começa limpo: as telas montadas no anterior são desmontadas (a limpeza dos efeitos
// tira os ouvintes do módulo, como a troca de rota) e o acesso volta ao de gerente. Sem isso uma
// tela esquecida continuava relendo o estoque e inflava a contagem do cenário seguinte.
const telasAbertas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  } finally {
    for (const tela of telasAbertas.splice(0)) tela.desmontar()
    g.acesso = gerente()
    g.acoes = acoesDoCaixa()
  }
}

// Cada rota é [teste(metodo, url), resposta ou função que devolve a resposta].
function montarFetch(rotas = []) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body, headers = {} } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null, chave: headers['Idempotency-Key'] ?? null })
    for (const [teste, resposta] of rotas) {
      if (!teste(method, url)) continue
      const { status = 200, data = null, error } = typeof resposta === 'function' ? resposta(url) : resposta
      return new Response(JSON.stringify(error ? { error } : { data }), { status })
    }
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  return chamadas
}

// Mini-renderizador da prova-1585: componente-função guarda estado pelo caminho na árvore.
function montar(componente, props = {}) {
  const hosts = new Map()
  const render = (el, caminho) => {
    if (el == null || typeof el === 'boolean') return null
    if (typeof el !== 'object') return String(el)
    if (Array.isArray(el)) return el.map((filho, i) => render(filho, `${caminho}.${filho?.key ?? i}`))
    if (typeof el.type === 'function') {
      const h = hosts.get(caminho) ?? { valores: [] }
      hosts.set(caminho, h)
      host = h; h.i = 0; h.efeitos = []
      const saida = el.type(el.props)
      const efeitos = h.efeitos
      const arvore = render(saida, caminho + '>')
      for (const efeito of efeitos) efeito()
      return arvore
    }
    const { children, ...resto } = el.props ?? {}
    return { type: el.type, props: { ...resto, children: render(children, caminho + '/') } }
  }
  const t = {
    arvore: null,
    renderizar() { t.arvore = render({ type: componente, props }, 'raiz'); return t.arvore },
    async assentar() {
      for (let i = 0; i < 6; i += 1) { t.renderizar(); await new Promise((r) => setTimeout(r, 5)) }
      return t.renderizar()
    },
    // Trocar de rota desmonta a tela: a limpeza de cada efeito roda e o estado some.
    desmontar() {
      for (const h of hosts.values()) for (const v of h.valores) if (typeof v?.limpar === 'function') v.limpar()
      hosts.clear()
    },
  }
  telasAbertas.push(t)
  return t
}
const esperar = (ms) => new Promise((r) => setTimeout(r, ms))
const andar = (no, fn, achados = []) => {
  if (no == null || typeof no !== 'object') return achados
  if (Array.isArray(no)) { for (const filho of no) andar(filho, fn, achados); return achados }
  if (fn(no)) achados.push(no)
  andar(no.props?.rodape, fn, achados)
  return andar(no.props?.children, fn, achados)
}
const texto = (no) => (no == null ? '' : typeof no !== 'object' ? String(no) : Array.isArray(no) ? no.map(texto).join('') : texto(no.props?.children))
const limpo = (no) => texto(no).replace(/\u00a0/g, ' ')
const doTipo = (no, tipo) => andar(no, (n) => n.type === tipo)
const comRotulo = (no, rotulo) => andar(no, (n) => n.props?.['aria-label'] === rotulo)
const campos = (no, rotulo) => andar(no, (n) => n.type === 'CampoTexto' && n.props.rotulo === rotulo)
const evento = { preventDefault() {} }
const digitar = (valor) => ({ target: { value: valor } })

// === Produção do dia ====================================================================
const CARDAPIO = [
  { sku: 'i-1', nome: 'Talharim', porcao: '500 g', categoria: 'Massas', porcoes: [] },
  { sku: 'i-2', nome: 'Lasanha', porcao: '', categoria: 'Massas', porcoes: [
    { id: 'v-6', rotulo: '600 g', peso: '600 g', padrao: true }, { id: 'v-8', rotulo: 'Família', peso: '' },
  ] },
  { sku: 'i-3', nome: 'Molho sugo', porcao: '300 g', categoria: 'Molhos', porcoes: [] },
]
const ESTOQUE = { pratos: [
  { cardapioItemId: 'i-1', nome: 'Talharim', saldo: 4, descoberto: 0, fisico: 4, lotes: [], porcoes: [] },
  { cardapioItemId: 'i-2', nome: 'Lasanha', saldo: 2, descoberto: 0, fisico: 2, lotes: [], porcoes: [{ variacaoId: 'v-6', rotulo: '600 g', saldo: 2 }] },
], alertas: [] }
// O nome da sugestão vem do estoque, em minúsculas; a tela mostra o do cardápio. O último prato saiu
// do cardápio e continua na sugestão: não pode derrubar a tela nem virar chip.
const SUGESTAO = { ate: '2026-10-12', pratos: [
  { cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'talharim', minimo: 6, saldo: 4, agendados: 4, descoberto: 0, sugestao: 6 },
  { cardapioItemId: 'i-2', produtoId: 'p-2', nome: 'lasanha', minimo: 0, saldo: 2, agendados: 5, descoberto: 0, sugestao: 3 },
  { cardapioItemId: 'i-3', produtoId: 'p-3', nome: 'molho sugo', minimo: 0, saldo: 9, agendados: 0, descoberto: 0, sugestao: 0 },
  { cardapioItemId: 'i-404', produtoId: 'p-404', nome: 'prato que saiu', minimo: 0, saldo: 0, agendados: 9, descoberto: 0, sugestao: 9 },
] }
// A tela recebe também um prato tirado do cardápio: ele não pode aparecer na grade.
g.catalogo = { cardapio: [...CARDAPIO, { sku: 'i-9', nome: 'Prato antigo', porcao: '1 kg', categoria: 'Massas', porcoes: [], removidoEm: '2026-10-01T00:00:00Z' }] }
const lidas = (chamadas, rota) => chamadas.filter((c) => rota(c.metodo, c.url)).length
const LER_ESTOQUE = (m, u) => m === 'GET' && u.endsWith('/producao/estoque-do-dia')
const LER_SUGESTAO = (m, u) => m === 'GET' && u.includes('/producao/sugestao')
const PRODUZIR = (m, u) => m === 'POST' && u.endsWith('/api/atendimento/producao')
const LOTE = { status: 201, data: { loteId: 'l-1', codigoLote: 'LOT-261011-001', totalEtiquetas: 8, pratos: [{ cardapioItemId: 'i-1', nome: 'Talharim', porcoes: 8, sobraG: null }] } }
const rotasDaProducao = () => [[LER_ESTOQUE, { data: ESTOQUE }], [LER_SUGESTAO, { data: SUGESTAO }], [PRODUZIR, LOTE]]

const AbaProducaoDoDiaApi = (await import('../src/features/gestao/producao/AbaProducaoDoDiaApi.jsx')).AbaProducaoDoDiaApi
const { dispensarLote, guardarRascunho, lotesLancados } = await import('../src/aplicacao/producaoDoDia.js')
const zerarProducao = () => { guardarRascunho(null); for (const l of [...lotesLancados()]) dispensarLote(l.loteId) }
const cartoesDaTela = (no) => andar(no, (n) => n.type === 'li' && n.props['data-prato'])
const itensDoLote = (no) => andar(no, (n) => n.type === 'li' && String(n.props.className).includes('itemDoLote'))
const revisar = (t) => {
  if (!doTipo(t.renderizar(), 'Modal').length) doTipo(t.arvore, 'Botao').find((b) => texto(b) === 'Conferir produção').props.onClick()
  return t.renderizar()
}
const tocar = (t, nome) => { comRotulo(t.renderizar(), `Somar 1 porção de ${nome}`)[0].props.onClick(); return t.renderizar() }
const contador = (t, nome) => comRotulo(t.renderizar(), `Digitar a quantidade de ${nome}`)[0]
const modal = (t) => doTipo(t.renderizar(), 'Modal')[0]
const campo = (t) => campos(modal(t), 'Porções')[0]
const usar = (t) => { doTipo(modal(t).props.rodape, 'Botao').find((b) => texto(b) === 'Usar quantidade').props.onClick(); return t.renderizar() }
const digitarNoModal = (t, valor) => { campo(t).props.onChange(digitar(valor)); return t.renderizar() }
const noLote = (t, nome) => {
  const c = comRotulo(t.renderizar(), `Digitar a quantidade de ${nome}`)[0]
  return c ? Number(texto(doTipo(c, 'span')[0])) : 0
}
const abrirTela = async () => {
  zerarProducao()
  montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  return t
}

await confere('quantidade digitada: inteiro passa, vazio, fração e negativo são recusados, 0 tira', async () => {
  const { TETO_PORCOES_POR_LOTE, quantidadeDigitada } = await import('../src/dominio/gradeDeProducao.js')
  assert.equal(TETO_PORCOES_POR_LOTE, 9999, 'o mesmo teto de RegistrarProducaoUseCase.TetoPorcoesPorLote')
  assert.deepEqual(quantidadeDigitada(' 30 ', [], { sku: 'i-1', variacaoId: '' }), { valor: 30 })
  assert.deepEqual(quantidadeDigitada('0', [], { sku: 'i-1', variacaoId: '' }), { valor: 0 })
  for (const ruim of ['', '2,5', '2.5', '-3', 'dez']) {
    assert.ok(quantidadeDigitada(ruim, [], { sku: 'i-1', variacaoId: '' }).erro, `"${ruim}" é recusado`)
  }
  assert.match(quantidadeDigitada('2,5', [], { sku: 'i-1', variacaoId: '' }).erro, /inteir/)
})

await confere('quantidade digitada: o teto conta o resto do lote, não o próprio prato', async () => {
  const { quantidadeDigitada } = await import('../src/dominio/gradeDeProducao.js')
  const linhas = [{ sku: 'i-1', variacaoId: '', porcoes: '40' }, { sku: 'i-3', variacaoId: '', porcoes: '9000' }]
  const talharim = { sku: 'i-1', variacaoId: '' }
  assert.deepEqual(quantidadeDigitada('999', linhas, talharim), { valor: 999 }, '9000 dos outros + 999 = 9.999')
  const acima = quantidadeDigitada('1000', linhas, talharim)
  assert.match(acima.erro, /999/, 'diz quanto ainda cabe')
})

await confere('cartão: digitar 30 no contador põe 30 porções com uma confirmação', async () => {
  const t = await abrirTela()
  assert.equal(contador(t, 'Talharim 500 g'), undefined, 'sem nada no lote o contador não aparece')
  tocar(t, 'Talharim 500 g')
  contador(t, 'Talharim 500 g').props.onClick()
  assert.equal(modal(t).props.titulo, 'Quantas porções de Talharim 500 g?')
  assert.equal(campo(t).props.value, '1', 'o campo abre com a quantidade atual')
  assert.equal(campo(t).props.inputMode, 'numeric')
  digitarNoModal(t, '30')
  usar(t)
  assert.equal(modal(t), undefined, 'confirmar fecha o campo')
  assert.equal(noLote(t, 'Talharim 500 g'), 30)
  assert.match(texto(t.arvore), /No lote: 30 porções · 1 prato/)
})

await confere('cartão: o toque simples continua somando 1 depois de digitar', async () => {
  const t = await abrirTela()
  tocar(t, 'Talharim 500 g')
  contador(t, 'Talharim 500 g').props.onClick()
  digitarNoModal(t, '30')
  usar(t)
  tocar(t, 'Talharim 500 g')
  assert.equal(noLote(t, 'Talharim 500 g'), 31)
})

await confere('cartão: 0 tira o prato do lote', async () => {
  const t = await abrirTela()
  tocar(t, 'Talharim 500 g')
  tocar(t, 'Talharim 500 g')
  contador(t, 'Talharim 500 g').props.onClick()
  digitarNoModal(t, '0')
  usar(t)
  assert.equal(modal(t), undefined)
  assert.equal(contador(t, 'Talharim 500 g'), undefined, 'o prato saiu do lote')
  assert.match(texto(t.arvore), /Lote vazio/)
})

await confere('cartão: fração e valor acima do teto ficam no campo com a mensagem, sem mexer no lote', async () => {
  const t = await abrirTela()
  tocar(t, 'Talharim 500 g')
  contador(t, 'Talharim 500 g').props.onClick()
  digitarNoModal(t, '2,5')
  usar(t)
  assert.ok(modal(t), 'o campo continua aberto')
  assert.match(String(campo(t).props.erro), /inteir/)
  assert.equal(noLote(t, 'Talharim 500 g'), 1)
  digitarNoModal(t, '10000')
  assert.equal(campo(t).props.erro, undefined, 'digitar de novo limpa a mensagem')
  usar(t)
  assert.match(String(campo(t).props.erro), /9999|9\.999/)
  assert.equal(noLote(t, 'Talharim 500 g'), 1)
  doTipo(modal(t).props.rodape, 'Botao').find((b) => texto(b) === 'Cancelar').props.onClick()
  assert.equal(modal(t), undefined)
  assert.equal(noLote(t, 'Talharim 500 g'), 1, 'cancelar não muda nada')
})

await confere('cartão: segurar o cartão abre o campo vazio e o toque que acompanha não soma', async () => {
  const t = await abrirTela()
  const botao = comRotulo(t.renderizar(), 'Somar 1 porção de Talharim 500 g')[0]
  botao.props.onPointerDown({ pointerType: 'touch' })
  await esperar(650)
  assert.ok(modal(t), 'segurou, abriu')
  assert.equal(campo(t).props.value, '', 'prato fora do lote abre vazio')
  comRotulo(t.renderizar(), 'Somar 1 porção de Talharim 500 g')[0].props.onClick()
  assert.equal(noLote(t, 'Talharim 500 g'), 0, 'o clique que fecha o toque longo não soma')
  digitarNoModal(t, '12')
  usar(t)
  assert.equal(noLote(t, 'Talharim 500 g'), 12)
})

await confere('cartão: toque rápido soma 1 e não abre o campo', async () => {
  const t = await abrirTela()
  const botao = comRotulo(t.renderizar(), 'Somar 1 porção de Talharim 500 g')[0]
  botao.props.onPointerDown({ pointerType: 'touch' })
  await esperar(100)
  botao.props.onPointerUp()
  comRotulo(t.renderizar(), 'Somar 1 porção de Talharim 500 g')[0].props.onClick()
  await esperar(550)
  assert.equal(modal(t), undefined)
  assert.equal(noLote(t, 'Talharim 500 g'), 1)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length > 0) process.exit(1)
