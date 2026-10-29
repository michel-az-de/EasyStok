/* eslint-disable no-console */
// Prova da #1809 (Produção do dia para quem abre a tela pela primeira vez) e da #1808 item 1
// (filtro por categoria nos Itens do cardápio).
//   Produção do dia:
//   - o cartão traz a foto do prato (a mesma do cardápio), com reserva quando não há foto ou ela falha;
//   - passos visíveis no topo: tocar, conferir, registrar;
//   - o "−" e o contador do cartão têm tooltip e rótulo;
//   - sem pratos, a tela diz o que fazer e onde.
//   Itens do cardápio:
//   - categorias distintas, com contagem, combinando com a disponibilidade e com a busca;
//   - reordenar só com Todos, Todas as categorias e sem busca.
//   Telas reais compiladas, com hooks de mentira (como a prova-1624). Pixel e toque pedem navegador.
//
//   node ferramentas/prova-1809-producao-cardapio.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import { transformWithOxc } from 'vite'

const telasCompiladas = new Map()
for (const caminho of ['features/gestao/producao/AbaProducaoDoDiaApi', 'features/gestao/cardapio/AbaCardapioApi']) {
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
const g = globalThis.__prova1809 = { hooks, catalogo: { cardapio: [] }, gestao: null, acesso: { acoes: { editarCardapio: true, editarProducao: true }, permite: () => true } }
const DE_FORA = 'globalThis.__prova1809'
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
    if (url.endsWith('/aplicacao/contextos.js')) return inline(`export const useCatalogo=()=>${DE_FORA}.catalogo;export const useAcoes=()=>({});export const useAtendimento=()=>({agora:0})`)
    if (url.includes('/aplicacao/acessoModulos.')) return inline(`export const useAcessoModulos=()=>${DE_FORA}.acesso`)
    if (url.endsWith('/aplicacao/useGestaoCardapioApi.js')) return inline(`export const useGestaoCardapioApi=()=>${DE_FORA}.gestao`)
    if (url.endsWith('.css')) return inline('export default new Proxy({},{get:(_,p)=>String(p)})')
    if (url.endsWith('.json')) return inline('export default ' + readFileSync(new URL(url), 'utf8'))
    // Componente de fora das telas (Botao, Chip, Vazio) vira o próprio nome: aparece na árvore sem rodar.
    if (url.endsWith('.jsx')) {
      const nomes = [...readFileSync(new URL(url), 'utf8').matchAll(/export (?:function|const) (\w+)/g)].map((m) => m[1])
      return inline(nomes.map((n) => `export const ${n}='${n}'`).join('\n'))
    }
    return proximo(url, contexto)
  },
})

let passou = 0
const falhas = []
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
  }
}

function montarFetch(rotas = []) {
  globalThis.fetch = async (url, { method = 'GET' } = {}) => {
    for (const [teste, resposta] of rotas) {
      if (!teste(method, url)) continue
      return new Response(JSON.stringify({ data: resposta }), { status: 200 })
    }
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
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
    desmontar() {
      for (const h of hosts.values()) for (const v of h.valores) if (typeof v?.limpar === 'function') v.limpar()
      hosts.clear()
    },
  }
  telasAbertas.push(t)
  return t
}
const andar = (no, fn, achados = []) => {
  if (no == null || typeof no !== 'object') return achados
  if (Array.isArray(no)) { for (const filho of no) andar(filho, fn, achados); return achados }
  if (fn(no)) achados.push(no)
  andar(no.props?.rodape, fn, achados)
  return andar(no.props?.children, fn, achados)
}
const texto = (no) => (no == null ? '' : typeof no !== 'object' ? String(no) : Array.isArray(no) ? no.map(texto).join('') : texto(no.props?.children))
const limpo = (no) => texto(no).replace(/\s+/g, ' ').trim()
const doTipo = (no, tipo) => andar(no, (n) => n.type === tipo)
const comRotulo = (no, rotulo) => andar(no, (n) => n.props?.['aria-label'] === rotulo)
const daClasse = (no, classe) => andar(no, (n) => String(n.props?.className ?? '').split(' ').includes(classe))

// === Domínio ============================================================================
const PRATOS = [
  { sku: 'i-1', nome: 'Talharim', porcao: '500 g', categoria: 'Massas', foto: '/midia/talharim.jpg', porcoes: [] },
  { sku: 'i-2', nome: 'Lasanha', porcao: '', categoria: 'Massas', foto: '/midia/lasanha.jpg', porcoes: [
    { id: 'v-6', rotulo: '600 g', peso: '600 g', padrao: true }, { id: 'v-8', rotulo: 'Família', peso: '' },
  ] },
  { sku: 'i-3', nome: 'Molho sugo', porcao: '300 g', categoria: 'Molhos', foto: null, porcoes: [] },
]

await confere('Produção: cada cartão traz a foto do prato; sem foto, nulo', async () => {
  const { cartoesDaGrade } = await import('../src/dominio/gradeDeProducao.js')
  assert.deepEqual(cartoesDaGrade(PRATOS, []).map((c) => [c.nome, c.tamanho, c.foto]), [
    ['Talharim', '500 g', '/midia/talharim.jpg'], ['Lasanha', '600 g', '/midia/lasanha.jpg'],
    ['Lasanha', 'Família', '/midia/lasanha.jpg'], ['Molho sugo', '300 g', null],
  ])
})

await confere('categorias: distintas, na ordem em que aparecem, com contagem; sem categoria fica fora dos chips', async () => {
  const { categoriasComContagem } = await import('../src/dominio/categorias.js')
  const { cartoesDaGrade, categoriasDaGrade } = await import('../src/dominio/gradeDeProducao.js')
  assert.deepEqual(categoriasComContagem([{ categoria: 'Massas' }, { categoria: null }, { categoria: 'Molhos' }, { categoria: 'Massas' }, { categoria: '' }]),
    [{ nome: 'Massas', total: 2 }, { nome: 'Molhos', total: 1 }])
  assert.deepEqual(categoriasComContagem(null), [])
  assert.deepEqual(categoriasDaGrade(cartoesDaGrade(PRATOS, [])), ['Massas', 'Molhos'], 'a Produção segue com os mesmos chips')
})

const ITENS = [
  { sku: 'c-1', nome: 'Lasanha', categoria: 'Massas', porcao: '600 g', foto: '/midia/l.jpg', hoje: true, arquivado: false },
  { sku: 'c-2', nome: 'Talharim', categoria: 'Massas', porcao: '500 g', foto: null, hoje: false, arquivado: false },
  { sku: 'c-3', nome: 'Molho sugo', categoria: 'Molhos', porcao: '300 g', foto: '/midia/m.jpg', hoje: true, arquivado: false },
  { sku: 'c-4', nome: 'Pudim', categoria: 'Sobremesas', porcao: '', foto: null, hoje: true, arquivado: false },
  { sku: 'c-5', nome: 'Brownie', categoria: null, porcao: '', foto: null, hoje: true, arquivado: true },
]
const nomes = (lista) => lista.map((i) => i.nome)

await confere('cardápio: categoria combina com a disponibilidade e com a busca', async () => {
  const { filtrarCardapio } = await import('../src/dominio/filtroDoCardapio.js')
  assert.deepEqual(nomes(filtrarCardapio(ITENS, {})), nomes(ITENS), 'sem recorte, todos')
  assert.deepEqual(nomes(filtrarCardapio(ITENS, { categoria: 'Massas' })), ['Lasanha', 'Talharim'])
  assert.deepEqual(nomes(filtrarCardapio(ITENS, { categoria: 'Massas', filtro: 'hoje' })), ['Lasanha'])
  assert.deepEqual(nomes(filtrarCardapio(ITENS, { categoria: 'Massas', filtro: 'fora' })), ['Talharim'])
  assert.deepEqual(nomes(filtrarCardapio(ITENS, { filtro: 'hoje', busca: ' molho ' })), ['Molho sugo'])
  assert.deepEqual(nomes(filtrarCardapio(ITENS, { categoria: 'Massas', busca: 'TALH' })), ['Talharim'])
  assert.deepEqual(nomes(filtrarCardapio(ITENS, { busca: 'sobremesas' })), ['Pudim'], 'a busca continua olhando a categoria')
  assert.deepEqual(nomes(filtrarCardapio(ITENS, { categoria: 'Molhos', filtro: 'sem-foto' })), [])
})

await confere('cardápio: cada contagem aplica os outros recortes; as categorias não somem do lugar', async () => {
  const { contagensDoCardapio } = await import('../src/dominio/filtroDoCardapio.js')
  const emMassas = contagensDoCardapio(ITENS, { categoria: 'Massas' })
  assert.deepEqual(emMassas.filtros, { todos: 2, hoje: 1, fora: 1, 'sem-foto': 1 })
  assert.deepEqual(emMassas.categorias, [{ nome: 'Massas', total: 2 }, { nome: 'Molhos', total: 1 }, { nome: 'Sobremesas', total: 1 }])
  assert.equal(emMassas.todas, 5)
  const hoje = contagensDoCardapio(ITENS, { filtro: 'hoje' })
  assert.deepEqual(hoje.filtros, { todos: 5, hoje: 3, fora: 2, 'sem-foto': 3 })
  assert.deepEqual(hoje.categorias.map((c) => c.total), [1, 1, 1])
  assert.equal(hoje.todas, 3)
  const pudim = contagensDoCardapio(ITENS, { filtro: 'sem-foto', busca: 'pudim' })
  assert.deepEqual(pudim.categorias, [{ nome: 'Massas', total: 0 }, { nome: 'Molhos', total: 0 }, { nome: 'Sobremesas', total: 1 }])
  assert.equal(pudim.todas, 1)
})

await confere('cardápio: reordenar só com Todos, Todas as categorias e sem busca', async () => {
  const { podeReordenar } = await import('../src/dominio/filtroDoCardapio.js')
  assert.equal(podeReordenar({ filtro: 'todos', categoria: null, busca: '' }), true)
  assert.equal(podeReordenar({ filtro: 'todos', categoria: null, busca: '   ' }), true)
  assert.equal(podeReordenar({ filtro: 'todos', categoria: 'Massas', busca: '' }), false)
  assert.equal(podeReordenar({ filtro: 'hoje', categoria: null, busca: '' }), false)
  assert.equal(podeReordenar({ filtro: 'todos', categoria: null, busca: 'lasanha' }), false)
})

// === Produção do dia (tela) ===============================================================
const AbaProducaoDoDiaApi = (await import('../src/features/gestao/producao/AbaProducaoDoDiaApi.jsx')).AbaProducaoDoDiaApi
const { dispensarLote, guardarRascunho, lotesLancados } = await import('../src/aplicacao/producaoDoDia.js')
const zerarProducao = () => { guardarRascunho(null); for (const l of [...lotesLancados()]) dispensarLote(l.loteId) }
const rotasDaProducao = () => [
  [(m, u) => m === 'GET' && u.endsWith('/producao/estoque-do-dia'), { pratos: [], alertas: [] }],
  [(m, u) => m === 'GET' && u.includes('/producao/sugestao'), { ate: '2026-10-15', pratos: [] }],
]
const cartoesDaTela = (no) => andar(no, (n) => n.type === 'li' && n.props['data-prato'])
const abrirProducao = async (cardapio) => {
  zerarProducao()
  g.catalogo = { cardapio }
  montarFetch(rotasDaProducao())
  const t = montar(AbaProducaoDoDiaApi)
  await t.assentar()
  return t
}

await confere('Produção do dia: três passos visíveis no topo, sem repetir a frase antiga', async () => {
  const t = await abrirProducao(PRATOS)
  const passos = comRotulo(t.arvore, 'Como registrar a produção')[0]
  assert.ok(passos, 'a lista de passos existe')
  assert.equal(passos.type, 'ol')
  const itens = doTipo(passos, 'li').map(limpo)
  assert.equal(itens.length, 3)
  assert.match(itens[0], /^1\s*Toque nos pratos que ficaram prontos/)
  assert.match(itens[0], /cada toque soma uma porção/i)
  assert.match(itens[1], /^2\s*Confira/)
  assert.match(itens[2], /^3\s*Registre a produção/)
  assert.match(itens[2], /estoque só muda/i)
  assert.doesNotMatch(limpo(t.arvore), /Cada toque em um prato seleciona/, 'a frase da #1813 virou o passo 1, não ficou duplicada')
})

await confere('Produção do dia: o cartão mostra a foto (preguiçosa, alt vazio) e reserva o lugar sem foto ou com falha', async () => {
  const t = await abrirProducao(PRATOS)
  const cartoes = cartoesDaTela(t.arvore)
  assert.equal(cartoes.length, 4)
  const fotoDe = (cartao) => daClasse(cartao, 'fotoDoPrato')[0]
  const imagem = doTipo(fotoDe(cartoes[0]), 'img')[0]
  assert.ok(imagem, 'cartão com foto tem <img>')
  assert.deepEqual([imagem.props.src, imagem.props.alt, imagem.props.loading], ['/midia/talharim.jpg', '', 'lazy'])
  assert.ok(doTipo(cartoes[0], 'button')[0] && doTipo(doTipo(cartoes[0], 'button')[0], 'img').length === 1, 'a foto faz parte do toque')
  const semFoto = fotoDe(cartoes[3])
  assert.ok(semFoto, 'prato sem foto guarda o mesmo lugar')
  assert.equal(doTipo(semFoto, 'img').length, 0)
  imagem.props.onError()
  const depois = fotoDe(cartoesDaTela(t.renderizar())[0])
  assert.equal(doTipo(depois, 'img').length, 0, 'foto que falha vira a reserva')
  assert.ok(doTipo(fotoDe(cartoesDaTela(t.arvore)[1]), 'img').length === 1, 'a falha de um cartão não apaga a foto dos outros')
})

await confere('Produção do dia: o "−" e o contador do cartão têm tooltip e rótulo', async () => {
  const t = await abrirProducao(PRATOS)
  comRotulo(t.arvore, 'Somar 1 porção de Talharim 500 g')[0].props.onClick()
  comRotulo(t.renderizar(), 'Somar 1 porção de Talharim 500 g')[0].props.onClick()
  const cartao = cartoesDaTela(t.renderizar())[0]
  const menos = comRotulo(cartao, 'Tirar 1 porção de Talharim 500 g')[0]
  assert.ok(menos, 'o "−" existe')
  assert.equal(menos.props.title, 'Tirar 1 porção de Talharim 500 g')
  const contador = daClasse(cartao, 'contagem')[0]
  assert.equal(contador.props.title, '2 porções de Talharim 500 g na seleção')
  assert.equal(contador.props['aria-label'], '2 porções de Talharim 500 g na seleção')
  assert.match(limpo(contador), /^2/)
})

await confere('Produção do dia: sem pratos, o estado vazio explica e leva aos Itens do cardápio', async () => {
  const t = await abrirProducao([])
  const vazio = doTipo(t.arvore, 'Vazio')[0]
  assert.ok(vazio, 'há um estado vazio que ensina')
  assert.match(vazio.props.titulo, /Nenhum prato para produzir/)
  assert.match(limpo(vazio), /cardápio/i)
  assert.equal(vazio.props.acao?.props?.href, '#/m/cardapio/itens')
})

// === Itens do cardápio (tela) ============================================================
const AbaCardapioApi = (await import('../src/features/gestao/cardapio/AbaCardapioApi.jsx')).AbaCardapioApi
const abrirCardapio = () => {
  g.gestao = { estado: 'ok', itens: ITENS.map((i) => ({ ...i, linha: 'servir', porcoes: 0, preco: 10, noSite: true })), erro: null, aviso: null, recarregar() {}, fecharAviso() {} }
  const t = montar(AbaCardapioApi)
  t.renderizar()
  return t
}
const grupo = (t, legenda) => doTipo(t.arvore, 'fieldset').find((f) => limpo(doTipo(f, 'legend')[0]) === legenda)
const chips = (t, legenda) => doTipo(grupo(t, legenda), 'button').map((b) => [limpo(b), b.props['aria-pressed']])
const tocarChip = (t, legenda, rotulo) => { doTipo(grupo(t, legenda), 'button').find((b) => limpo(b).startsWith(rotulo)).props.onClick(); return t.renderizar() }
const categoriaNaTela = (t) => doTipo(t.arvore, 'CampoSelecao').find((c) => c.props.rotulo === 'Categoria')
const escolherCategoria = (t, valor) => { categoriaNaTela(t).props.onChange({ target: { value: valor } }); return t.renderizar() }
const limparFiltros = (t) => doTipo(t.arvore, 'Botao').find((b) => limpo(b) === 'Limpar filtros')
const pratosNaTela = (t) => andar(t.arvore, (n) => n.type === 'h3').map(limpo)

await confere('Itens do cardápio: seletor de categoria com contagem, Todas por padrão e sem limpar inativo', async () => {
  const t = abrirCardapio()
  assert.ok(categoriaNaTela(t), 'o seletor de categoria permanece visível')
  assert.equal(categoriaNaTela(t).props.value, '')
  assert.deepEqual(categoriaNaTela(t).props.opcoes, [
    { valor: '', rotulo: 'Todas (5)' }, { valor: 'Massas', rotulo: 'Massas (2)' },
    { valor: 'Molhos', rotulo: 'Molhos (1)' }, { valor: 'Sobremesas', rotulo: 'Sobremesas (1)' },
  ])
  assert.equal(limparFiltros(t), undefined)
})

await confere('Itens do cardápio: categoria, disponibilidade e busca combinam; contagens acompanham', async () => {
  const t = abrirCardapio()
  escolherCategoria(t, 'Massas')
  assert.deepEqual(pratosNaTela(t), ['Lasanha', 'Talharim'])
  assert.deepEqual(chips(t, 'Filtrar pratos').map(([r]) => r), ['Todos 2', 'Disponíveis hoje 1', 'Fora de hoje 1', 'Sem foto 1'])
  tocarChip(t, 'Filtrar pratos', 'Disponíveis hoje')
  assert.deepEqual(pratosNaTela(t), ['Lasanha'])
  assert.equal(categoriaNaTela(t).props.value, 'Massas', 'o filtro ativo permanece visível no seletor')
  assert.deepEqual(categoriaNaTela(t).props.opcoes, [
    { valor: '', rotulo: 'Todas (3)' }, { valor: 'Massas', rotulo: 'Massas (1)' },
    { valor: 'Molhos', rotulo: 'Molhos (1)' }, { valor: 'Sobremesas', rotulo: 'Sobremesas (1)' },
  ])
  escolherCategoria(t, '')
  doTipo(t.arvore, 'CampoTexto')[0].props.onChange({ target: { value: 'pudim' } })
  t.renderizar()
  assert.deepEqual(pratosNaTela(t), ['Pudim'])
  limparFiltros(t).props.onClick(); t.renderizar()
  assert.deepEqual(pratosNaTela(t), nomes(ITENS), 'limpar restaura a lista completa')
  assert.equal(categoriaNaTela(t).props.value, '')
  assert.equal(doTipo(t.arvore, 'CampoTexto')[0].props.value, '')
  assert.equal(chips(t, 'Filtrar pratos')[0][1], true)
  assert.equal(limparFiltros(t), undefined)
})

await confere('Itens do cardápio: reordenar pede Todos, Todas as categorias e busca vazia', async () => {
  const t = abrirCardapio()
  assert.ok(comRotulo(t.arvore, 'Subir Lasanha').length === 1, 'com tudo à mostra, dá para ordenar')
  escolherCategoria(t, 'Massas')
  assert.equal(comRotulo(t.arvore, 'Subir Lasanha').length, 0, 'com uma categoria escolhida, não')
  assert.match(limpo(t.arvore), /Todas as categorias/)
  escolherCategoria(t, '')
  assert.ok(comRotulo(t.arvore, 'Subir Lasanha').length === 1)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
