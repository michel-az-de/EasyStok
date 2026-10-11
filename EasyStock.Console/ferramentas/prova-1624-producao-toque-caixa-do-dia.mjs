/* eslint-disable no-console */
// Prova da #1624 (design sprint com a dona, dia de produção e de encomendas).
//   Produção do dia com o jeito do PWA:
//   - um cartão por prato e por porção, com o saldo; tocar soma 1, "−" tira, zero sai do lote;
//   - "Falta produzir" vem da sugestão do planejamento e um toque põe a quantidade no lote;
//   - validade em um toque e peso da porção já preenchido; o corpo enviado é o de sempre.
//   Caixa do dia:
//   - dia sem abertura mostra o que entrou e saiu, junto do abrir o caixa;
//   - o resumo tem a linha de vendas e as linhas somam o total;
//   - fluxo dos últimos 7 dias lido de `api/caixa/dia?data=`.
//   Telas reais compiladas, com hooks de mentira (como a prova-1585). Pixel e toque pedem navegador.
//
//   node ferramentas/prova-1624-producao-toque-caixa-do-dia.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import { transformWithOxc } from 'vite'

const telasCompiladas = new Map()
for (const caminho of ['features/gestao/producao/AbaProducaoDoDiaApi', 'features/gestao/caixa/AbaCaixaApi']) {
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
const g = globalThis.__prova1624 = { hooks, catalogo: { cardapio: [] }, caixa: null, acoes: {}, acesso: { acoes: {}, permite: () => true }, agora: AGORA }
const DE_FORA = 'globalThis.__prova1624'
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
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
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
  }
  return t
}
const andar = (no, fn, achados = []) => {
  if (no == null || typeof no !== 'object') return achados
  if (Array.isArray(no)) { for (const filho of no) andar(filho, fn, achados); return achados }
  if (fn(no)) achados.push(no)
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
const SUGESTAO = { ate: '2026-10-12', pratos: [
  { cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'Talharim', minimo: 6, saldo: 4, agendados: 4, descoberto: 0, sugestao: 6 },
  { cardapioItemId: 'i-2', produtoId: 'p-2', nome: 'Lasanha', minimo: 0, saldo: 2, agendados: 5, descoberto: 0, sugestao: 3 },
  { cardapioItemId: 'i-3', produtoId: 'p-3', nome: 'Molho sugo', minimo: 0, saldo: 9, agendados: 0, descoberto: 0, sugestao: 0 },
] }
const LER_ESTOQUE = (m, u) => m === 'GET' && u.endsWith('/producao/estoque-do-dia')
const LER_SUGESTAO = (m, u) => m === 'GET' && u.includes('/producao/sugestao')
const PRODUZIR = (m, u) => m === 'POST' && u.endsWith('/api/atendimento/producao')
const LOTE = { status: 201, data: { loteId: 'l-1', codigoLote: 'LOT-261011-001', totalEtiquetas: 8, pratos: [{ cardapioItemId: 'i-1', nome: 'Talharim', porcoes: 8, sobraG: null }] } }
const rotasDaProducao = () => [[LER_ESTOQUE, { data: ESTOQUE }], [LER_SUGESTAO, { data: SUGESTAO }], [PRODUZIR, LOTE]]

await confere('grade: um cartão por prato de tamanho único e um por porção, com o saldo de cada um', async () => {
  const { cartoesDaGrade } = await import('../src/dominio/gradeDeProducao.js')
  const { estoqueDoDiaDaApi } = await import('../src/infra/api/producaoApi.js')
  const cartoes = cartoesDaGrade(CARDAPIO, estoqueDoDiaDaApi(ESTOQUE).pratos)
  assert.deepEqual(cartoes.map((c) => [c.nome, c.tamanho, c.variacaoId, c.saldo]), [
    ['Talharim', '500 g', '', 4], ['Lasanha', '600 g', 'v-6', 2], ['Lasanha', 'Família', 'v-8', 0], ['Molho sugo', '300 g', '', null],
  ])
  assert.equal(new Set(cartoes.map((c) => c.chave)).size, 4, 'cada cartão tem a própria chave')
  // O tamanho só some do cartão quando o nome termina com ele; "Panqueca de Frango" contém "p" e "g".
  const { nomeDoCartao, tamanhoNoCartao } = await import('../src/dominio/gradeDeProducao.js')
  assert.equal(tamanhoNoCartao({ nome: 'Talharim 500 g', tamanho: '500 g' }), '')
  assert.equal(nomeDoCartao({ nome: 'Talharim 500 g', tamanho: '500 g' }), 'Talharim 500 g')
  assert.deepEqual(['P', 'G'].map((tamanho) => nomeDoCartao({ nome: 'Panqueca de Frango', tamanho })), ['Panqueca de Frango P', 'Panqueca de Frango G'])
  assert.equal(nomeDoCartao({ nome: 'Molho 1500 ml', tamanho: '500 ml' }), 'Molho 1500 ml 500 ml')
})

await confere('lote: somar cria a linha com o peso do prato, somar de novo aumenta, zerar tira a linha', async () => {
  const { cartoesDaGrade, definirNoLote, porcoesNoLote, resumoDoLote, somarNoLote } = await import('../src/dominio/gradeDeProducao.js')
  let n = 0
  const apoio = { pratos: CARDAPIO, novaLinha: () => ({ id: `n-${n += 1}`, sku: '', variacaoId: '', porcoes: '', pesoPorPorcaoG: '', pesoRealG: '', validadeDias: '5' }) }
  const [talharim, lasanha600, lasanhaFamilia] = cartoesDaGrade(CARDAPIO, [])
  let linhas = somarNoLote([], talharim, 1, apoio)
  assert.deepEqual(linhas.map((l) => [l.sku, l.variacaoId, l.porcoes, l.pesoPorPorcaoG, l.validadeDias]), [['i-1', '', '1', '500', '5']])
  linhas = somarNoLote(somarNoLote(linhas, talharim, 1, apoio), lasanha600, 1, apoio)
  assert.deepEqual(linhas.map((l) => [l.sku, l.variacaoId, l.porcoes, l.pesoPorPorcaoG]), [['i-1', '', '2', '500'], ['i-2', 'v-6', '1', '600']])
  assert.equal(linhas[0].id, 'n-1', 'a linha que já estava segue a mesma')
  linhas = somarNoLote(linhas, lasanhaFamilia, 1, apoio)
  assert.equal(linhas[2].pesoPorPorcaoG, '', 'porção sem peso escrito não inventa peso')
  assert.deepEqual([porcoesNoLote(linhas, talharim), porcoesNoLote(linhas, lasanha600), porcoesNoLote(linhas, lasanhaFamilia)], [2, 1, 1])
  assert.deepEqual(resumoDoLote(linhas), { pratos: 3, porcoes: 4 })
  linhas = definirNoLote(linhas, talharim, 12, apoio)
  assert.equal(linhas[0].porcoes, '12')
  linhas = somarNoLote(somarNoLote(linhas, lasanha600, -1, apoio), lasanhaFamilia, -5, apoio)
  assert.deepEqual(linhas.map((l) => l.sku), ['i-1'], 'chegou a zero, saiu do lote; abaixo de zero não existe')
  // O mesmo prato em duas linhas (Planejamento e toque): o cartão mostra a soma e o "−" tira de verdade.
  const repetido = [{ ...linhas[0], id: 'a', porcoes: '3' }, { ...linhas[0], id: 'b', porcoes: '2' }]
  assert.equal(porcoesNoLote(repetido, talharim), 5)
  assert.deepEqual(somarNoLote(repetido, talharim, -1, apoio).map((l) => [l.id, l.porcoes]), [['a', '4']])
})

await confere('grade: filtro por categoria e busca sem acento; falta produzir só com sugestão maior que zero', async () => {
  const { cartoesDaGrade, categoriasDaGrade, faltaProduzir, filtrarGrade } = await import('../src/dominio/gradeDeProducao.js')
  const cartoes = cartoesDaGrade(CARDAPIO, [])
  assert.deepEqual(categoriasDaGrade(cartoes), ['Massas', 'Molhos'])
  assert.deepEqual(filtrarGrade(cartoes, { categoria: 'Molhos' }).map((c) => c.nome), ['Molho sugo'])
  assert.deepEqual(filtrarGrade(cartoes, { busca: 'lasanha familia' }).map((c) => c.tamanho), ['Família'])
  const falta = faltaProduzir(SUGESTAO.pratos.map((s) => ({ sku: s.cardapioItemId, nome: s.nome, sugestao: s.sugestao, agendados: s.agendados })), cartoes)
  assert.deepEqual(falta.map((f) => [f.nome, f.quantidade, f.cartao?.chave ?? null]), [['Talharim', 6, cartoes[0].chave], ['Lasanha', 3, null]])
})

const AbaProducaoDoDiaApi = (await import('../src/features/gestao/producao/AbaProducaoDoDiaApi.jsx')).AbaProducaoDoDiaApi
const { dispensarLote, guardarRascunho, lotesLancados } = await import('../src/aplicacao/producaoDoDia.js')
const zerarProducao = () => { guardarRascunho(null); for (const l of [...lotesLancados()]) dispensarLote(l.loteId) }
const cartoesDaTela = (no) => andar(no, (n) => n.type === 'li' && n.props['data-prato'])
const itensDoLote = (no) => andar(no, (n) => n.type === 'li' && String(n.props.className).includes('itemDoLote'))
const tocar = (t, nome) => { comRotulo(t.renderizar(), `Somar 1 porção de ${nome}`)[0].props.onClick(); return t.renderizar() }

await confere('Produção do dia: a tela abre com a grade e o saldo; nada no lote e nada para confirmar', async () => {
  zerarProducao()
  g.catalogo = { cardapio: CARDAPIO }
  montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  assert.equal(cartoesDaTela(t.arvore).length, 4)
  assert.match(texto(cartoesDaTela(t.arvore)[0]), /Talharim.*500 g.*4 no estoque/)
  assert.match(texto(cartoesDaTela(t.arvore)[2]), /Lasanha.*Família.*0 no estoque/)
  assert.doesNotMatch(texto(cartoesDaTela(t.arvore)[3]), /no estoque/, 'prato sem estoque ligado não mostra saldo inventado')
  const botaoDe = (indice) => doTipo(cartoesDaTela(t.arvore)[indice], 'button')[0]
  const descricao = andar(cartoesDaTela(t.arvore)[0], (n) => n.props?.id === botaoDe(0).props['aria-describedby'])[0]
  assert.match(texto(descricao), /4 no estoque/, 'o saldo é a descrição do botão, para o leitor de tela')
  assert.equal(botaoDe(3).props['aria-describedby'], undefined)
  assert.equal(itensDoLote(t.arvore).length, 0)
  assert.equal(doTipo(t.arvore, 'CampoSelecao').filter((c) => c.props.rotulo === 'Prato').length, 0, 'não há mais lista suspensa de pratos')
  assert.equal(doTipo(t.arvore, 'Botao').find((b) => b.props.tipo === 'submit').props.disabled, true)
})

await confere('Produção do dia: tocar no prato soma 1 porção, mostra o saldo depois e "−" tira até sair do lote', async () => {
  zerarProducao()
  montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  tocar(t, 'Talharim 500 g')
  tocar(t, 'Talharim 500 g')
  assert.match(texto(cartoesDaTela(t.arvore)[0]), /4 no estoque.*6 depois/)
  assert.equal(itensDoLote(t.arvore).length, 1)
  assert.match(texto(itensDoLote(t.arvore)[0]), /Talharim/)
  assert.equal(campos(t.arvore, 'Porções')[0].props.value, '2')
  assert.equal(campos(t.arvore, 'Peso de cada porção (g)')[0].props.value, '500', 'o peso do prato já vem preenchido')
  assert.match(limpo(andar(t.arvore, (n) => String(n.props?.className).includes('barraDoLote'))[0]), /2 porções · 1 prato/)
  comRotulo(t.arvore, 'Tirar 1 porção de Talharim 500 g')[0].props.onClick()
  comRotulo(t.renderizar(), 'Tirar 1 porção de Talharim 500 g')[0].props.onClick()
  assert.equal(itensDoLote(t.renderizar()).length, 0)
  assert.equal(comRotulo(t.arvore, 'Tirar 1 porção de Talharim 500 g').length, 0, 'sem porção no lote, não há o que tirar')
})

await confere('Produção do dia: "Falta produzir" põe a quantidade no lote; prato com porções pede o tamanho', async () => {
  zerarProducao()
  montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  const falta = () => andar(t.renderizar(), (n) => n.props?.['aria-label'] === 'Falta produzir')[0]
  assert.deepEqual(doTipo(falta(), 'Chip').map(texto), ['6× Talharim', '3× Lasanha'])
  doTipo(falta(), 'Chip')[0].props.onClick()
  assert.equal(campos(t.renderizar(), 'Porções')[0].props.value, '6')
  doTipo(falta(), 'Chip')[1].props.onClick()
  assert.equal(itensDoLote(t.renderizar()).length, 1, 'a Lasanha não entra sem ela dizer o tamanho')
  assert.deepEqual(cartoesDaTela(t.arvore).map((c) => c.props['data-prato']).length, 2, 'a grade mostra só os tamanhos da Lasanha')
  assert.match(texto(cartoesDaTela(t.arvore)[0]), /Lasanha/)
})

await confere('Produção do dia: validade em um toque; confirmar manda o corpo de sempre, mostra o lote e relê o saldo', async () => {
  zerarProducao()
  const chamadas = montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  for (let i = 0; i < 8; i += 1) tocar(t, 'Talharim 500 g')
  const validade = () => doTipo(itensDoLote(t.renderizar())[0], 'Chip')
  assert.deepEqual(validade().map(texto), ['3 dias', '5 dias', '7 dias', '10 dias', '15 dias'])
  assert.deepEqual(validade().map((c) => c.props.ativo), [false, true, false, false, false])
  validade()[2].props.onClick()
  assert.equal(campos(t.renderizar(), 'Validade (dias)')[0].props.value, '7')
  const leiturasAntes = chamadas.filter((c) => c.metodo === 'GET').length
  await doTipo(t.arvore, 'form')[0].props.onSubmit(evento)
  await t.assentar()
  const envios = chamadas.filter((c) => c.metodo === 'POST')
  assert.deepEqual(envios.map((c) => c.corpo), [{ pratos: [{ cardapioItemId: 'i-1', porcoes: 8, pesoPorPorcaoG: 500, pesoRealG: null, validadeDias: 7 }] }])
  assert.ok(envios[0].chave, 'o envio leva a chave de idempotência')
  assert.match(texto(doTipo(t.arvore, 'output')[0]), /Lote LOT-261011-001 lançado: 8× Talharim\./)
  assert.equal(itensDoLote(t.arvore).length, 0)
  assert.ok(chamadas.filter((c) => c.metodo === 'GET').length >= leiturasAntes + 2, 'saldo e falta produzir são relidos depois do lote')
})

await confere('Produção do dia: sem saldo nem sugestão (API fora) a grade continua servindo', async () => {
  zerarProducao()
  montarFetch([[LER_ESTOQUE, { status: 500, error: { message: 'fora' } }], [LER_SUGESTAO, { status: 403, error: { message: 'sem acesso' } }]])
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  assert.equal(cartoesDaTela(t.arvore).length, 4)
  assert.equal(andar(t.arvore, (n) => n.props?.['aria-label'] === 'Falta produzir').length, 0)
  assert.match(texto(t.arvore), /Não consegui ler o saldo dos pratos agora\./, 'a falha do saldo é dita, com o tentar de novo')
  tocar(t, 'Molho sugo 300 g')
  assert.equal(campos(t.arvore, 'Porções')[0].props.value, '1')
})

// === Caixa do dia =======================================================================
const moedaDe = (v) => v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }).replace(/\u00a0/g, ' ')
const dia = (extra = {}) => ({
  data: '2026-10-11', aberto: false, fechado: false, esquecidoAberto: false, abertoDesde: null, fechamento: null,
  saldoInicial: 0, totalVendas: 0, totalPagamentosPedidos: 0, totalEntradasExtras: 0, totalSaidasExtras: 0, saldoEsperado: 0,
  movimentos: [], linhasExtras: [], ...extra,
})
const caixaCom = (doDia) => ({ estado: 'ok', erro: null, fechamentos: [], recarregar: async () => {}, dia: doDia })
const DIA_DA_API = (m, u) => m === 'GET' && u.includes('/api/caixa/dia')

await confere('fluxo: entrou soma vendas, pagamentos e entradas; saiu são as saídas; o saldo é a diferença', async () => {
  const { linhaDoFluxo, linhasDoFluxo, totaisDoFluxo } = await import('../src/dominio/caixa.js')
  const linha = linhaDoFluxo(dia({ data: '2026-10-09', fechado: true, saldoInicial: 100, totalVendas: 80, totalPagamentosPedidos: 310.5, totalEntradasExtras: 20, totalSaidasExtras: 45 }))
  assert.deepEqual(linha, { data: '2026-10-09', entrou: 410.5, saiu: 45, saldo: 365.5, situacao: 'fechado' })
  assert.equal(linhaDoFluxo(dia({ aberto: true, totalEntradasExtras: 10 })).situacao, 'aberto')
  assert.equal(linhaDoFluxo(dia({ totalPagamentosPedidos: 59.9 })).situacao, 'sem-abertura')
  assert.equal(linhaDoFluxo(dia()).situacao, 'sem-movimento')
  assert.deepEqual(totaisDoFluxo([linha, linhaDoFluxo(dia({ totalPagamentosPedidos: 59.9, totalSaidasExtras: 9.9 }))]), { entrou: 470.4, saiu: 54.9, saldo: 415.5 })
  // Caixa esquecido aberto desde 09/10: hoje já soma a sessão inteira, então 09 e 10 não repetem na tabela.
  const anteriores = ['2026-10-10', '2026-10-09', '2026-10-08'].map((data) => dia({ data, totalVendas: 10 }))
  const esquecido = linhasDoFluxo(dia({ aberto: true, esquecidoAberto: true, abertoDesde: '2026-10-09', totalVendas: 30 }), anteriores)
  assert.deepEqual(esquecido.map((l) => [l.data, l.desde, l.entrou]), [['2026-10-11', '2026-10-09', 30], ['2026-10-08', null, 10]])
  assert.deepEqual(linhasDoFluxo(dia({ aberto: true }), anteriores).map((l) => l.data), ['2026-10-11', '2026-10-10', '2026-10-09', '2026-10-08'])
})

const { AbaCaixaApi } = await import('../src/features/gestao/caixa/AbaCaixaApi.jsx')

await confere('Caixa do dia: dia sem abertura mostra o que entrou, com o lançamento, junto do abrir o caixa', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.acesso = { acoes: { gerenciarCaixa: true }, permite: () => true }
  g.acoes = { abrirCaixa() {}, fecharCaixa() {}, lancarMovimentoCaixa() {}, estornarMovimentoCaixa() {} }
  g.caixa = caixaCom(dia({
    totalEntradasExtras: 120.5, totalPagamentosPedidos: 59.9, saldoEsperado: 180.4,
    movimentos: [{ id: 'm-1', tipo: 'entrada', valor: 120.5, categoria: 'Venda no balcão', meio: 'pix', descricao: '', em: '2026-10-11T16:00:00Z' }],
    linhasExtras: [{ em: '2026-10-11T17:00:00Z', tipo: 'pagamento', valor: 59.9, descricao: 'Pagamento de pedido', meio: 'pix', origem: 'Pedido' }],
  }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  const tela = limpo(t.arvore)
  assert.ok(doTipo(t.arvore, 'Botao').some((b) => /Abrir o caixa/.test(texto(b))), 'o abrir o caixa continua na tela')
  assert.match(tela, /O caixa de hoje ainda não foi aberto\./)
  assert.match(tela, new RegExp(`Outras entradas${moedaDe(120.5).replace('$', '\\$')}`))
  assert.match(tela, new RegExp(`Pagamentos de pedidos${moedaDe(59.9).replace('$', '\\$')}`))
  assert.match(tela, /Venda no balcão/)
  assert.match(tela, /Pagamento de pedido/)
})

await confere('Caixa do dia: dia sem abertura e sem movimento diz que nada entrou, sem resumo zerado', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.caixa = caixaCom(dia())
  const t = montar(AbaCaixaApi)
  await t.assentar()
  assert.match(limpo(t.arvore), /Nada entrou nem saiu hoje ainda\./)
  assert.equal(andar(t.arvore, (n) => n.type === 'dl' && String(n.props.className).includes('resumoLista')).length, 0)
})

await confere('Caixa do dia: o resumo tem a linha de vendas e as linhas somam o total', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.caixa = caixaCom(dia({ aberto: true, saldoInicial: 100, totalVendas: 80, totalPagamentosPedidos: 310.5, totalEntradasExtras: 20, totalSaidasExtras: 45, saldoEsperado: 465.5 }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  const resumo = andar(t.arvore, (n) => n.type === 'dl' && String(n.props.className).includes('resumoLista'))[0]
  const linhas = Object.fromEntries(doTipo(resumo, 'div').map((d) => [texto(doTipo(d, 'dt')[0]), limpo(doTipo(d, 'dd')[0])]))
  assert.equal(linhas.Vendas, moedaDe(80))
  const valor = (rotulo) => Number(linhas[rotulo].replace(/[^\d,]/g, '').replace(',', '.'))
  assert.equal(valor('Saldo inicial') + valor('Vendas') + valor('Pagamentos de pedidos') + valor('Outras entradas') - valor('Outras saídas'), valor('Total esperado do dia'))
})

await confere('Caixa do dia: fluxo dos últimos 7 dias lê os 6 dias anteriores e soma com o de hoje', async () => {
  const porDia = {
    '2026-10-10': dia({ data: '2026-10-10', fechado: true, totalPagamentosPedidos: 300, totalSaidasExtras: 50 }),
    '2026-10-08': dia({ data: '2026-10-08', totalVendas: 99.9 }),
  }
  const chamadas = montarFetch([[DIA_DA_API, (url) => { const data = new URL(url, 'http://x').searchParams.get('data'); return { data: porDia[data] ?? dia({ data }) } }]])
  g.caixa = caixaCom(dia({ aberto: true, totalEntradasExtras: 120.5, saldoEsperado: 120.5 }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  assert.deepEqual(chamadas.map((c) => new URL(c.url, 'http://x').searchParams.get('data')).sort(),
    ['2026-10-05', '2026-10-06', '2026-10-07', '2026-10-08', '2026-10-09', '2026-10-10'])
  const fluxo = andar(t.arvore, (n) => n.props?.['aria-label'] === 'Fluxo de caixa dos últimos 7 dias')[0]
  const linhas = doTipo(fluxo, 'tr').map((tr) => [...doTipo(tr, 'th'), ...doTipo(tr, 'td')].map(limpo))
  assert.equal(linhas.length, 9, 'cabeçalho, 7 dias e o total')
  assert.deepEqual(linhas[1].slice(0, 4), ['Hoje, 11/10', moedaDe(120.5), moedaDe(0), moedaDe(120.5)])
  assert.deepEqual(linhas[2].slice(0, 4), ['sáb., 10/10', moedaDe(300), moedaDe(50), moedaDe(250)])
  assert.match(linhas[2][4], /Fechado/)
  assert.match(linhas[4][4], /Sem abertura/)
  assert.match(linhas[3][4], /Sem movimento/)
  assert.deepEqual(linhas[8].slice(1, 4), [moedaDe(520.4), moedaDe(50), moedaDe(470.4)])
})

await confere('Caixa do dia: fluxo que não carrega avisa e deixa tentar de novo, sem derrubar o caixa de hoje', async () => {
  montarFetch([[DIA_DA_API, { status: 500, error: { message: 'Sem conexão com o EasyStok.' } }]])
  g.caixa = caixaCom(dia({ aberto: true, totalEntradasExtras: 10, saldoEsperado: 10 }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  assert.match(limpo(t.arvore), /Na gaveta \(dinheiro\)/)
  assert.match(limpo(t.arvore), /Os dias anteriores não carregaram\./)
  assert.ok(doTipo(t.arvore, 'Botao').some((b) => /Tentar de novo/.test(texto(b))))
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length > 0) process.exit(1)
