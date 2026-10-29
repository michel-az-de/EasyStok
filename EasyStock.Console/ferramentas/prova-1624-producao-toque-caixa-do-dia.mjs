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
//   Banca de revisão (#1627): envio em voo que sobrevive à troca de tela, ordem do lote pelo
//   cardápio, linha do Planejamento sem porção, campo só com dígitos, limpar com confirmação,
//   impressão depois de sair da tela; no caixa, venda sem forma de pagamento fora do "Pix e cartão",
//   suprimento e sangria fora do fluxo, dia fechado que mudou depois, fluxo que não recarrega à toa.
//   Telas reais compiladas, com hooks de mentira (como a prova-1585). Pixel e toque pedem navegador.
//
//   node ferramentas/prova-1624-producao-toque-caixa-do-dia.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import { transformWithOxc } from 'vite'

const telasCompiladas = new Map()
// #2078: o modal do fechamento e o gesto da loja também rodam de verdade (avisos, foco, Esc, permissão).
for (const caminho of ['features/gestao/producao/AbaProducaoDoDiaApi', 'features/gestao/caixa/AbaCaixaApi', 'features/gestao/caixa/ModalFecharCaixa', 'features/loja/GestoLoja']) {
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
const g = globalThis.__prova1624 = { hooks, catalogo: { cardapio: [] }, caixa: null, acoes: acoesDoCaixa(), acesso: gerente(), agora: AGORA }
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
const revisar = (t) => {
  if (!doTipo(t.renderizar(), 'Modal').length) doTipo(t.arvore, 'Botao').find((b) => texto(b) === 'Conferir produção').props.onClick()
  return t.renderizar()
}
const tocar = (t, nome) => { comRotulo(t.renderizar(), `Somar 1 porção de ${nome}`)[0].props.onClick(); return t.renderizar() }

await confere('Produção do dia: a tela abre com a grade e o saldo; nada no lote e nada para confirmar', async () => {
  zerarProducao()
  const chamadas = montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  assert.deepEqual([lidas(chamadas, LER_ESTOQUE), lidas(chamadas, LER_SUGESTAO)], [1, 1], 'uma leitura de cada ao abrir, por mais que a tela redesenhe')
  assert.equal(cartoesDaTela(t.arvore).length, 4, 'o prato tirado do cardápio não entra na grade')
  assert.equal(doTipo(t.arvore, 'form').length, 0, 'o lote só aparece na revisão explícita')
  assert.match(texto(cartoesDaTela(t.arvore)[0]), /Talharim.*500 g.*4 no estoque/)
  assert.match(texto(cartoesDaTela(t.arvore)[2]), /Lasanha.*Família.*0 no estoque/)
  assert.doesNotMatch(texto(cartoesDaTela(t.arvore)[3]), /no estoque/, 'prato sem estoque ligado não mostra saldo inventado')
  const botaoDe = (indice) => doTipo(cartoesDaTela(t.arvore)[indice], 'button')[0]
  const descricao = andar(cartoesDaTela(t.arvore)[0], (n) => n.props?.id === botaoDe(0).props['aria-describedby'])[0]
  assert.match(texto(descricao), /4 no estoque/, 'o saldo é a descrição do botão, para o leitor de tela')
  assert.equal(botaoDe(3).props['aria-describedby'], undefined)
  assert.equal(itensDoLote(t.arvore).length, 0)
  assert.equal(doTipo(t.arvore, 'CampoSelecao').filter((c) => c.props.rotulo === 'Prato').length, 0, 'não há mais lista suspensa de pratos')
  assert.equal(doTipo(t.arvore, 'Botao').find((b) => texto(b) === 'Conferir produção').props.disabled, true)
})

await confere('Produção do dia: tocar no prato soma 1 porção, mostra o saldo depois e "−" tira até sair do lote', async () => {
  zerarProducao()
  montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  tocar(t, 'Talharim 500 g')
  tocar(t, 'Talharim 500 g')
  assert.match(texto(cartoesDaTela(t.arvore)[0]), /4 no estoque.*6 depois/)
  revisar(t)
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
  revisar(t)
  assert.equal(campos(t.renderizar(), 'Porções')[0].props.value, '6')
  tocar(t, 'Talharim 500 g')
  tocar(t, 'Talharim 500 g')
  doTipo(falta(), 'Chip')[0].props.onClick()
  assert.equal(campos(t.renderizar(), 'Porções')[0].props.value, '8', 'com 8 no lote, tocar no "6×" não rebaixa')
  doTipo(falta(), 'Chip')[1].props.onClick()
  assert.equal(itensDoLote(t.renderizar()).length, 1, 'a Lasanha não entra sem ela dizer o tamanho')
  assert.deepEqual(cartoesDaTela(t.arvore).map((c) => c.props['data-prato']).length, 2, 'a grade mostra só os tamanhos da Lasanha')
  assert.match(texto(cartoesDaTela(t.arvore)[0]), /Lasanha/)
})

await confere('Produção do dia: validade em um toque; confirmar manda o corpo de sempre, mostra o lote e relê o saldo', async () => {
  zerarProducao()
  // A segunda leitura do estoque já traz o lote somado: 4 + 8.
  let leiturasDoEstoque = 0
  const depoisDoLote = { ...ESTOQUE, pratos: [{ ...ESTOQUE.pratos[0], saldo: 12, fisico: 12 }, ESTOQUE.pratos[1]] }
  const chamadas = montarFetch([
    [LER_ESTOQUE, () => ({ data: (leiturasDoEstoque += 1) === 1 ? ESTOQUE : depoisDoLote })], [LER_SUGESTAO, { data: SUGESTAO }], [PRODUZIR, LOTE],
  ])
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  for (let i = 0; i < 8; i += 1) tocar(t, 'Talharim 500 g')
  revisar(t)
  const validade = () => doTipo(itensDoLote(t.renderizar())[0], 'Chip')
  assert.deepEqual(validade().map(texto), ['3 dias', '5 dias', '7 dias', '10 dias', '15 dias'])
  assert.deepEqual(validade().map((c) => c.props.ativo), [false, true, false, false, false])
  validade()[2].props.onClick()
  assert.equal(campos(t.renderizar(), 'Validade (dias)')[0].props.value, '7')
  assert.deepEqual([lidas(chamadas, LER_ESTOQUE), lidas(chamadas, LER_SUGESTAO)], [1, 1])
  await doTipo(t.arvore, 'form')[0].props.onSubmit(evento)
  await t.assentar()
  const envios = chamadas.filter((c) => c.metodo === 'POST')
  assert.deepEqual(envios.map((c) => c.corpo), [{ pratos: [{ cardapioItemId: 'i-1', porcoes: 8, pesoPorPorcaoG: 500, pesoRealG: null, validadeDias: 7 }] }])
  assert.ok(envios[0].chave, 'o envio leva a chave de idempotência')
  assert.match(texto(doTipo(t.arvore, 'output')[0]), /Lote LOT-261011-001 lançado: 8× Talharim\./)
  assert.equal(itensDoLote(t.arvore).length, 0)
  assert.deepEqual([lidas(chamadas, LER_ESTOQUE), lidas(chamadas, LER_SUGESTAO)], [2, 2], 'saldo e falta produzir são relidos uma vez cada depois do lote')
  assert.match(texto(cartoesDaTela(t.arvore)[0]), /12 no estoque/, 'o cartão mostra o saldo novo')
})

await confere('Produção do dia: prato com porções vai com a porção tocada no corpo e o nome dela no lote', async () => {
  zerarProducao()
  const chamadas = montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  tocar(t, 'Lasanha 600 g')
  tocar(t, 'Lasanha Família')
  revisar(t)
  assert.deepEqual(itensDoLote(t.arvore).map((item) => texto(doTipo(item, 'h4')[0])), ['Lasanha 600 g', 'Lasanha Família'])
  await doTipo(t.arvore, 'form')[0].props.onSubmit(evento)
  assert.deepEqual(chamadas.filter((c) => c.metodo === 'POST').map((c) => c.corpo), [{ pratos: [
    { cardapioItemId: 'i-2', variacaoId: 'v-6', porcoes: 1, pesoPorPorcaoG: 600, pesoRealG: null, validadeDias: 5 },
    { cardapioItemId: 'i-2', variacaoId: 'v-8', porcoes: 1, pesoPorPorcaoG: null, pesoRealG: null, validadeDias: 5 },
  ] }])
})

await confere('Produção do dia: o filtro por categoria muda a grade; a linha do Planejamento escolhe a porção no lote', async () => {
  zerarProducao()
  const { levarParaProducao } = await import('../src/aplicacao/planejamento.js')
  assert.equal(levarParaProducao([{ sku: 'i-2', porcoes: '6' }]), 1)
  montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  const filtro = (rotulo) => doTipo(t.renderizar(), 'Chip').find((c) => c.props.papel === 'filtro' && texto(c) === rotulo)
  filtro('Molhos').props.onClick()
  assert.deepEqual(cartoesDaTela(t.renderizar()).map((c) => texto(doTipo(c, 'button')[0])), ['Molho sugo300 g'])
  filtro('Todos').props.onClick()
  assert.equal(cartoesDaTela(t.renderizar()).length, 4)
  revisar(t)
  const item = () => itensDoLote(t.renderizar())[0]
  // #2081 (A11): as porções saíram do resumo para o campo à vista ao lado do nome.
  assert.match(texto(doTipo(item(), 'summary')[0]), /Lasanha.*escolha a porção/)
  assert.equal(campos(item(), 'Porções')[0].props.value, '6')
  doTipo(item(), 'CampoSelecao').find((c) => c.props.rotulo === 'Porção').props.onChange(digitar('v-8'))
  assert.match(texto(doTipo(item(), 'summary')[0]), /Lasanha Família/)
  assert.doesNotMatch(texto(doTipo(item(), 'summary')[0]), /escolha a porção/)
  assert.equal(itensDoLote(t.arvore).length, 1)
})

await confere('Produção do dia: sem saldo nem sugestão (API fora) a grade continua servindo', async () => {
  zerarProducao()
  let estoqueFora = true
  const chamadas = montarFetch([[LER_ESTOQUE, () => (estoqueFora ? { status: 500, error: { message: 'fora' } } : { data: ESTOQUE })], [LER_SUGESTAO, { status: 403, error: { message: 'sem acesso' } }]])
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  assert.equal(cartoesDaTela(t.arvore).length, 4)
  assert.equal(andar(t.arvore, (n) => n.props?.['aria-label'] === 'Falta produzir').length, 0)
  assert.match(texto(t.arvore), /Não foi possível carregar o saldo dos pratos\./, 'a falha do saldo é dita')
  estoqueFora = false
  await doTipo(t.arvore, 'Botao').find((b) => texto(b) === 'Tentar de novo').props.onClick()
  await t.assentar()
  assert.equal(lidas(chamadas, LER_ESTOQUE), 2, 'o tentar de novo relê o estoque')
  assert.match(texto(cartoesDaTela(t.arvore)[0]), /4 no estoque/)
  assert.doesNotMatch(texto(t.arvore), /Não foi possível carregar o saldo/)
  tocar(t, 'Molho sugo 300 g')
  revisar(t)
  assert.equal(campos(t.arvore, 'Porções')[0].props.value, '1')
})

// --- Banca #1627: produção ---------------------------------------------------------------
const novaLinhaDeProva = () => { let n = 0; return () => ({ id: `n-${n += 1}`, sku: '', variacaoId: '', porcoes: '', pesoPorPorcaoG: '', pesoRealG: '', validadeDias: '5' }) }

await confere('lote: a ordem é a do cardápio, não a dos toques (o corpo enviado não muda com a ordem)', async () => {
  const { cartoesDaGrade, somarNoLote } = await import('../src/dominio/gradeDeProducao.js')
  const apoio = { pratos: CARDAPIO, novaLinha: novaLinhaDeProva() }
  const [talharim, lasanha600, , molho] = cartoesDaGrade(CARDAPIO, [])
  const umaOrdem = somarNoLote(somarNoLote(somarNoLote([], molho, 1, apoio), lasanha600, 1, apoio), talharim, 1, apoio)
  const outraOrdem = somarNoLote(somarNoLote(somarNoLote([], talharim, 1, apoio), molho, 1, apoio), lasanha600, 1, apoio)
  assert.deepEqual(umaOrdem.map((l) => [l.sku, l.variacaoId]), [['i-1', ''], ['i-2', 'v-6'], ['i-3', '']])
  assert.deepEqual(outraOrdem.map((l) => [l.sku, l.variacaoId]), umaOrdem.map((l) => [l.sku, l.variacaoId]))
})

await confere('lote: a linha do Planejamento ganha o peso do prato e, sem porção, vira a porção do primeiro toque', async () => {
  const { cartoesDaGrade, linhasIniciais, somarNoLote } = await import('../src/dominio/gradeDeProducao.js')
  const apoio = { pratos: CARDAPIO, novaLinha: novaLinhaDeProva() }
  const [, lasanha600, lasanhaFamilia] = cartoesDaGrade(CARDAPIO, [])
  let linhas = linhasIniciais([{ sku: 'i-2', porcoes: '6' }, { sku: 'i-1', porcoes: '4' }], apoio)
  assert.deepEqual(linhas.map((l) => [l.sku, l.variacaoId, l.porcoes, l.pesoPorPorcaoG, l.validadeDias]),
    [['i-1', '', '4', '500', '5'], ['i-2', '', '6', '', '5']], 'ordem do cardápio; o Talharim planejado já traz 500 g')
  linhas = somarNoLote(linhas, lasanha600, 1, apoio)
  assert.deepEqual(linhas.map((l) => [l.sku, l.variacaoId, l.porcoes, l.pesoPorPorcaoG]), [['i-1', '', '4', '500'], ['i-2', 'v-6', '6', '600']],
    'as 6 planejadas são da porção tocada; não nasce outra linha')
  linhas = somarNoLote(linhas, lasanha600, 1, apoio)
  assert.equal(linhas[1].porcoes, '7')
  linhas = somarNoLote(linhas, lasanhaFamilia, 1, apoio)
  assert.deepEqual(linhas.map((l) => [l.variacaoId, l.porcoes]), [['', '4'], ['v-6', '7'], ['v-8', '1']])
  // Rascunho que ela já digitou não é mexido: peso vazio digitado fica vazio.
  assert.equal(linhasIniciais([{ id: 'a', sku: 'i-1', variacaoId: '', porcoes: '2', pesoPorPorcaoG: '', pesoRealG: '', validadeDias: '7' }], apoio)[0].pesoPorPorcaoG, '')
})

await confere('campo: porções e validade só guardam dígitos, até 4', async () => {
  const { soDigitos } = await import('../src/dominio/gradeDeProducao.js')
  assert.deepEqual(['12a', '-3', 'abc', '0x10', '1e2', ' 7 ', '123456'].map((t) => soDigitos(t)), ['12', '3', '', '010', '12', '7', '1234'])
})

const enviarDaTela = (t) => doTipo(t.arvore, 'Botao').filter((b) => b.props.tipo === 'submit')
const respostaJson = (data, status = 200) => new Response(JSON.stringify({ data }), { status })
// Só o POST da produção fica em voo; as leituras respondem na hora.
function fetchComEnvioEmVoo() {
  const controle = { responder: null }
  globalThis.fetch = (url, { method = 'GET' } = {}) => {
    if (method === 'POST') return new Promise((resolver) => { controle.responder = () => resolver(respostaJson(LOTE.data, 201)) })
    if (LER_ESTOQUE(method, url)) return Promise.resolve(respostaJson(ESTOQUE))
    if (LER_SUGESTAO(method, url)) return Promise.resolve(respostaJson(SUGESTAO))
    return Promise.resolve(respostaJson(null))
  }
  return controle
}

await confere('produção: com um envio em voo, o segundo é recusado sem ir para a API', async () => {
  zerarProducao()
  const { criarProducaoDoDia } = await import('../src/aplicacao/producaoDoDia.js')
  let responder = null
  let envios = 0
  globalThis.fetch = () => { envios += 1; return new Promise((resolver) => { responder = () => resolver(respostaJson(LOTE.data, 201)) }) }
  const producao = criarProducaoDoDia()
  const vistos = []
  const sair = producao.aoMudarEnvio((valor) => vistos.push(valor))
  const linha = (porcoes) => ({ sku: 'i-1', nome: 'Talharim', porcoes, pesoPorPorcaoG: 500, pesoRealG: null, validadeDias: 5 })
  const primeiro = producao.produzir([linha(3)])
  for (let i = 0; i < 50 && !responder; i += 1) await esperar(0)
  assert.equal(producao.enviando(), true)
  // Corpo diferente ganharia chave nova e gravaria outro lote: não sai nem da máquina.
  assert.deepEqual(await producao.produzir([linha(4)]), { ok: false, erro: 'A produção anterior ainda está sendo gravada. Espere um instante.' })
  assert.equal(envios, 1)
  responder()
  assert.equal((await primeiro).ok, true)
  assert.equal(producao.enviando(), false)
  assert.deepEqual(vistos, [false, true, false], 'quem se inscreve recebe o estado de agora e cada mudança')
  sair()
  zerarProducao()
})

await confere('Produção do dia: com o envio em voo, a tela que volta fica travada até a resposta', async () => {
  zerarProducao()
  const controle = fetchComEnvioEmVoo()
  const antes = montar(AbaProducaoDoDiaApi)
  await antes.assentar()
  tocar(antes, 'Talharim 500 g')
  revisar(antes)
  const envio = doTipo(antes.arvore, 'form')[0].props.onSubmit(evento)
  for (let i = 0; i < 50 && !controle.responder; i += 1) await esperar(0)
  antes.desmontar()
  const depois = montar(AbaProducaoDoDiaApi)
  await depois.assentar()
  revisar(depois)
  assert.equal(itensDoLote(depois.arvore).length, 1, 'o lote que está indo continua na tela')
  assert.ok(enviarDaTela(depois).length > 0 && enviarDaTela(depois).every((b) => b.props.disabled === true), 'não dá para confirmar de novo')
  assert.equal(comRotulo(depois.arvore, 'Somar 1 porção de Talharim 500 g')[0].props.disabled, true, 'nem mexer no lote que já foi')
  controle.responder()
  await envio
  await depois.assentar()
  assert.equal(itensDoLote(depois.arvore).length, 0)
  assert.equal(comRotulo(depois.arvore, 'Somar 1 porção de Talharim 500 g')[0].props.disabled, false)
  assert.match(texto(doTipo(depois.arvore, 'output')[0]), /Lote LOT-261011-001 lançado/)
})

await confere('Produção do dia: o aviso de erro some quando ela mexe no lote', async () => {
  zerarProducao()
  guardarRascunho([{ id: 'a', sku: 'i-1', variacaoId: '', porcoes: '', pesoPorPorcaoG: '500', pesoRealG: '', validadeDias: '5' }])
  const chamadas = montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  revisar(t)
  await doTipo(t.arvore, 'form')[0].props.onSubmit(evento)
  const alertas = () => andar(t.renderizar(), (n) => n.props?.role === 'alert')
  assert.match(texto(alertas()[0]), /Prato 01 \(Talharim\): Quantas porções saíram\?/)
  assert.equal(chamadas.filter((c) => c.metodo === 'POST').length, 0)
  tocar(t, 'Talharim 500 g')
  assert.equal(alertas().length, 0, 'ela corrigiu: o aviso antigo não fica na tela')
})

await confere('Produção do dia: o campo de porções ignora o que não é dígito e o toque soma em cima do digitado', async () => {
  zerarProducao()
  montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  tocar(t, 'Talharim 500 g')
  revisar(t)
  campos(t.arvore, 'Porções')[0].props.onChange(digitar('12a'))
  assert.equal(campos(t.renderizar(), 'Porções')[0].props.value, '12')
  tocar(t, 'Talharim 500 g')
  assert.equal(campos(t.arvore, 'Porções')[0].props.value, '13', 'somou em cima do 12, não trocou por 1')
  campos(t.arvore, 'Validade (dias)')[0].props.onChange(digitar('7 dias'))
  assert.equal(campos(t.renderizar(), 'Validade (dias)')[0].props.value, '7')
})

await confere('Produção do dia: Limpar pede confirmação antes de apagar o lote', async () => {
  zerarProducao()
  montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  tocar(t, 'Talharim 500 g')
  tocar(t, 'Molho sugo 300 g')
  revisar(t)
  const botao = (rotulo) => doTipo(t.renderizar(), 'Botao').find((b) => texto(b) === rotulo)
  botao('Limpar').props.onClick()
  assert.equal(itensDoLote(t.renderizar()).length, 2, 'um toque só não apaga')
  assert.match(texto(andar(t.arvore, (n) => String(n.props?.className).includes('barraDoLote'))[0]), /Tirar os 2 pratos do lote\?/)
  botao('Manter o lote').props.onClick()
  assert.equal(itensDoLote(t.renderizar()).length, 2)
  botao('Limpar').props.onClick()
  botao('Sim, limpar').props.onClick()
  assert.equal(itensDoLote(t.renderizar()).length, 0)
})

await confere('Produção do dia: o lote mostra porções, validade e peso de cada prato numa linha, e os campos dizem de qual prato são', async () => {
  zerarProducao()
  montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  tocar(t, 'Talharim 500 g')
  tocar(t, 'Talharim 500 g')
  revisar(t)
  const item = itensDoLote(t.arvore)[0]
  // #2081 (A11): as porções saíram do resumo para o campo à vista ao lado do nome.
  assert.match(texto(doTipo(item, 'summary')[0]), /Talharim 500 g.*validade 5 dias.*500 g cada/)
  assert.equal(campos(item, 'Porções')[0].props.value, '2')
  assert.equal(doTipo(doTipo(item, 'details')[0], 'CampoTexto').some((c) => c.props.rotulo === 'Porções'), false, 'o campo de porções não fica escondido em Ajustar')
  assert.equal(campos(item, 'Porções')[0].props['aria-label'], 'Porções de Talharim 500 g')
  assert.equal(campos(item, 'Validade (dias)')[0].props['aria-label'], 'Validade em dias de Talharim 500 g')
  assert.ok(doTipo(t.arvore, 'Botao').some((b) => texto(b) === 'Conferir produção' && b.props.tipo !== 'submit'), 'a barra só abre a revisão')
  assert.equal(enviarDaTela(t).length, 1, 'um único Confirmar no rodapé da revisão')
  assert.equal(enviarDaTela(t)[0].props.form, doTipo(t.arvore, 'form')[0].props.id, 'o rodapé confirma o formulário revisado')
})

await confere('Produção do dia: etiqueta que chega depois de ela sair da tela não abre a impressão', async () => {
  zerarProducao()
  montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  tocar(t, 'Talharim 500 g')
  revisar(t)
  await doTipo(t.arvore, 'form')[0].props.onSubmit(evento)
  await t.assentar()
  let impressoes = 0
  globalThis.window = { print: () => { impressoes += 1 } }
  let entregar = null
  globalThis.fetch = () => new Promise((resolver) => { entregar = () => resolver(respostaJson({ etiquetas: [{ id: 'e-1', sequencial: 1, codigo: 'C1', produto: { nome: 'Talharim', pesoG: 500, fichaAlergenos: [] }, loteCodigo: 'LOT-261011-001', loteValidadeEm: null, loteCriadoEm: '2026-10-11T12:00:00' }] })) })
  const etiquetas = doTipo(t.arvore, 'BotaoIcone').find((b) => b.props.rotulo === 'Etiquetas do lote LOT-261011-001')
  assert.equal(etiquetas.props.dica, 'Etiquetas')
  const imprimindo = etiquetas.props.onClick()
  for (let i = 0; i < 50 && !entregar; i += 1) await esperar(0)
  t.desmontar()
  entregar()
  await imprimindo
  await esperar(80)
  assert.equal(impressoes, 0)
  zerarProducao()
})

await confere('impressão: a folha não fica presa à altura da janela (lote grande sai em várias páginas)', () => {
  const css = readFileSync(new URL('../src/estilos/base.css', import.meta.url), 'utf8')
  const impressao = css.slice(css.lastIndexOf('@media print'))
  assert.match(css, /html, body \{ height: 100dvh; overflow: hidden; \}/, 'na tela o corpo continua do tamanho da janela')
  assert.match(impressao, /html, body \{ height: auto; overflow: visible; \}/, 'no papel não: senão só a primeira página imprime')
  assert.ok(impressao.indexOf('[data-imprimir] { display: block; }') > 0)
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
  // A API manda `aberto: true` também no dia que fechou (ele teve abertura): fechado vem primeiro.
  const linha = linhaDoFluxo(dia({ data: '2026-10-09', fechado: true, aberto: true, saldoInicial: 100, totalVendas: 80, totalPagamentosPedidos: 310.5, totalEntradasExtras: 20, totalSaidasExtras: 45 }))
  assert.deepEqual(linha, { data: '2026-10-09', entrou: 410.5, saiu: 45, saldo: 365.5, situacao: 'fechado', depoisDoFechamento: 0 })
  assert.equal(linhaDoFluxo(dia({ aberto: true, totalEntradasExtras: 10 })).situacao, 'aberto')
  assert.equal(linhaDoFluxo(dia({ totalPagamentosPedidos: 59.9 })).situacao, 'sem-abertura')
  assert.equal(linhaDoFluxo(dia({ totalSaidasExtras: 12 })).situacao, 'sem-abertura', 'dia só com saída também tem movimento')
  assert.equal(linhaDoFluxo(dia()).situacao, 'sem-movimento')
  const { temMovimentoNoDia } = await import('../src/dominio/caixa.js')
  assert.equal(temMovimentoNoDia(dia()), false)
  assert.equal(temMovimentoNoDia(dia({ movimentos: [{ id: 'a', tipo: 'abertura', valor: 50 }] })), false, 'abrir o caixa não é movimento')
  assert.equal(temMovimentoNoDia(dia({ movimentos: [{ id: 'm', tipo: 'saida', valor: 5 }] })), true)
  assert.equal(temMovimentoNoDia(dia({ linhasExtras: [{ valor: 10, meio: 'pix' }] })), true)
  assert.equal(temMovimentoNoDia(dia({ totalVendas: 10 })), true)
  assert.deepEqual(totaisDoFluxo([linha, linhaDoFluxo(dia({ totalPagamentosPedidos: 59.9, totalSaidasExtras: 9.9 }))]), { entrou: 470.4, saiu: 54.9, saldo: 415.5 })
  // Caixa esquecido aberto desde 09/10: hoje já soma a sessão inteira, então 09 e 10 não repetem na tabela.
  const anteriores = ['2026-10-10', '2026-10-09', '2026-10-08'].map((data) => dia({ data, totalVendas: 10 }))
  const esquecido = linhasDoFluxo(dia({ aberto: true, esquecidoAberto: true, abertoDesde: '2026-10-09', totalVendas: 30 }), anteriores)
  assert.deepEqual(esquecido.map((l) => [l.data, l.desde, l.entrou]), [['2026-10-11', '2026-10-09', 30], ['2026-10-08', null, 10]])
  assert.deepEqual(linhasDoFluxo(dia({ aberto: true }), anteriores).map((l) => l.data), ['2026-10-11', '2026-10-10', '2026-10-09', '2026-10-08'])
})

const { AbaCaixaApi } = await import('../src/features/gestao/caixa/AbaCaixaApi.jsx')

await confere('Caixa do dia: dia sem abertura mostra o que entrou, com o lançamento, junto do abrir o caixa', async () => {
  montarFetch([[DIA_DA_API, (url) => ({ data: dia({ data: new URL(url, 'http://x').searchParams.get('data') }) })]])
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
  // É o caso da dona (o PWA não abre caixa): os 7 dias também aparecem aqui.
  const fluxo = andar(t.arvore, (n) => n.props?.['aria-label'] === 'Fluxo de caixa dos últimos 7 dias')[0]
  assert.equal(doTipo(fluxo, 'tr').length, 9)
  assert.match(limpo(doTipo(fluxo, 'tr')[1]), /Hoje, 11\/10.*Sem abertura/)
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
  assert.equal(valor('Saldo inicial') + valor('Vendas') + valor('Pagamentos de pedidos') + valor('Outras entradas') - valor('Outras saídas'), valor('Total do caixa (com Pix e cartão)'))
})

await confere('Caixa do dia: fluxo dos últimos 7 dias lê os 6 dias anteriores e soma com o de hoje', async () => {
  const porDia = {
    '2026-10-10': dia({ data: '2026-10-10', fechado: true, aberto: true, totalPagamentosPedidos: 300, totalSaidasExtras: 50 }),
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
  let fora = true
  const chamadas = montarFetch([[DIA_DA_API, (url) => (fora
    ? { status: 500, error: { message: 'Sem conexão com o EasyStok.' } }
    : { data: dia({ data: new URL(url, 'http://x').searchParams.get('data'), totalVendas: 10 }) })]])
  g.caixa = caixaCom(dia({ aberto: true, totalEntradasExtras: 10, saldoEsperado: 10 }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  const fluxo = () => andar(t.arvore, (n) => n.props?.['aria-label'] === 'Fluxo de caixa dos últimos 7 dias')[0]
  assert.match(limpo(t.arvore), /Na gaveta \(dinheiro\)/)
  assert.match(limpo(t.arvore), /Os dias anteriores não carregaram\./)
  assert.equal(doTipo(fluxo(), 'tfoot').length, 0, 'sem os dias anteriores não há total de 7 dias')
  assert.equal(doTipo(fluxo(), 'tr').length, 2, 'só o cabeçalho e hoje')
  fora = false
  doTipo(fluxo(), 'Botao').find((b) => /Tentar de novo/.test(texto(b))).props.onClick()
  await t.assentar()
  assert.equal(chamadas.length, 12, 'seis leituras que falharam e seis da nova tentativa')
  assert.equal(doTipo(fluxo(), 'tr').length, 9)
  assert.doesNotMatch(limpo(t.arvore), /Os dias anteriores não carregaram/)
})

await confere('Caixa do dia: quem não é gerente não vê fechar nem estornar; caixa fechado não lança', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  const movimentos = [{ id: 'm-1', tipo: 'entrada', valor: 20, categoria: 'Troco devolvido', meio: 'dinheiro', descricao: '', em: '2026-10-11T16:00:00Z' }]
  g.acesso = { acoes: {}, permite: () => true }
  g.caixa = caixaCom(dia({ aberto: true, saldoInicial: 100, totalEntradasExtras: 20, saldoEsperado: 120, movimentos }))
  const operador = montar(AbaCaixaApi)
  await operador.assentar()
  const botoes = (t) => doTipo(t.arvore, 'Botao').map(texto)
  assert.ok(botoes(operador).includes('Lançamento'))
  assert.ok(!botoes(operador).some((b) => /Fechar o caixa|Estornar/.test(b)), 'fechar e estornar são da dona ou gerente')
  assert.match(limpo(operador.arvore), /Somente a gerência pode fechar o caixa e estornar lançamentos\./)
  operador.desmontar()
  g.acesso = gerente()
  g.caixa = caixaCom(dia({ aberto: true, fechado: true, saldoInicial: 100, totalEntradasExtras: 20, saldoEsperado: 120, movimentos,
    fechamento: { id: 'f', data: '2026-10-11', saldoInicial: 100, totalVendas: 0, totalPagamentosPedidos: 0, totalEntradasExtras: 20, totalSaidasExtras: 0, saldoFinal: 120, fechadoEm: '2026-10-11T23:00:00Z', fechadoPorNome: 'Thati', observacoes: null } }))
  const fechado = montar(AbaCaixaApi)
  await fechado.assentar()
  assert.ok(doTipo(fechado.arvore, 'Pilula').some((pilula) => texto(pilula) === 'Fechado'))
  assert.ok(!botoes(fechado).some((b) => /Lançamento|Fechar o caixa|Estornar/.test(b)), 'dia fechado não lança, não fecha de novo e não estorna')
  // #2078 (D7): o mesmo nome para o total da API nos três estados do dia.
  assert.match(limpo(fechado.arvore), /Total do caixa \(com Pix e cartão\)/)
  assert.doesNotMatch(limpo(fechado.arvore), /Total esperado do dia|Total do dia/)
})

// --- Banca #1627: caixa -------------------------------------------------------------------
await confere('gaveta: venda ou pagamento sem forma de pagamento fica à parte, não vira "Pix e cartão"', async () => {
  const { gavetaDoDia } = await import('../src/dominio/caixa.js')
  const g = gavetaDoDia(dia({ saldoInicial: 100, linhasExtras: [
    { valor: 250, meio: null, origem: 'Venda' }, { valor: 40, meio: 'pix', origem: 'Pedido' }, { valor: 30, meio: 'dinheiro', origem: 'Pedido' },
  ] }))
  assert.deepEqual(g, { naGaveta: 130, pixECartao: 40, semForma: 250 })
  assert.equal(gavetaDoDia(dia({ saldoInicial: 100 })).semForma, 0)
})

await confere('fluxo: suprimento e sangria só trocam o dinheiro de lugar e não entram em entrou nem em saiu', async () => {
  const { linhaDoFluxo } = await import('../src/dominio/caixa.js')
  const linha = linhaDoFluxo(dia({ aberto: true, totalVendas: 300, totalEntradasExtras: 120, totalSaidasExtras: 395, movimentos: [
    { id: 'a', tipo: 'entrada', categoria: 'Suprimento', valor: 100 }, { id: 'b', tipo: 'entrada', categoria: 'Troco devolvido', valor: 20 },
    { id: 'c', tipo: 'saida', categoria: ' sangria ', valor: 350 }, { id: 'd', tipo: 'saida', categoria: 'Despesa', valor: 45 },
  ] }))
  assert.deepEqual([linha.entrou, linha.saiu, linha.saldo], [320, 45, 275])
})

await confere('fluxo: dia fechado que recebeu dinheiro depois do fechamento diz quanto mudou', async () => {
  const { linhaDoFluxo } = await import('../src/dominio/caixa.js')
  const retrato = { saldoInicial: 100, totalVendas: 0, totalPagamentosPedidos: 400, totalEntradasExtras: 0, totalSaidasExtras: 0, saldoFinal: 500 }
  // #1630: a API devolve o dia fechado com os números do fechamento e o que entrou depois em `posFechamento`.
  const fechadoComPix = linhaDoFluxo(dia({ fechado: true, saldoInicial: 100, totalPagamentosPedidos: 400, posFechamento: 80, fechamento: retrato }))
  assert.equal(fechadoComPix.depoisDoFechamento, 80)
  assert.equal(fechadoComPix.entrou, 400, 'o dia fechado não muda')
  assert.equal(linhaDoFluxo(dia({ fechado: true, saldoInicial: 100, totalPagamentosPedidos: 400, posFechamento: 0, fechamento: retrato })).depoisDoFechamento, 0)
  assert.equal(linhaDoFluxo(dia({ aberto: true, totalPagamentosPedidos: 480 })).depoisDoFechamento, 0)
})

await confere('Caixa do dia: sem abertura o resumo não inventa gaveta nem saldo inicial, e a venda sem forma aparece com o nome certo', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.acesso = { acoes: { gerenciarCaixa: true }, permite: () => true }
  g.caixa = caixaCom(dia({
    totalVendas: 250, totalPagamentosPedidos: 70, saldoEsperado: 320,
    linhasExtras: [
      { em: '2026-10-11T15:00:00Z', tipo: 'venda', valor: 250, descricao: 'Mobile', meio: null, origem: 'Venda' },
      { em: '2026-10-11T16:00:00Z', tipo: 'pagamento', valor: 40, descricao: '', meio: 'pix', origem: 'Pedido' },
      { em: '2026-10-11T17:00:00Z', tipo: 'pagamento', valor: 30, descricao: '', meio: 'dinheiro', origem: 'Pedido' },
    ],
  }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  const resumo = andar(t.arvore, (n) => n.type === 'dl' && String(n.props.className).includes('resumoLista'))[0]
  const linhas = Object.fromEntries(doTipo(resumo, 'div').map((d) => [texto(doTipo(d, 'dt')[0]), limpo(doTipo(d, 'dd')[0])]))
  assert.equal(linhas['Na gaveta (dinheiro)'], undefined, 'sem abrir o caixa ninguém disse quanto há na gaveta')
  assert.equal(linhas['Saldo inicial'], undefined)
  assert.deepEqual([linhas['Em dinheiro'], linhas['Pix, cartão e outros'], linhas['Sem forma de pagamento'], linhas['Total do caixa (com Pix e cartão)']],
    [moedaDe(30), moedaDe(40), moedaDe(250), moedaDe(320)])
})

await confere('Caixa do dia: abrir o caixa não relê os dias anteriores; fechar o caixa esquecido relê', async () => {
  const chamadas = montarFetch([[DIA_DA_API, (url) => ({ data: dia({ data: new URL(url, 'http://x').searchParams.get('data') }) })]])
  g.caixa = caixaCom(dia({ totalEntradasExtras: 10, saldoEsperado: 10 }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  assert.equal(chamadas.length, 6)
  g.caixa = caixaCom(dia({ aberto: true, saldoInicial: 50, totalEntradasExtras: 10, saldoEsperado: 60 }))
  await t.assentar()
  assert.equal(chamadas.length, 6, 'a tabela dos 7 dias continuou montada')
  assert.match(limpo(t.arvore), /Na gaveta \(dinheiro\)/)
  g.caixa = caixaCom(dia({ aberto: true, esquecidoAberto: true, abertoDesde: '2026-10-09', saldoInicial: 50, saldoEsperado: 60 }))
  await t.assentar()
  assert.equal(chamadas.length, 12, 'a sessão mudou: os dias anteriores são relidos')
  const fluxo = andar(t.arvore, (n) => n.props?.['aria-label'] === 'Fluxo de caixa dos últimos 7 dias')[0]
  assert.match(limpo(fluxo), /Desde 09\/10/)
  assert.match(limpo(fluxo), /O caixa está aberto desde 09\/10/)
  assert.doesNotMatch(limpo(doTipo(fluxo, 'tfoot')[0]), /7 dias/, 'o total não diz 7 dias quando soma uma sessão mais longa')
})

await confere('Caixa do dia: erro ao estornar fica junto dos lançamentos, não no bloco de abrir o caixa', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.acoes = { abrirCaixa() {}, fecharCaixa() {}, lancarMovimentoCaixa() {}, estornarMovimentoCaixa: async () => { throw new Error('Movimento de outro dia.') } }
  g.caixa = caixaCom(dia({
    totalEntradasExtras: 120.5, saldoEsperado: 120.5,
    movimentos: [{ id: 'm-1', tipo: 'entrada', valor: 120.5, categoria: 'Venda no balcão', meio: 'pix', descricao: '', em: '2026-10-11T16:00:00Z' }],
  }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  doTipo(t.arvore, 'Botao').find((b) => texto(b) === 'Estornar').props.onClick()
  await doTipo(t.renderizar(), 'PromptEstorno')[0].props.aoConfirmar('lancei errado')
  t.renderizar()
  const movimentacao = andar(t.arvore, (n) => n.props?.['aria-label'] === 'Movimentação do caixa')[0]
  assert.match(texto(andar(movimentacao, (n) => n.props?.role === 'alert')[0]), /Movimento de outro dia\./)
  assert.equal(andar(t.arvore, (n) => n.props?.role === 'alert').length, 1, 'o erro aparece uma vez só')
  assert.ok(doTipo(t.arvore, 'Botao').some((b) => texto(b) === 'Abrir o caixa'), 'o botão de abrir não diz que está abrindo')
})

await confere('Caixa do dia: a tabela dos 7 dias tem legenda, rótulo por célula para tela estreita e avisa dia fechado que mudou', async () => {
  const retrato = { id: 'f', data: '2026-10-10', saldoInicial: 0, totalVendas: 0, totalPagamentosPedidos: 300, totalEntradasExtras: 0, totalSaidasExtras: 0, saldoFinal: 300, fechadoEm: '2026-10-10T23:00:00Z' }
  montarFetch([[DIA_DA_API, (url) => { const data = new URL(url, 'http://x').searchParams.get('data'); return { data: data === '2026-10-10' ? dia({ data, fechado: true, totalPagamentosPedidos: 300, posFechamento: 80, fechamento: retrato }) : dia({ data }) } }]])
  g.caixa = caixaCom(dia({ aberto: true, totalEntradasExtras: 10, saldoEsperado: 10 }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  const fluxo = andar(t.arvore, (n) => n.props?.['aria-label'] === 'Fluxo de caixa dos últimos 7 dias')[0]
  assert.match(texto(doTipo(fluxo, 'caption')[0]), /suprimento e sangria/)
  const ontem = doTipo(fluxo, 'tr')[2]
  assert.deepEqual(doTipo(ontem, 'td').map((td) => td.props['data-rotulo']), ['Entrou', 'Saiu', 'Resultado do dia', 'Caixa'])
  assert.match(limpo(ontem), /Fechado.*R\$ 80,00 depois de fechar/)
})

// Frente J2 (#1990), item 5: no laboratório (23/10) o caixa fechou com devolução pendente e o
// modal não disse nada. Agora soma o `aDevolver` dos cancelados com recebimento e avisa, sem travar.
await confere('J2-5 fechar o caixa avisa quanto há a devolver de pedidos cancelados, sem bloquear', async () => {
  const { somarDevolucoesPendentes } = await import('../src/aplicacao/devolucoesPendentes.js')
  const { avisoDeDevolucoesPendentes } = await import('../src/dominio/caixa.js')
  const consultados = []
  const portas = {
    listarCancelados: async () => [
      { id: 'a', totalPago: 216 }, { id: 'b', totalPago: 0 }, { id: 'c', totalPago: 50 }, { id: 'd', totalPago: 30 },
    ],
    aDevolverDoPedido: async (id) => { consultados.push(id); return { a: 216, c: 0, d: 30 }[id] },
  }
  assert.deepEqual(await somarDevolucoesPendentes(portas), { total: 246, pedidos: 2, falhas: 0 })
  assert.deepEqual(consultados, ['a', 'c', 'd'], 'sem recebimento não consulta')
  assert.deepEqual(await somarDevolucoesPendentes({ ...portas, listarCancelados: async () => null }), { total: 0, pedidos: 0, falhas: 0 })
  // Uma leitura que falha não apaga as outras; e nunca mais que algumas leituras ao mesmo tempo.
  let emVoo = 0, pico = 0
  const muitos = Array.from({ length: 12 }, (_, i) => ({ id: `p${i}`, totalPago: 10 }))
  const soma = await somarDevolucoesPendentes({
    listarCancelados: async () => muitos,
    aDevolverDoPedido: async (id) => {
      emVoo += 1; pico = Math.max(pico, emVoo)
      await new Promise((r) => setTimeout(r, 1))
      emVoo -= 1
      if (id === 'p3') throw new Error('404')
      return 10
    },
  })
  assert.deepEqual(soma, { total: 110, pedidos: 11, falhas: 1 })
  assert.ok(pico <= 4, `no máximo 4 leituras por vez (foram ${pico})`)
  const semEspacoDuro = (t) => String(t ?? '').replace(/ /g, ' ')
  assert.equal(semEspacoDuro(avisoDeDevolucoesPendentes({ total: 246, pedidos: 2 })),
    'Há R$ 246,00 a devolver de 2 pedidos cancelados. O fechamento não espera: registre a devolução no Pós-venda de cada pedido.')
  assert.equal(semEspacoDuro(avisoDeDevolucoesPendentes({ total: 216, pedidos: 1 })),
    'Há R$ 216,00 a devolver de 1 pedido cancelado. O fechamento não espera: registre a devolução no Pós-venda de cada pedido.')
  assert.equal(avisoDeDevolucoesPendentes({ total: 0, pedidos: 0 }), null)
  assert.match(semEspacoDuro(avisoDeDevolucoesPendentes({ total: 110, pedidos: 11, falhas: 1 })),
    /^Há R\$ 110,00 a devolver de 11 pedidos cancelados\..* 1 pedido cancelado não foi conferido\.$/)
  assert.equal(avisoDeDevolucoesPendentes({ total: 0, pedidos: 0, falhas: 2 }),
    'Não deu para conferir tudo: 2 pedidos cancelados não foram conferidos.')
  const modal = readFileSync(new URL('../src/features/gestao/caixa/ModalFecharCaixa.jsx', import.meta.url), 'utf8')
  assert.match(modal, /useDevolucoesPendentes\(consultarDevolucoes\)/)
  assert.match(modal, /avisoDeDevolucoesPendentes\(devolucoes\)/)
  assert.doesNotMatch(modal, /disabled=\{[^}]*devolucoes/, 'o aviso não trava o botão')
  const aba = readFileSync(new URL('../src/features/gestao/caixa/AbaCaixaApi.jsx', import.meta.url), 'utf8')
  assert.match(aba, /<ModalFecharCaixa\s+consultarDevolucoes/)
})

// --- #2078 (banca #2073): leitura do caixa do dia ----------------------------------------
const DIA_ABERTO_2078 = () => dia({
  aberto: true, saldoInicial: 130, totalPagamentosPedidos: 136, totalEntradasExtras: 50, totalSaidasExtras: 23.9, saldoEsperado: 292.1,
  movimentos: [
    { id: 'm-1', tipo: 'entrada', valor: 50, categoria: 'Suprimento', meio: 'dinheiro', descricao: '', em: '2026-10-11T16:00:00Z' },
    { id: 'm-2', tipo: 'saida', valor: 23.9, categoria: 'Despesa', meio: 'dinheiro', descricao: 'Gás', em: '2026-10-11T18:30:00Z' },
  ],
  linhasExtras: [
    { em: '2026-10-11T17:00:00Z', tipo: 'pagamento', valor: 106, descricao: 'Pagamento de pedido', meio: 'dinheiro', origem: 'Pedido' },
    { em: '2026-10-11T19:00:00Z', tipo: 'pagamento', valor: 30, descricao: 'Pagamento de pedido', meio: 'pix', origem: 'Pedido' },
  ],
})
const caixaQueRele = (...dias) => {
  const fila = [...dias]
  const c = { estado: 'ok', erro: null, fechamentos: [], dia: fila[0] }
  c.recarregar = async () => { const proximo = fila.length > 1 ? fila.shift() : fila[0]; if (proximo) c.dia = proximo; return proximo }
  return c
}

await confere('#2078 B15: lançamentos do mais recente para o mais antigo', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.caixa = caixaCom(DIA_ABERTO_2078())
  const t = montar(AbaCaixaApi)
  await t.assentar()
  const movimentacao = andar(t.arvore, (n) => n.props?.['aria-label'] === 'Movimentação do caixa')[0]
  const titulos = doTipo(doTipo(movimentacao, 'ul')[0], 'li').map((li) => texto(doTipo(li, 'strong')[0]))
  assert.deepEqual(titulos, ['Pagamento de pedido', 'Despesa', 'Pagamento de pedido', 'Suprimento'])
})

await confere('#2078 E6: o número herói é a gaveta, à frente, com o total da API com um nome só', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.caixa = caixaCom(DIA_ABERTO_2078())
  const t = montar(AbaCaixaApi)
  await t.assentar()
  const resumo = andar(t.arvore, (n) => n.type === 'dl' && String(n.props.className).includes('resumoLista'))[0]
  const itens = doTipo(resumo, 'div')
  assert.equal(itens[0].props.className, 'resumoForte')
  assert.deepEqual([texto(doTipo(itens[0], 'dt')[0]), limpo(doTipo(itens[0], 'dd')[0])], ['Na gaveta (dinheiro)', moedaDe(262.1)])
  assert.equal(texto(doTipo(itens.at(-1), 'dt')[0]), 'Total do caixa (com Pix e cartão)')
  const css = readFileSync(new URL('../src/features/gestao/caixa/abaCaixa.module.css', import.meta.url), 'utf8')
  assert.match(css, /\.caixaApi > \.resumoLista \.resumoForte dd \{ font-size: var\(--t-valor\);/, 'o herói no tamanho de valor (28 px), não no de título')
  assert.doesNotMatch(css, /\.caixaApi > \.resumoLista dt \{ min-height/, 'sem duas linhas reservadas entre o rótulo e o número')
})

await confere('#2078 D6: sem abertura, com dinheiro já no dia, o saldo inicial pede só o troco', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.caixa = caixaCom(dia({ totalPagamentosPedidos: 319, saldoEsperado: 319,
    linhasExtras: [{ em: '2026-10-11T17:00:00Z', tipo: 'pagamento', valor: 319, descricao: '', meio: 'dinheiro', origem: 'Pedido' }] }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  const campo = andar(t.arvore, (n) => n.type === 'CampoReais' && n.props.rotulo === 'Saldo inicial')[0]
  assert.match(campo.props.dica.replace(/ /g, ' '), /^Só o troco que ficou de ontem, sem os R\$ 319,00 em dinheiro que já entraram hoje/)
  assert.match(limpo(t.arvore), /Ao abrir, informe só o troco: o que entrou hoje já está na conta\./)
  assert.doesNotMatch(limpo(t.arvore), /só registra o que está na gaveta/)
})

await confere('#2078 B11/E4: Lançamento abre com foco no valor e não fecha por Esc ou fundo com algo digitado', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.caixa = caixaCom(DIA_ABERTO_2078())
  const t = montar(AbaCaixaApi)
  await t.assentar()
  doTipo(t.arvore, 'Botao').find((b) => texto(b) === 'Lançamento').props.onClick()
  const modal = () => doTipo(t.renderizar(), 'Modal')[0]
  assert.equal(modal().props.titulo, 'Lançar entrada ou saída')
  assert.equal(modal().props.focarCampo, true)
  assert.equal(modal().props.segurarFechamento, false, 'vazio, Esc fecha como antes')
  assert.equal(andar(modal(), (n) => n.type === 'CampoReais' && n.props.rotulo === 'Valor')[0].props['data-foco-inicial'], '')
  andar(modal(), (n) => n.type === 'CampoReais' && n.props.rotulo === 'Valor')[0].props.aoMudar(1250)
  assert.equal(modal().props.segurarFechamento, true, 'com valor digitado, Esc e fundo não perdem o que ela digitou')
  assert.equal(typeof modal().props.aoFechar, 'function', 'o X continua fechando')
})

await confere('#2078 B11/E4/E5: Fechar o caixa abre no contado, segura o valor e anuncia a diferença com a gravidade em palavra', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.caixa = caixaQueRele(DIA_ABERTO_2078())
  const t = montar(AbaCaixaApi)
  await t.assentar()
  await doTipo(t.arvore, 'Botao').find((b) => texto(b) === 'Fechar o caixa').props.onClick()
  const modal = () => doTipo(t.renderizar(), 'Modal')[0]
  assert.equal(modal().props.titulo, 'Fechar o caixa')
  assert.equal(modal().props.focarCampo, true)
  assert.equal(modal().props.segurarFechamento, false)
  const contado = () => andar(modal(), (n) => n.type === 'CampoReais')[0]
  assert.equal(contado().props['data-foco-inicial'], '')
  contado().props.aoMudar(22210)
  assert.equal(modal().props.segurarFechamento, true)
  const diferenca = doTipo(modal(), 'DiferencaDaGaveta')[0]
  assert.equal(diferenca.props.conferencia.texto.replace(/ /g, ' '), 'Faltam R$ 40,00')
  assert.equal(diferenca.props.conferencia.gravidade, 'Diferença grande: conte de novo antes de fechar.')
  const componente = readFileSync(new URL('../src/componentes/DiferencaDaGaveta.jsx', import.meta.url), 'utf8')
  assert.match(componente, /<output className=\{className\} aria-live="polite">/, 'a região existe antes do valor')
})

await confere('#2078 B12: sem conexão no fechamento não culpa o valor e a nova tentativa relê; erro velho não vence o aviso novo', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  const fechadoAgora = { ...DIA_ABERTO_2078(), fechado: true, fechamento: { id: 'f', data: '2026-10-11', saldoFinal: 292.1, fechadoEm: '2026-10-11T22:00:00Z' } }
  // Abrir o modal e a 1ª confirmação leem o dia aberto; a 2ª confirmação não consegue ler; a 3ª já vê o dia fechado.
  const c = caixaQueRele(DIA_ABERTO_2078(), DIA_ABERTO_2078(), DIA_ABERTO_2078())
  g.caixa = c
  let fechamentos = 0
  g.acoes = { ...acoesDoCaixa(), fecharCaixa: async () => { fechamentos += 1; throw Object.assign(new Error('Sem conexão com o EasyStok.'), { status: 0 }) } }
  const t = montar(AbaCaixaApi)
  await t.assentar()
  await doTipo(t.arvore, 'Botao').find((b) => texto(b) === 'Fechar o caixa').props.onClick()
  const modal = () => doTipo(t.renderizar(), 'Modal')[0]
  const alerta = () => andar(modal(), (n) => n.props?.role === 'alert').map(limpo).join(' | ')
  const confirmar = () => doTipo(modal().props.rodape, 'Botao').find((b) => texto(b) === 'Fechar o caixa').props.onClick()
  andar(modal(), (n) => n.type === 'CampoReais')[0].props.aoMudar(26210)
  await confirmar()
  assert.equal(fechamentos, 1)
  assert.match(alerta(), /^Sem conexão com o EasyStok\. Não deu para saber se o fechamento foi gravado\./)
  assert.doesNotMatch(alerta(), /Confira o valor/)
  // A releitura falha agora: o aviso dela aparece sozinho, sem o erro da tentativa anterior.
  c.recarregar = async () => null
  await confirmar()
  assert.equal(alerta(), 'Não deu para conferir o caixa agora. Tente de novo antes de fechar.')
  // Ela tenta de novo e o fechamento sem resposta tinha gravado: a releitura vê o dia fechado e o modal sai.
  c.recarregar = async () => { c.dia = fechadoAgora; return fechadoAgora }
  await confirmar()
  assert.equal(fechamentos, 1, 'não manda fechar de novo')
  assert.equal(doTipo(t.renderizar(), 'Modal').length, 0)
})

await confere('#2078 B12: recusa da API diz que o caixa não fechou, sem culpar o valor; esperado que mudou aparece uma vez', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  const entrou = { ...DIA_ABERTO_2078(), saldoInicial: 140 }
  g.caixa = caixaQueRele(DIA_ABERTO_2078(), DIA_ABERTO_2078(), entrou)
  g.acoes = { ...acoesDoCaixa(), fecharCaixa: async () => { throw Object.assign(new Error('O caixa de hoje já foi fechado.'), { status: 409 }) } }
  const t = montar(AbaCaixaApi)
  await t.assentar()
  await doTipo(t.arvore, 'Botao').find((b) => texto(b) === 'Fechar o caixa').props.onClick()
  const modal = () => doTipo(t.renderizar(), 'Modal')[0]
  const alerta = () => andar(modal(), (n) => n.props?.role === 'alert').map(limpo).join(' | ')
  const confirmar = () => doTipo(modal().props.rodape, 'Botao').find((b) => texto(b) === 'Fechar o caixa').props.onClick()
  andar(modal(), (n) => n.type === 'CampoReais')[0].props.aoMudar(26210)
  await confirmar()
  assert.equal(alerta(), 'O caixa não foi fechado. O caixa de hoje já foi fechado.')
  await confirmar()
  assert.match(alerta(), /^Entrou ou saiu dinheiro enquanto você contava\. O esperado na gaveta agora é R\$ 272,10\. Confira a contagem e feche de novo\.$/)
})

await confere('#2078 B20: dia fechado não manda conferir antes de fechar', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.caixa = caixaCom(dia({ aberto: true, fechado: true, saldoInicial: 100, totalVendas: 38, saldoEsperado: 138,
    linhasExtras: [{ em: '2026-10-11T17:00:00Z', tipo: 'venda', valor: 38, descricao: 'Mobile', meio: null, origem: 'Venda' }],
    fechamento: { id: 'f', data: '2026-10-11', saldoFinal: 138, fechadoEm: '2026-10-11T23:00:00Z', observacoes: null } }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  assert.match(limpo(t.arvore), /O que veio sem forma de pagamento pode ter sido dinheiro\./)
  assert.doesNotMatch(limpo(t.arvore), /confira antes de fechar o caixa/)
})

const { ModalAbrirLoja, ModalFecharLoja } = await import('../src/features/loja/GestoLoja.jsx')
const FECHAMENTO_DE_ONTEM = { id: 'f-0', data: '2026-10-10', saldoInicial: 100, totalVendas: 0, totalPagamentosPedidos: 140, totalEntradasExtras: 0, totalSaidasExtras: 50, saldoFinal: 190, dinheiroNaGaveta: 130, fechadoEm: '2026-10-10T23:00:00Z', fechadoPorNome: 'Thati', observacoes: null }

await confere('#2078 B6: caixa e loja fechados dizem quem destrava e onde, sem mudar permissão', async () => {
  g.caixa = caixaCom(dia({ aberto: true, fechado: true, fechamento: { id: 'f', data: '2026-10-11', fechadoEm: '2026-10-11T23:14:00Z' } }))
  const t = montar(ModalAbrirLoja, { aoFechar() {} })
  t.renderizar()
  assert.match(limpo(t.arvore), /não reabre\. A loja só abre com o caixa aberto\. Para a loja voltar a seguir o horário da semana, a dona usa Atendimento › Horários e mensagens › Voltar a seguir o horário\./)
  assert.ok(!doTipo(t.arvore.props.rodape, 'Botao').some((b) => /Abrir/.test(texto(b))), 'continua sem botão de abrir')
})

await confere('#2078 D5/B8 e D6: abrir a loja mostra só a gaveta do último fechamento e pede só o troco', async () => {
  g.caixa = { ...caixaCom(dia({ totalPagamentosPedidos: 45, saldoEsperado: 45,
    linhasExtras: [{ em: '2026-10-11T12:00:00Z', tipo: 'pagamento', valor: 45, descricao: '', meio: 'dinheiro', origem: 'Pedido' }] })), fechamentos: [FECHAMENTO_DE_ONTEM] }
  const t = montar(ModalAbrirLoja, { aoFechar() {} })
  t.renderizar()
  const numeros = doTipo(doTipo(t.arvore, 'dl')[0], 'dt').map(texto)
  assert.deepEqual(numeros, ['Na gaveta em 10/10'], 'sem "Entrou" e "Saiu" que não fechavam com a gaveta')
  const campo = andar(t.arvore, (n) => n.type === 'CampoReais')[0]
  assert.equal(campo.props.rotulo, 'Saldo inicial (o troco da gaveta)')
  assert.match(campo.props.dica.replace(/ /g, ' '), /^Só o troco que ficou de ontem, sem os R\$ 45,00 em dinheiro que já entraram hoje: eles já estão na conta\. Já vem com o que o fechamento de 10\/10 deixou\.$/)
})

// #2078 (revisão R1): a operadora abria a loja pelo topo: o caixa abria (política Operador) e o
// controle da loja respondia 403. Sem `controlarLoja`, nada sai e a tela diz quem abre.
await confere('#2078 R1: quem não controla a loja não abre caixa nem loja pelo gesto, e sabe quem abre', async () => {
  const chamadas = []
  g.acesso = { acoes: { gerenciarCaixa: false, controlarLoja: false }, permite: () => true }
  g.acoes = { ...acoesDoCaixa(), abrirLojaComCaixa: async (...a) => { chamadas.push(a) } }
  g.caixa = { ...caixaCom(dia()), fechamentos: [FECHAMENTO_DE_ONTEM] }
  const t = montar(ModalAbrirLoja, { aoFechar() {} })
  t.renderizar()
  assert.match(t.arvore.props.descricao, /Só gerente ou dona abre a loja: peça a uma delas\./)
  assert.deepEqual(doTipo(t.arvore.props.rodape, 'Botao').map(texto), ['Entendi'])
  assert.equal(andar(t.arvore, (n) => n.type === 'CampoReais').length, 0, 'sem campo de saldo inicial')
  assert.deepEqual(chamadas, [])
})

await confere('#2078 B9/B10: fechar a loja diz o estado do caixa e quem não pode fechar não escreve justificativa à toa', async () => {
  g.acesso = { acoes: { gerenciarCaixa: true, controlarLoja: true }, permite: () => true }
  g.caixa = caixaCom(dia({ aberto: true, fechado: true }))
  const gerenteVe = montar(ModalFecharLoja, { aoFechar() {} })
  gerenteVe.renderizar()
  assert.match(limpo(gerenteVe.arvore), /O caixa de hoje já está fechado\./)
  assert.doesNotMatch(limpo(gerenteVe.arvore), /O caixa continua aberto/)
  assert.equal(doTipo(gerenteVe.arvore, 'CampoArea').length, 1)
  g.acesso = { acoes: { gerenciarCaixa: false, controlarLoja: false }, permite: () => true }
  g.caixa = caixaCom(dia({ aberto: true }))
  const operadora = montar(ModalFecharLoja, { aoFechar() {} })
  operadora.renderizar()
  assert.match(operadora.arvore.props.descricao, /Só gerente ou dona fecha a loja agora: peça a uma delas\./)
  assert.equal(doTipo(operadora.arvore, 'CampoArea').length, 0, 'sem campo de justificativa')
  assert.deepEqual(doTipo(operadora.arvore.props.rodape, 'Botao').map(texto), ['Entendi'])
  assert.match(limpo(operadora.arvore), /O caixa continua aberto\. Para fechar o caixa, use Financeiro › Caixa do dia\./)
})

// --- #2113 (seguimento da #2084, PR #2106): o console mostra o que a API passou a entregar ----------
const { caixaDiaDaApi } = await import('../src/infra/api/caixaApi.js')
const { criarAcoesCaixaApi } = await import('../src/aplicacao/api/caixa.js')
const { gavetaDoDia } = await import('../src/dominio/caixa.js')
const { ModalFecharCaixa } = await import('../src/features/gestao/caixa/ModalFecharCaixa.jsx')
const DIA_A_CONFIRMAR = () => dia({
  aberto: true, aberturaAConfirmar: true, saldoInicial: 130, totalPagamentosPedidos: 106, saldoEsperado: 236,
  movimentos: [{ id: 'a-1', tipo: 'abertura', valor: 130, categoria: 'Abertura de caixa', meio: 'dinheiro', descricao: '', em: '2026-10-11T12:00:00Z' }],
  linhasExtras: [{ em: '2026-10-11T12:00:00Z', tipo: 'pagamento', valor: 106, descricao: 'Pagamento de pedido', meio: 'dinheiro', origem: 'Pedido' }],
})
const DIA_ESQUECIDO = () => dia({
  aberto: true, esquecidoAberto: true, abertoDesde: '2026-10-10', saldoInicial: 100, totalEntradasExtras: 50, totalPagamentosPedidos: 70, saldoEsperado: 220,
  dinheiroEsperadoNoFechamento: 150, dinheiroEntrouDepois: 70,
  movimentos: [{ id: 'm-1', tipo: 'entrada', valor: 50, categoria: 'Suprimento', meio: 'dinheiro', descricao: '', em: '2026-10-10T16:00:00Z' }],
  linhasExtras: [{ em: '2026-10-11T13:00:00Z', tipo: 'pagamento', valor: 70, descricao: '', meio: 'dinheiro', origem: 'Pedido' }],
})

await confere('#2113: o caixa do dia da API traz a abertura a confirmar, o caixa esquecido, o dinheiro depois de fechar e o pedido da linha', () => {
  const lido = caixaDiaDaApi({
    data: '2026-10-11', aberto: true, aberturaAConfirmar: true, aberturaPendenteCrossDay: true, abertoDesde: '2026-10-10',
    dinheiroEsperadoNoFechamento: 150, dinheiroEntrouDepois: 70, dinheiroPosFechamento: 12,
    linhasExtras: [{ hora: '2026-10-11T13:00:00Z', tipo: 'pagamento', valor: 70, metodo: 'dinheiro', origem: 'Pedido', pedidoNumeroDoDia: 12, clienteNome: 'Maria Souza', registradoPorNome: 'Thati' }],
  })
  assert.equal(lido.aberturaAConfirmar, true)
  assert.equal(lido.dinheiroEsperadoNoFechamento, 150)
  assert.equal(lido.dinheiroEntrouDepois, 70)
  assert.equal(lido.dinheiroPosFechamento, 12)
  assert.equal(lido.linhasExtras[0].pedidoNumeroDoDia, 12)
  assert.equal(lido.linhasExtras[0].clienteNome, 'Maria Souza')
  const antigo = caixaDiaDaApi({ data: '2026-10-11' })
  assert.deepEqual([antigo.aberturaAConfirmar, antigo.dinheiroEsperadoNoFechamento, antigo.dinheiroEntrouDepois, antigo.dinheiroPosFechamento], [false, null, null, 0])
})

await confere('#2113 B3: fechar o caixa manda o valor contado à API, além da observação', async () => {
  const chamadas = montarFetch([[(m, u) => m === 'POST' && u.endsWith('/api/caixa/fechar'), { data: {} }]])
  await criarAcoesCaixaApi().fecharCaixa(null, 218.5, 222.1)
  const corpo = chamadas.find((c) => c.url.endsWith('/api/caixa/fechar')).corpo
  assert.equal(corpo.valorContado, 218.5)
  assert.match(corpo.observacoes.replace(/\u00a0/g, ' '), /^Contado na gaveta: R\$ 218,50\. Faltam R\$ 3,60\.$/)
})

await confere('#2113 B1: caixa que abriu sozinho avisa e a gerente confirma com a gaveta contada agora', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  const aberturas = []
  g.acoes = { ...acoesDoCaixa(), abrirCaixa: async (...a) => { aberturas.push(a) } }
  g.caixa = { ...caixaCom(DIA_A_CONFIRMAR()), fechamentos: [FECHAMENTO_DE_ONTEM] }
  const t = montar(AbaCaixaApi)
  await t.assentar()
  assert.match(limpo(t.arvore), /O caixa de hoje abriu sozinho no primeiro pagamento em dinheiro, com R\$ 130,00 que o fechamento de 10\/10 deixou na gaveta\./)
  const campo = () => andar(t.renderizar(), (n) => n.type === 'CampoReais' && n.props.rotulo === 'Quanto há na gaveta agora (todo o dinheiro)')[0]
  const botao = () => doTipo(t.renderizar(), 'Botao').find((b) => texto(b) === 'Confirmar abertura')
  assert.ok(campo(), 'campo da gaveta contada')
  assert.equal(campo().props.centavos, null, 'nasce vazio: confirmar é contar')
  assert.equal(botao().props.disabled, true)
  campo().props.aoMudar(23600)
  assert.equal(doTipo(t.renderizar(), 'DiferencaDaGaveta').find((d) => d.props.conferencia.pronta).props.conferencia.texto, 'Bate certo')
  assert.equal(botao().props.disabled, false)
  await botao().props.onClick()
  assert.equal(aberturas.length, 1)
  assert.equal(aberturas[0][1], 236, 'saldoInicial é a gaveta contada agora; a API desconta o dinheiro do dia')
  assert.match(aberturas[0][2].replace(/\u00a0/g, ' '), /^Abertura automática conferida: contado R\$ 236,00 na gaveta\. Bate certo\.$/)
})

await confere('#2113 B1: a operadora vê o aviso da abertura a confirmar, sem campo nem botão', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.acesso = { acoes: { gerenciarCaixa: false, controlarLoja: false }, permite: () => true }
  g.caixa = { ...caixaCom(DIA_A_CONFIRMAR()), fechamentos: [FECHAMENTO_DE_ONTEM] }
  const t = montar(AbaCaixaApi)
  await t.assentar()
  assert.match(limpo(t.arvore), /abriu sozinho no primeiro pagamento em dinheiro/)
  assert.match(limpo(t.arvore), /Peça à gerência para contar a gaveta e confirmar a abertura\./)
  assert.equal(andar(t.arvore, (n) => n.type === 'CampoReais').length, 0)
  assert.ok(!doTipo(t.arvore, 'Botao').some((b) => texto(b) === 'Confirmar abertura'))
})

await confere('#2113 B1: abrir a loja com o caixa a confirmar pede a gaveta contada e confirma junto', async () => {
  const chamadas = []
  g.acoes = { ...acoesDoCaixa(), abrirLojaComCaixa: async (a) => { chamadas.push(a) } }
  g.caixa = { ...caixaCom(DIA_A_CONFIRMAR()), fechamentos: [FECHAMENTO_DE_ONTEM] }
  const t = montar(ModalAbrirLoja, { aoFechar() {} })
  t.renderizar()
  assert.match(limpo(t.arvore), /O caixa de hoje abriu sozinho no primeiro pagamento em dinheiro, com R\$ 130,00 que o fechamento de 10\/10 deixou na gaveta\./)
  const botao = () => doTipo(t.renderizar().props.rodape, 'Botao').find((b) => /Confirmar abertura/.test(texto(b)))
  assert.equal(texto(botao()), 'Confirmar abertura e abrir a loja')
  assert.equal(botao().props.disabled, true, 'sem contar não confirma')
  andar(t.renderizar(), (n) => n.type === 'CampoReais' && n.props.rotulo === 'Quanto há na gaveta agora (todo o dinheiro)')[0].props.aoMudar(23000)
  assert.equal(botao().props.disabled, false)
  await botao().props.onClick()
  assert.equal(chamadas.length, 1)
  assert.equal(chamadas[0].saldoInicial, 230)
  assert.equal(chamadas[0].caixaJaAberto, false, 'confirma a abertura pelo POST de abrir')
  assert.match(chamadas[0].observacoes.replace(/\u00a0/g, ' '), /^Abertura automática conferida: contado R\$ 230,00 na gaveta\. Faltam R\$ 6,00\.$/)
})

await confere('#2113 D1: fechar o caixa esquecido confere o esperado do dia da abertura e mostra à parte o que entrou depois', async () => {
  const conferidos = []
  const d = DIA_ESQUECIDO()
  const t = montar(ModalFecharCaixa, { resumo: d, gaveta: gavetaDoDia(d), aoFechar() {}, aoConfirmar: (v) => conferidos.push(v) })
  t.renderizar()
  const itens = doTipo(doTipo(t.arvore, 'dl')[0], 'div').map((i) => [texto(doTipo(i, 'dt')[0]), limpo(doTipo(i, 'dd')[0])])
  assert.deepEqual(itens[0], ['Esperado no fechamento de 10/10', moedaDe(150)])
  assert.ok(itens.some(([r, v]) => r === 'Entrou depois, fica no caixa de hoje' && v === moedaDe(70)), JSON.stringify(itens))
  const campo = () => andar(t.renderizar(), (n) => n.type === 'CampoReais')[0]
  assert.equal(campo().props.rotulo, 'Quanto você contou na gaveta (todo o dinheiro)')
  campo().props.aoMudar(22000)
  assert.equal(doTipo(t.renderizar(), 'DiferencaDaGaveta')[0].props.conferencia.texto, 'Bate certo', '220 contado = 150 do dia + 70 que entrou depois')
  campo().props.aoMudar(21500)
  assert.equal(doTipo(t.renderizar(), 'DiferencaDaGaveta')[0].props.conferencia.texto.replace(/\u00a0/g, ' '), 'Faltam R$ 5,00')
  doTipo(t.renderizar().props.rodape, 'Botao').find((b) => texto(b) === 'Fechar o caixa').props.onClick()
  assert.deepEqual(conferidos, [215], 'o contado vai inteiro; a API separa o que entrou depois')
})

await confere('#2113 D1: o caixa esquecido no abrir a loja também separa o esperado do que entrou depois', async () => {
  g.caixa = { ...caixaCom(DIA_ESQUECIDO()), fechamentos: [FECHAMENTO_DE_ONTEM] }
  const t = montar(ModalAbrirLoja, { aoFechar() {} })
  t.renderizar()
  const itens = doTipo(doTipo(t.arvore, 'dl')[0], 'div').map((i) => [texto(doTipo(i, 'dt')[0]), limpo(doTipo(i, 'dd')[0])])
  assert.deepEqual(itens, [['Esperado no fechamento de 10/10', moedaDe(150)], ['Entrou depois, fica no caixa de hoje', moedaDe(70)]])
  const campo = andar(t.arvore, (n) => n.type === 'CampoReais')[0]
  assert.equal(campo.props.rotulo, 'Quanto você contou na gaveta (todo o dinheiro)')
  campo.props.aoMudar(22000)
  assert.equal(doTipo(t.renderizar(), 'DiferencaDaGaveta')[0].props.conferencia.texto, 'Bate certo')
})

await confere('#2113 B4: dia fechado diz o dinheiro que entrou depois de fechar e vai para a abertura de amanhã', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.caixa = caixaCom(dia({ aberto: true, fechado: true, saldoInicial: 100, saldoEsperado: 100, posFechamento: 52, dinheiroPosFechamento: 12,
    fechamento: { id: 'f', data: '2026-10-11', saldoFinal: 100, fechadoEm: '2026-10-11T22:00:00Z', observacoes: null } }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  assert.match(limpo(t.arvore), /R\$ 12,00 em dinheiro entrou na gaveta depois de fechar e vai para a abertura de amanhã\./)
})

await confere('#2113: a linha do pagamento de pedido diz o número do pedido no dia e o cliente', async () => {
  montarFetch([[DIA_DA_API, { data: dia() }]])
  g.caixa = caixaCom(dia({ aberto: true, saldoInicial: 100, totalPagamentosPedidos: 136, saldoEsperado: 236, linhasExtras: [
    { em: '2026-10-11T17:00:00Z', tipo: 'pagamento', valor: 106, descricao: '', meio: 'dinheiro', origem: 'Pedido', pedidoNumeroDoDia: 12, clienteNome: 'Maria Souza' },
    { em: '2026-10-11T16:00:00Z', tipo: 'pagamento', valor: 30, descricao: '', meio: 'pix', origem: 'Pedido' },
  ] }))
  const t = montar(AbaCaixaApi)
  await t.assentar()
  const movimentacao = andar(t.arvore, (n) => n.props?.['aria-label'] === 'Movimentação do caixa')[0]
  const linhas = doTipo(doTipo(movimentacao, 'ul')[0], 'li')
  assert.equal(texto(doTipo(linhas[0], 'strong')[0]), 'Pagamento do pedido nº 012')
  assert.match(limpo(linhas[0]), /Dinheiro · Maria Souza/)
  assert.equal(texto(doTipo(linhas[1], 'strong')[0]), 'Pagamento de pedido', 'sem número, como antes')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length > 0) process.exit(1)
