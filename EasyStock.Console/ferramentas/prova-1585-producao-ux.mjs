/* eslint-disable no-console */
// Prova da #1585 (banca de UX do módulo Produção).
//   Aplicação, sem React:
//   - o rascunho da Produção do dia sobrevive à troca de tela e sai quando o lote grava;
//   - os lotes lançados na visita acumulam, cada um com as próprias etiquetas, até dispensar;
//   - o erro da linha diz de qual prato é; a chave do envio vale depois de sair e voltar;
//   - a lista de compras gerada fica guardada e não se repete para as mesmas porções.
//   Telas reais compiladas, com hooks de mentira (como a prova-premium-cozinha): monta, clica e
//   confere a árvore. Pixel, foco e a impressão no papel pedem navegador.
//
//   node ferramentas/prova-1585-producao-ux.mjs

import { registerHooks } from 'node:module'
import { readFileSync, readdirSync } from 'node:fs'
import assert from 'node:assert/strict'
import { transformWithOxc } from 'vite'

const pastaDasTelas = new URL('../src/features/gestao/producao/', import.meta.url)
const telasCompiladas = new Map()
for (const nome of readdirSync(pastaDasTelas).filter((n) => n.endsWith('Api.jsx'))) {
  const url = new URL(nome, pastaDasTelas)
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
globalThis.__prova1585 = { hooks, catalogo: { cardapio: [] } }
const inline = (source) => ({ format: 'module', shortCircuit: true, source })

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador === 'react') return { url: 'data:text/javascript,export const {useState,useMemo,useCallback,useEffect}=globalThis.__prova1585.hooks', shortCircuit: true }
    if (especificador === 'react/jsx-runtime') return { url: 'data:text/javascript,export const jsx=(type,props,key)=>({type,props,key});export const jsxs=jsx;export const Fragment="fragment"', shortCircuit: true }
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(especificador + ext, contexto) } catch { /* próxima extensão */ } }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (telasCompiladas.has(url)) return inline(telasCompiladas.get(url))
    if (url.endsWith('/infra/fonteDados.js')) return inline("export const FONTE_API = true\nexport const API_BASE = ''\n")
    if (url.endsWith('/aplicacao/contextos.js')) return inline('export const useCatalogo=()=>globalThis.__prova1585.catalogo')
    if (url.includes('/aplicacao/acessoModulos.')) return inline('export const useAcessoModulos=()=>({acoes:{editarProducao:true},permite:()=>true})')
    if (url.endsWith('.css')) return inline('export default new Proxy({},{get:(_,p)=>String(p)})')
    if (url.endsWith('.json')) return inline('export default ' + readFileSync(new URL(url), 'utf8'))
    // Componente de fora das telas (Botao, CampoTexto, Vazio) vira o próprio nome: aparece na árvore sem rodar.
    if (url.endsWith('.jsx')) {
      const nomes = [...readFileSync(new URL(url), 'utf8').matchAll(/export (?:function|const) (\w+)/g)].map((m) => m[1])
      return inline(nomes.map((n) => `export const ${n}='${n}'`).join('\n'))
    }
    return proximo(url, contexto)
  },
})

const {
  aoLancarLote, criarProducaoDoDia, dispensarLote, guardarRascunho, linhasDoRascunho, lotesLancados, producaoDoDia,
} = await import('../src/aplicacao/producaoDoDia.js')
const { assinaturaDoPlano, comprasGuardadas, guardarCompras, itensDeCompra, levarParaProducao } = await import('../src/aplicacao/planejamento.js')
const { hashDoModulo } = await import('../src/dominio/modulos.js')
const { rotaDaHash } = await import('../src/dominio/rota.js')
const tela = async (nome) => (await import(new URL(`${nome}.jsx`, pastaDasTelas).href))[nome]

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

// Cada rota é [teste(metodo, url), resposta ou função que devolve a resposta]. `cair` derruba a rede.
function montarFetch(rotas = []) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body, headers = {} } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null, chave: headers['Idempotency-Key'] ?? null })
    for (const [teste, resposta] of rotas) {
      if (!teste(method, url)) continue
      const { status = 200, data = null, error, cair } = typeof resposta === 'function' ? resposta() : resposta
      if (cair) throw new TypeError('Failed to fetch')
      return new Response(JSON.stringify(error ? { error } : { data }), { status })
    }
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  return chamadas
}

const PRODUZIR = (m, u) => m === 'POST' && u.endsWith('/api/atendimento/producao')
const lote = (n, extra = {}) => ({ status: 201, data: {
  loteId: `l-${n}`, codigoLote: `LOT-261011-00${n}`, totalEtiquetas: 2,
  pratos: [{ cardapioItemId: 'i-1', nome: 'Ravióli', porcoes: 2, sobraG: null }], ...extra } })
// Responde os POSTs da produção com os lotes dados, na ordem.
const montarProducao = (...lotes) => { let n = 0; return montarFetch([[PRODUZIR, () => lotes[n++] ?? {}]]) }

const linha = (extra = {}) => ({ sku: 'i-1', nome: 'Ravióli', porcoes: 2, pesoPorPorcaoG: 500, pesoRealG: null, validadeDias: 5, ...extra })
// O que a tela guarda: as linhas como ela digitou (texto), com o id do cartão.
const digitada = (id, extra = {}) => ({ id, sku: 'i-1', variacaoId: '', porcoes: '2', pesoPorPorcaoG: '500', pesoRealG: '', validadeDias: '5', ...extra })
const zerar = () => {
  guardarRascunho(null)
  for (const l of [...lotesLancados()]) dispensarLote(l.loteId)
}

// === Aplicação ==========================================================================
await confere('sem nada guardado a tela abre vazia; o que ela digitou volta igual na ida e volta', () => {
  zerar()
  assert.equal(linhasDoRascunho(null), null)
  const digitadas = [digitada('a', { porcoes: '12' }), digitada('b', { sku: 'i-2', pesoRealG: '1144' })]
  guardarRascunho(digitadas)
  assert.deepEqual(linhasDoRascunho(null), digitadas)
  assert.deepEqual(linhasDoRascunho([]), digitadas, 'Planejamento sem porções não mexe no rascunho')
})

await confere('produção que não grava (linha inválida ou rede fora) mantém o rascunho', async () => {
  zerar()
  const digitadas = [digitada('a')]
  guardarRascunho(digitadas)
  const producao = criarProducaoDoDia()
  montarFetch()
  assert.equal((await producao.produzir([linha({ porcoes: 0 })])).ok, false)
  montarFetch([[PRODUZIR, { cair: true }]])
  assert.equal((await producao.produzir([linha()])).ok, false)
  assert.deepEqual(linhasDoRascunho(null), digitadas)
  assert.equal(lotesLancados().length, 0)
})

await confere('gravou: o rascunho sai e o lote entra na lista', async () => {
  zerar()
  guardarRascunho([digitada('a')])
  montarProducao(lote(1))
  assert.equal((await criarProducaoDoDia().produzir([linha()])).ok, true)
  assert.equal(linhasDoRascunho(null), null)
  assert.deepEqual(lotesLancados().map((l) => l.codigo), ['LOT-261011-001'])
})

await confere('vindo do Planejamento, o prato planejado entra uma vez e o rascunho de outro prato fica', () => {
  zerar()
  guardarRascunho([digitada('a', { porcoes: '4', pesoRealG: '2000' }), digitada('b', { sku: 'i-2', porcoes: '6' }), digitada('c', { sku: '' })])
  const linhas = linhasDoRascunho([{ sku: 'i-1', porcoes: '10' }, { sku: 'i-3', porcoes: '3' }])
  assert.deepEqual(linhas.map((l) => [l.sku, l.porcoes]), [['i-2', '6'], ['i-1', '10'], ['i-3', '3']])
  assert.equal(linhas[0].id, 'b', 'a linha que já estava segue a mesma')
  zerar()
  assert.deepEqual(linhasDoRascunho([{ sku: 'i-1', porcoes: '3' }]), [{ sku: 'i-1', porcoes: '3' }], 'sem rascunho, só as planejadas')
})

await confere('os lotes acumulam, o mais novo primeiro; dispensar tira só aquele', async () => {
  zerar()
  montarProducao(lote(1), lote(2, { totalEtiquetas: 6, avisos: ['Molho sugo: faltaram 200 g.'] }))
  const producao = criarProducaoDoDia()
  await producao.produzir([linha()])
  await producao.produzir([linha({ porcoes: 6 })])
  assert.deepEqual(lotesLancados().map((l) => [l.loteId, l.codigo, l.etiquetas]), [['l-2', 'LOT-261011-002', 6], ['l-1', 'LOT-261011-001', 2]])
  assert.deepEqual(lotesLancados().map((l) => l.avisos), [['Molho sugo: faltaram 200 g.'], []])
  assert.deepEqual(dispensarLote('l-2').map((l) => l.loteId), ['l-1'])
  assert.deepEqual(dispensarLote('nao-existe').map((l) => l.loteId), ['l-1'])
})

await confere('o mesmo lote devolvido de novo pela API (mesma chave) não repete na lista', async () => {
  zerar()
  montarProducao(lote(1), lote(1))
  const producao = criarProducaoDoDia()
  await producao.produzir([linha()])
  await producao.produzir([linha()])
  assert.deepEqual(lotesLancados().map((l) => l.loteId), ['l-1'])
})

await confere('quem está na tela é avisado quando o lote grava, e só enquanto está nela', async () => {
  zerar()
  montarProducao(lote(1), lote(2))
  const avisados = []
  const sair = aoLancarLote((l) => avisados.push(l.loteId))
  const producao = criarProducaoDoDia()
  await producao.produzir([linha({ porcoes: 0 })])
  assert.deepEqual(avisados, [], 'produção que não gravou não avisa')
  await producao.produzir([linha()])
  sair()
  await producao.produzir([linha()])
  assert.deepEqual(avisados, ['l-1'])
  assert.equal(lotesLancados().length, 2, 'o lote que gravou sem ninguém na tela fica na lista para a volta')
})

await confere('o erro da linha traz o número do cartão e o nome do prato, sem chamar a API', async () => {
  const chamadas = montarFetch()
  const producao = criarProducaoDoDia()
  assert.equal((await producao.produzir([linha(), linha({ nome: 'Lasanha', porcoes: 0 })])).erro, 'Prato 02 (Lasanha): Quantas porções saíram?')
  assert.equal((await producao.produzir([linha({ sku: '', nome: null })])).erro, 'Prato 01: Escolha o prato.')
  assert.equal((await producao.produzir([linha(), linha(), linha({ nome: 'Nhoque', pesoRealG: 900 })])).erro, 'Prato 03 (Nhoque): O peso real não fecha as porções.')
  assert.equal((await producao.produzir([])).erro, 'Adicione ao menos um prato.')
  assert.equal(chamadas.length, 0)
})

await confere('a produção da tela é uma só: resposta perdida, sair, voltar e reenviar repete a chave', async () => {
  zerar()
  let cair = true
  const chamadas = montarFetch([[PRODUZIR, () => (cair ? { cair } : lote(1))]])
  guardarRascunho([digitada('a')])
  assert.equal((await producaoDoDia.produzir([linha()])).ok, false)
  assert.equal(linhasDoRascunho(null).length, 1, 'a tela remontada abre com o rascunho')
  cair = false
  assert.equal((await producaoDoDia.produzir([linha()])).ok, true)
  assert.ok(chamadas[0].chave)
  assert.equal(chamadas[1].chave, chamadas[0].chave)
})

await confere('a lista de compras gerada vale para as mesmas porções e data; mudou, pode gerar outra', () => {
  const linhas = [{ sku: 'i-1', porcoes: '12' }, { sku: 'i-2', porcoes: '0' }, { sku: 'i-3', porcoes: '' }]
  const gerada = assinaturaDoPlano(linhas, '2026-11-01')
  assert.equal(assinaturaDoPlano([{ sku: 'i-1', porcoes: ' 12 ' }, { sku: 'i-2', porcoes: '' }], '2026-11-01'), gerada, 'mesmas porções')
  assert.notEqual(assinaturaDoPlano([{ sku: 'i-1', porcoes: '13' }], '2026-11-01'), gerada, 'porção mudou')
  assert.notEqual(assinaturaDoPlano([...linhas, { sku: 'i-4', porcoes: '1' }], '2026-11-01'), gerada, 'prato a mais')
  assert.notEqual(assinaturaDoPlano(linhas, '2026-11-02'), gerada, 'data mudou')
})

await confere('os itens da lista gerada ficam guardados para a tela mostrar o que comprar', () => {
  assert.equal(comprasGuardadas(), null)
  const linhas = [{ sku: 'i-1', porcoes: '12' }]
  const itens = itensDeCompra(
    [{ insumoId: 's-1', nome: 'Molho', falta: 1.4, unidade: 'Kg' }],
    [{ id: 's-3', nome: 'Bandeja', comprar: true, minimo: 50, saldo: 20, unidade: 'Un' }],
  )
  const guardada = guardarCompras(linhas, '2026-11-01', { nome: 'Compras da produção de 01/11', itens })
  assert.equal(comprasGuardadas(), guardada)
  assert.equal(guardada.assinatura, assinaturaDoPlano(linhas, '2026-11-01'))
  assert.deepEqual(guardada.itens.map((i) => [i.texto, i.quantidade, i.unidade, i.categoria]), [['Molho', 1.4, 'Kg', 'Produção'], ['Bandeja', 30, 'Un', 'Mínimo']])
})

// === Telas ==============================================================================
// Mini-renderizador: componente-função guarda estado pelo caminho na árvore; o resto vira objeto.
// Trocar de rota no console desmonta a tela: aqui é `montar` de novo, com estado zerado.
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
    // Leitura da API e efeito mudam estado depois do render: alguns ciclos até assentar.
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
const botoes = (no, rotulo) => andar(no, (n) => n.type === 'Botao' && texto(n).includes(rotulo))
const campos = (no, tipo, rotulo) => andar(no, (n) => n.type === tipo && n.props.rotulo === rotulo)
const alertas = (no) => andar(no, (n) => n.props?.role === 'alert')
const links = (no) => andar(no, (n) => n.type === 'a').map((n) => [n.props.href, texto(n)])
const formulario = (no, marca) => andar(no, (n) => n.type === 'form' && (!marca || n.props['aria-label'] === marca || String(n.props.className).includes(marca)))[0]
const evento = { preventDefault() {} }
const digitar = (valor) => ({ target: { value: valor } })
const esperar = (ms) => new Promise((r) => setTimeout(r, ms))

let impressoes = 0
globalThis.window = { print: () => { impressoes += 1 } }
globalThis.__prova1585.catalogo = { cardapio: [{ sku: 'i-1', nome: 'Ravióli', porcoes: [] }, { sku: 'i-2', nome: 'Lasanha', porcoes: [] }] }

const AbaProducaoDoDiaApi = await tela('AbaProducaoDoDiaApi')
const cartoes = (no) => andar(no, (n) => n.type === 'li' && String(n.props.className).includes('loteProducao'))
const avisosDeLote = (no) => andar(no, (n) => n.type === 'output')
const folha = (t) => andar(t.renderizar(), (n) => n.type === 'FolhaImpressao')
// Preenche o cartão `indice` com o prato e as porções.
const preencher = (t, indice, sku, porcoes) => {
  campos(t.renderizar(), 'CampoSelecao', 'Prato')[indice].props.onChange(digitar(sku))
  campos(t.renderizar(), 'CampoTexto', 'Porções')[indice].props.onChange(digitar(porcoes))
  return t.renderizar()
}
const confirmar = async (t) => { await formulario(t.renderizar()).props.onSubmit(evento); return t.renderizar() }

await confere('Produção do dia: sair da tela e voltar não apaga o que ela digitou', () => {
  zerar()
  const antes = montar(AbaProducaoDoDiaApi)
  assert.equal(cartoes(antes.renderizar()).length, 1)
  preencher(antes, 0, 'i-1', '12')
  botoes(antes.arvore, 'Mais um prato')[0].props.onClick()
  antes.renderizar()
  const depois = montar(AbaProducaoDoDiaApi)
  assert.equal(cartoes(depois.renderizar()).length, 2)
  assert.equal(campos(depois.arvore, 'CampoTexto', 'Porções')[0].props.value, '12')
  assert.match(texto(cartoes(depois.arvore)[0]), /Ravióli/)
})

await confere('Produção do dia: o erro nomeia o prato, fica no formulário e nada é enviado', async () => {
  zerar()
  guardarRascunho([digitada('a', { porcoes: '12' }), digitada('b', { sku: '', porcoes: '' })])
  const chamadas = montarFetch()
  const t = montar(AbaProducaoDoDiaApi)
  await confirmar(t)
  assert.equal(texto(alertas(formulario(t.arvore))[0]), 'Prato 02: Escolha o prato.')
  preencher(t, 1, 'i-2', '')
  await confirmar(t)
  assert.equal(texto(alertas(formulario(t.arvore))[0]), 'Prato 02 (Lasanha): Quantas porções saíram?')
  assert.equal(chamadas.length, 0)
})

await confere('Produção do dia: confirmar mostra o lote e zera as linhas; outro lote soma; a volta mantém os botões', async () => {
  zerar()
  const chamadas = montarProducao(lote(1), lote(2, { totalEtiquetas: 6, avisos: ['Molho: faltaram 200 g.'] }))
  const t = montar(AbaProducaoDoDiaApi)
  preencher(t, 0, 'i-1', '2')
  await confirmar(t)
  assert.match(texto(avisosDeLote(t.arvore)[0]), /Lote LOT-261011-001 lançado: 2× Ravióli\./)
  assert.equal(cartoes(t.arvore).length, 1)
  assert.equal(campos(t.arvore, 'CampoTexto', 'Porções')[0].props.value, '')
  preencher(t, 0, 'i-1', '6')
  await confirmar(t)
  assert.deepEqual(avisosDeLote(t.arvore).map((o) => texto(o).match(/LOT-[\d-]+/)[0]), ['LOT-261011-002', 'LOT-261011-001'])
  assert.match(texto(avisosDeLote(t.arvore)[0]), /Molho: faltaram 200 g\./)
  assert.equal(chamadas.length, 2)
  const volta = montar(AbaProducaoDoDiaApi)
  assert.deepEqual(botoes(volta.renderizar(), 'Imprimir').map(texto), ['Imprimir 6 etiquetas', 'Imprimir 2 etiquetas'])
})

await confere('Produção do dia: cada lote imprime as próprias etiquetas, o erro fica no lote e dispensar tira', async () => {
  zerar()
  montarProducao(lote(1), lote(2, { totalEtiquetas: 6 }))
  await producaoDoDia.produzir([linha()])
  await producaoDoDia.produzir([linha({ porcoes: 6 })])
  let cair = false
  const etiquetasDe = (codigo, quantas) => ({ data: { etiquetas: Array.from({ length: quantas }, (_, i) => ({
    id: `${codigo}-${i}`, sequencial: i + 1, codigo: `C${i}`, produto: { nome: 'Ravióli', pesoG: 500, fichaAlergenos: [] },
    loteCodigo: codigo, loteValidadeEm: null, loteCriadoEm: '2026-10-11T12:00:00' })) } })
  const chamadas = montarFetch([
    [(m, u) => u.endsWith('/api/lotes/l-1/etiquetas/render'), () => (cair ? { cair } : etiquetasDe('LOT-261011-001', 2))],
    [(m, u) => u.endsWith('/api/lotes/l-2/etiquetas/render'), etiquetasDe('LOT-261011-002', 6)],
  ])
  impressoes = 0
  const t = montar(AbaProducaoDoDiaApi)
  await botoes(t.renderizar(), 'Imprimir 6 etiquetas')[0].props.onClick()
  await esperar(80)
  assert.match(texto(folha(t)[0]), /Lote LOT-261011-002 · 6\/6/)
  assert.equal(impressoes, 1)
  // Lote 1 com a rede fora: a folha do lote 2 sai, nada imprime e o erro aparece no aviso do lote 1.
  cair = true
  await botoes(t.arvore, 'Imprimir 2 etiquetas')[0].props.onClick()
  await esperar(80)
  assert.equal(folha(t).length, 0, 'não sobra a folha do lote anterior')
  assert.equal(impressoes, 1)
  const [doLote2, doLote1] = avisosDeLote(t.arvore)
  assert.match(texto(doLote1), /Não consegui buscar as etiquetas deste lote\. Sem conexão com o EasyStok\./)
  assert.doesNotMatch(texto(doLote2), /Não consegui/)
  assert.equal(alertas(formulario(t.arvore)).length, 0, 'o erro da etiqueta não vai para o fim do formulário')
  cair = false
  await botoes(t.arvore, 'Imprimir 2 etiquetas')[0].props.onClick()
  await esperar(80)
  assert.match(texto(folha(t)[0]), /Lote LOT-261011-001 · 2\/2/)
  assert.equal(impressoes, 2)
  assert.equal(chamadas.length, 3)
  // Dispensar o lote 1 leva a folha dele; o lote 2 continua.
  botoes(t.arvore, 'Dispensar')[1].props.onClick()
  assert.equal(folha(t).length, 0)
  assert.deepEqual(avisosDeLote(t.arvore).map((o) => texto(o).match(/LOT-[\d-]+/)[0]), ['LOT-261011-002'])
  assert.deepEqual(lotesLancados().map((l) => l.loteId), ['l-2'])
})

await confere('Produção do dia: envio que termina depois de sair e voltar mostra o lote e tira as linhas', async () => {
  zerar()
  let responder = null
  globalThis.fetch = () => new Promise((resolver) => { responder = () => resolver(new Response(JSON.stringify(lote(3)), { status: 201 })) })
  const antes = montar(AbaProducaoDoDiaApi)
  preencher(antes, 0, 'i-1', '4')
  const envio = formulario(antes.arvore).props.onSubmit(evento)
  for (let i = 0; i < 50 && !responder; i += 1) await esperar(0)
  const depois = montar(AbaProducaoDoDiaApi)
  assert.equal(campos(depois.renderizar(), 'CampoTexto', 'Porções')[0].props.value, '4', 'com o envio em voo, as linhas ainda estão lá')
  responder()
  await envio
  assert.equal(campos(depois.renderizar(), 'CampoTexto', 'Porções')[0].props.value, '')
  assert.match(texto(avisosDeLote(depois.arvore)[0]), /LOT-261011-003/)
})

await confere('Produção do dia: as porções vindas do Planejamento entram junto do que já estava digitado', () => {
  zerar()
  preencher(montar(AbaProducaoDoDiaApi), 0, 'i-1', '5')
  assert.equal(levarParaProducao([{ sku: 'i-2', porcoes: '8' }, { sku: 'i-9', porcoes: '0' }]), 1)
  const t = montar(AbaProducaoDoDiaApi)
  assert.deepEqual(cartoes(t.renderizar()).map((c) => texto(andar(c, (n) => n.type === 'h3')[0])), ['Ravióli', 'Lasanha'])
  assert.deepEqual(campos(t.arvore, 'CampoTexto', 'Porções').map((c) => c.props.value), ['5', '8'])
  assert.equal(campos(t.arvore, 'CampoTexto', 'Validade (dias)')[1].props.value, '5', 'a planejada ganha a validade padrão')
  zerar()
})

const insumo = (extra) => ({ unidade: 'G', saldo: 500, minimo: null, custo: null, abaixoDoMinimo: false, receitas: 0, porcoes: 0, embalagem: false, ...extra })
const LER_INSUMOS = (m, u) => m === 'GET' && u.endsWith('/producao/insumos')

await confere('Insumos: o aviso sai no formulário que o causou; sem formulário, no topo', async () => {
  const chamadas = montarFetch([
    [LER_INSUMOS, { data: [insumo({ produtoId: 's-1', nome: 'Molho sugo' }), insumo({ produtoId: 's-2', nome: 'Recheio' })] }],
    [(m) => m !== 'GET', { status: 409, error: { code: 'CONFLITO', message: 'Insumo em uso.' } }],
  ])
  const t = montar(await tela('AbaInsumosApi'))
  await t.assentar()
  const cadastro = () => formulario(t.arvore, 'Cadastrar insumo')
  const ajuste = () => formulario(t.arvore, 'ajusteInsumo')
  // Cadastro com mínimo inválido: o aviso fica dentro do formulário, antes do botão, e nada é enviado.
  campos(cadastro(), 'CampoTexto', 'Nome do insumo')[0].props.onChange(digitar('Massa'))
  t.renderizar()
  campos(cadastro(), 'CampoTexto', 'Mínimo (opcional)')[0].props.onChange(digitar('1,5'))
  t.renderizar()
  cadastro().props.onSubmit(evento)
  await t.assentar()
  assert.equal(alertas(t.arvore).length, 1)
  assert.equal(texto(alertas(cadastro())[0]), 'Não foi possível cadastrar o insumo. Mínimo em número inteiro, sem negativo. Fechar aviso')
  assert.equal(chamadas.filter((c) => c.metodo !== 'GET').length, 0)
  // Ajuste do segundo insumo recusado pela API: o aviso vai para o ajuste dele e sai do cadastro.
  botoes(t.arvore, 'Ajustar')[1].props.onClick()
  t.renderizar()
  ajuste().props.onSubmit(evento)
  await t.assentar()
  assert.equal(alertas(t.arvore).length, 1)
  assert.equal(texto(alertas(ajuste())[0]), 'Não foi possível salvar o ajuste. Insumo em uso. Fechar aviso')
  // Cancelar o ajuste fecha o aviso dele.
  botoes(ajuste(), 'Cancelar')[0].props.onClick()
  await t.assentar()
  assert.equal(alertas(t.arvore).length, 0)
  // "É embalagem" não tem formulário: o aviso sai no topo, fora das listas.
  botoes(t.arvore, 'É embalagem')[0].props.onClick()
  await t.assentar()
  assert.equal(alertas(t.arvore).length, 1)
  assert.equal(texto(alertas(t.arvore)[0]), 'Insumo em uso. Fechar')
  assert.equal(alertas(andar(t.arvore, (n) => n.type === 'ul' || n.type === 'form')).length, 0)
})

await confere('Planejamento: a lista gerada aparece na tela e o botão só destrava quando a porção muda', async () => {
  const chamadas = montarFetch([
    [(m, u) => m === 'GET' && u.includes('/producao/sugestao'), { data: { ate: '2026-10-12', pratos: [{ cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'Ravióli', minimo: 5, saldo: 1, agendados: 2, descoberto: 0, sugestao: 6 }] } }],
    [(m, u) => m === 'POST' && u.endsWith('/producao/planejamento'), { data: {
      itens: [{ produtoFinalId: 'p-1', status: 'Ok' }], tudoDisponivel: false, custoEstimadoTotal: 72,
      consolidado: [{ insumoId: 's-1', insumoNome: 'Molho', precisa: 2.4, unidadeReceita: 'Kg', saldo: 1000, unidadeSaldo: 'G', falta: 1.4, custoEstimado: 72 }] } }],
    [LER_INSUMOS, { data: [insumo({ produtoId: 's-3', nome: 'Bandeja', unidade: 'Un', saldo: 20, minimo: 50, abaixoDoMinimo: true })] }],
    [(m, u) => m === 'POST' && u.endsWith('/api/listas-compras/gerar'), { status: 201, data: { id: 'lc-1', nome: 'Compras da produção de 12/10' } }],
  ])
  const listasCriadas = () => chamadas.filter((c) => c.url.endsWith('/api/listas-compras/gerar')).length
  const AbaPlanejamentoApi = await tela('AbaPlanejamentoApi')
  let t = montar(AbaPlanejamentoApi)
  const calcular = async () => { await botoes(t.arvore, 'Calcular insumos')[0].props.onClick(); t.renderizar() }
  const gerar = () => andar(t.arvore, (n) => n.type === 'Botao' && n.props.icone === 'shopping-cart')[0]
  const lista = () => andar(t.arvore, (n) => n.type === 'section' && n.props['aria-label'] === 'Lista de compras gerada')
  await t.assentar()
  await calcular()
  assert.equal(texto(gerar()), 'Gerar lista de compras')
  assert.equal(gerar().props.disabled, false)
  assert.equal(lista().length, 0)
  await gerar().props.onClick()
  t.renderizar()
  assert.equal(listasCriadas(), 1)
  assert.equal(gerar().props.disabled, true)
  assert.equal(texto(lista()[0]), 'Compras da produção de 12/10Molhofalta para a produção1,4 KgBandejarepõe o mínimo30 UnPara gerar outra lista, mude as porções ou a data.')
  // Sair, voltar e calcular de novo sem mudar nada: a lista segue na tela e o botão travado.
  t = montar(AbaPlanejamentoApi)
  await t.assentar()
  await calcular()
  assert.equal(gerar().props.disabled, true)
  assert.equal(lista().length, 1)
  // Mudou a porção: a lista antiga sai e dá para gerar outra.
  campos(t.arvore, 'CampoTexto', 'Porções')[0].props.onChange(digitar('9'))
  t.renderizar()
  await calcular()
  assert.equal(gerar().props.disabled, false)
  assert.equal(lista().length, 0)
  await gerar().props.onClick()
  assert.equal(listasCriadas(), 2)
})

await confere('os textos de Estoque do dia, Receitas e Planejamento têm link para a tela citada', async () => {
  const PRODUCAO_DO_DIA = [hashDoModulo('producao', 'producao'), 'Produção do dia']
  assert.equal(rotaDaHash(PRODUCAO_DO_DIA[0], { fonteApi: true }).aba, 'producao-do-dia')
  assert.equal(rotaDaHash(hashDoModulo('producao', 'perdas'), { fonteApi: true }).aba, 'perdas')
  // Sem prato ligado ao estoque, as três telas mandam lançar a primeira produção.
  montarFetch()
  const vazia = async (nome) => { const t = montar(await tela(nome)); await t.assentar(); return links(t.arvore) }
  assert.deepEqual(await vazia('AbaEstoqueDoDiaApi'), [PRODUCAO_DO_DIA])
  assert.deepEqual(await vazia('AbaPlanejamentoApi'), [PRODUCAO_DO_DIA])
  assert.deepEqual(await vazia('AbaReceitasApi'), [PRODUCAO_DO_DIA, PRODUCAO_DO_DIA])
  // Contagem de prato com porção vencida guardada: o texto manda lançar em Perdas, com link.
  montarFetch([[(m, u) => m === 'GET' && u.endsWith('/producao/estoque-do-dia'), { data: { alertas: [], pratos: [{
    cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'Ravióli', saldo: 3, fisico: 5, descoberto: 0, lotes: [], porcoes: [] }] } }]])
  const t = montar(await tela('AbaEstoqueDoDiaApi'))
  await t.assentar()
  botoes(t.arvore, 'Ajustar')[0].props.onClick()
  assert.deepEqual(links(t.renderizar()), [[hashDoModulo('producao', 'perdas'), 'Perdas']])
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
