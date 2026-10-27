/* eslint-disable no-console */
// Prova da #2081: o registro de produção do console igual ao do APK piloto (banca #2073, revisor A e E).
//   - A4: etiqueta 80x40 com QR e Code 128 do código da etiqueta, empresa no topo; a prévia abre sozinha;
//   - A8/A10: lote ativo diz origem, quem registrou e fabricação, e reimprime (o do aparelho não);
//   - A6: "Conectar aparelho" gera o código pela API, sem o código na URL nem no log;
//   - A5/E7: perda sem custo diz "sem custo" e fica fora do total, que é o número da tela;
//   - A11: o campo de porções fica à vista no Conferir; A14: sem foto na grade, sem reserva de foto;
//   - A16: textos repetidos (porção no nome, "Perda · Perda no preparo"); E9: custo "1.500" é mil e
//     quinhentos; E11: a ação principal de Insumos e Perdas é um botão no topo, não uma barra.
//
//   node ferramentas/prova-2081-producao-paridade.mjs

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

// Hooks de mentira (como na prova da #1585): o estado mora no `host` do componente que renderiza.
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
  useRef(valor) { return host.valores[host.i++] ??= { current: valor } },
  useEffect(fn, deps) {
    const atual = host, i = atual.i++, antes = atual.valores[i]
    if (antes && deps && !deps.some((d, j) => !Object.is(d, antes.deps[j]))) return
    atual.valores[i] = { deps, limpar: antes?.limpar }
    atual.efeitos.push(() => { antes?.limpar?.(); atual.valores[i].limpar = fn() })
  },
}
globalThis.__prova2081 = { hooks, catalogo: { cardapio: [] } }
const inline = (source) => ({ format: 'module', shortCircuit: true, source })

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador === 'react') return { url: 'data:text/javascript,export const {useState,useMemo,useCallback,useEffect,useRef}=globalThis.__prova2081.hooks', shortCircuit: true }
    if (especificador === 'react/jsx-runtime') return { url: 'data:text/javascript,export const jsx=(type,props,key)=>({type,props,key});export const jsxs=jsx;export const Fragment="fragment"', shortCircuit: true }
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(especificador + ext, contexto) } catch { /* próxima extensão */ } }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (telasCompiladas.has(url)) return inline(telasCompiladas.get(url))
    if (url.endsWith('/infra/fonteDados.js')) return inline("export const FONTE_API = true\nexport const API_BASE = ''\n")
    if (url.endsWith('/aplicacao/contextos.js')) return inline('export const useCatalogo=()=>globalThis.__prova2081.catalogo')
    if (url.includes('/aplicacao/acessoModulos.')) return inline('export const useAcessoModulos=()=>({acoes:{editarProducao:true},permite:()=>true})')
    if (url.endsWith('.css')) return inline('export default new Proxy({},{get:(_,p)=>String(p)})')
    if (url.endsWith('.json')) return inline('export default ' + readFileSync(new URL(url), 'utf8'))
    // Componente de fora das telas (Botao, Modal, a etiqueta) vira o próprio nome: aparece na árvore sem rodar.
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

// Cada rota é [teste(metodo, url), resposta]. A resposta pode ser o corpo cru (`cru`) fora do envelope.
function montarFetch(rotas = []) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null })
    for (const [teste, resposta] of rotas) {
      if (!teste(method, url)) continue
      const { status = 200, data = null, cru, error } = typeof resposta === 'function' ? resposta() : resposta
      const corpo = status === 403 ? '' : JSON.stringify(cru !== undefined ? cru : error ? { error } : { data })
      return new Response(corpo, { status })
    }
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  return chamadas
}

// Sessão de gerente para as rotas autenticadas.
const daqui = Date.now() + 3600_000
globalThis.sessionStorage = (() => {
  const mapa = new Map([['easystok.sessao', JSON.stringify({ token: 'x.e30.y', expiraEm: daqui, venceEm: daqui, empresa: { id: 'emp-1' }, usuario: { id: 'u-1' } })]])
  return { getItem: (k) => mapa.get(k) ?? null, setItem: (k, v) => mapa.set(k, v), removeItem: (k) => mapa.delete(k) }
})()

const ETIQUETAS = (codigoLote = 'LOT-261026-001', quantas = 2) => ({ data: {
  layoutJson: '{"v":1}', empresa: { nome: 'Casa da Baba', logoUrl: null },
  etiquetas: Array.from({ length: quantas }, (_, i) => ({
    id: `e-${i + 1}`, sequencial: i + 1, codigo: `${codigoLote}-000${i + 1}`,
    produto: { nome: 'Lasanha Bolonhesa Clássica 1 kg', pesoG: 1000, fichaAlergenos: [] },
    loteCodigo: codigoLote, loteValidadeEm: '2026-10-31T23:06:08Z', loteCriadoEm: '2026-10-26T23:06:08Z' })),
} })

// === A4: códigos e folha da etiqueta ==================================================
await confere('A4: Code 128 (conjunto B) com início, verificador e fim, como o PWA', async () => {
  const { valoresCode128, barrasDoCode128 } = await import('../src/features/gestao/producao/etiqueta/codigos.js')
  const valores = valoresCode128('LOT-261026-001-0001')
  assert.equal(valores[0], 104, 'começa no conjunto B')
  assert.equal(valores.at(-1), 106, 'termina no fim')
  const dados = valores.slice(1, -2)
  assert.deepEqual(dados.map((v) => String.fromCharCode(v + 32)).join(''), 'LOT-261026-001-0001')
  assert.equal(valores.at(-2), (104 + dados.reduce((s, v, i) => s + v * (i + 1), 0)) % 103)
  const { barras, total } = barrasDoCode128('LOT-261026-001-0001')
  // 22 símbolos de 11 módulos, o fim de 13 e 10 de silêncio de cada lado.
  assert.equal(total, 21 * 11 + 13 + 20)
  assert.equal(barras.reduce((s, b) => s + b.largura, 0) % 1, 0)
  assert.equal(barras[0].x, 10)
})

await confere('A4: QR do código da etiqueta, com os três padrões de posição', async () => {
  const { matrizDoQr, caminhoDoQr } = await import('../src/features/gestao/producao/etiqueta/codigos.js')
  const m = matrizDoQr('LOT-261026-001-0001')
  assert.equal(m.length, 25, 'versão 2 no nível M')
  const quadro = (y, x) => [0, 6].every((d) => m[y][x + d] && m[y + d][x] && m[y + 6][x + d] && m[y + d][x + 6])
  assert.ok(quadro(0, 0) && quadro(0, 18) && quadro(18, 0))
  assert.match(caminhoDoQr(m, 2), /^M2 2h1v1h-1z/)
})

await confere('A4: a folha leva a empresa e as etiquetas; a produção busca a folha do lote', async () => {
  const { folhaDeEtiquetasDaApi } = await import('../src/infra/api/producaoApi.js')
  const folha = folhaDeEtiquetasDaApi(ETIQUETAS().data)
  assert.equal(folha.empresa, 'Casa da Baba')
  assert.deepEqual(folha.etiquetas.map((e) => [e.codigo, e.lote, e.pesoG]), [['LOT-261026-001-0001', 'LOT-261026-001', 1000], ['LOT-261026-001-0002', 'LOT-261026-001', 1000]])
  const { criarProducaoDoDia } = await import('../src/aplicacao/producaoDoDia.js')
  montarFetch([[(m, u) => u.endsWith('/api/lotes/l-1/etiquetas/render'), ETIQUETAS()]])
  assert.equal((await criarProducaoDoDia().folhaDeEtiquetas('l-1')).etiquetas.length, 2)
})

// === A8/A10: lotes ativos ================================================================
const ESTOQUE = (m, u) => m === 'GET' && u.endsWith('/producao/estoque-do-dia')
const LOTES = (m, u) => m === 'GET' && u.includes('/api/lotes?')
const estoque = { data: { pratos: [{ cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'Lasanha Bolonhesa Clássica 1 kg', porcao: '1 kg', saldo: 13, descoberto: 0, fisico: 13, porcoes: [],
  lotes: [
    { codigo: 'LOT-251026-200000', quantidade: 2, validadeEm: '2026-10-31T00:00:00Z', diasParaVencer: 5, vencendo: false, vencido: false },
    { codigo: 'LOT-261026-001', quantidade: 11, validadeEm: '2026-10-31T00:00:00Z', diasParaVencer: 5, vencendo: false, vencido: false },
  ] }], alertas: [] } }
const lotes = { data: [
  { id: 'l-pwa', codigo: 'LOT-251026-200000', origem: 'mobile', operadorNome: 'Rosa', operadorUserId: null, dataProducao: '2026-10-26T01:30:00Z' },
  { id: 'l-con', codigo: 'LOT-261026-001', origem: 'producao', operadorNome: null, operadorUserId: 'u-1', dataProducao: '2026-10-26T23:06:08Z' },
] }

await confere('A10: lote ativo diz de onde veio, quem registrou e quando foi fabricado (no fuso da loja)', async () => {
  const { lerEstoqueDoDia, podeReimprimir } = await import('../src/aplicacao/estoqueDoDia.js')
  montarFetch([[ESTOQUE, estoque], [LOTES, lotes]])
  const [doAparelho, doConsole] = (await lerEstoqueDoDia()).pratos[0].lotes
  assert.equal(doAparelho.origem, 'Aparelho · Rosa · fab. 25/10', '01:30 UTC é 22:30 do dia 25 em Brasília')
  assert.equal(doConsole.origem, 'Console · você · fab. 26/10')
  assert.equal(doConsole.id, 'l-con')
  assert.equal(podeReimprimir(doConsole), true)
  assert.equal(podeReimprimir(doAparelho), false, 'a etiqueta do aparelho não existe no EasyStok')
})

await confere('A10: sem a lista de lotes o estoque continua servindo, sem reimprimir', async () => {
  const { lerEstoqueDoDia, podeReimprimir } = await import('../src/aplicacao/estoqueDoDia.js')
  montarFetch([[ESTOQUE, estoque], [LOTES, { status: 500, error: { code: 'X', message: 'falhou' } }]])
  const r = await lerEstoqueDoDia()
  assert.equal(r.pratos[0].lotes.length, 2)
  assert.equal(r.pratos[0].lotes.some(podeReimprimir), false)
})

await confere('A16: a porção que o nome já diz não se repete embaixo', async () => {
  const { porcaoQueNaoRepete } = await import('../src/aplicacao/estoqueDoDia.js')
  assert.equal(porcaoQueNaoRepete('Lasanha Bolonhesa Clássica 1 kg', '1 kg'), '')
  assert.equal(porcaoQueNaoRepete('Ravióli', '400 g'), '400 g')
})

// === A6: pareamento ======================================================================
await confere('A6: a dona lê as lojas e gera o código pelo corpo da resposta; nada vai para a URL nem para o log', async () => {
  const { lojasParaParear, gerarCodigo } = await import('../src/aplicacao/aparelhos.js')
  const chamadas = montarFetch([
    [(m, u) => m === 'GET' && u.endsWith('/api/lojas'), { data: [{ id: 'lj-1', nome: 'Centro', ativa: true }, { id: 'lj-2', nome: 'Antiga', ativa: false }] }],
    [(m, u) => m === 'POST' && u.endsWith('/api/mobile/devices/pair-codes'), { cru: { pairingCode: '855799', expiresAt: '2026-10-26T23:22:35Z', deviceRecordId: 'pending-1' } }],
  ])
  const registros = []
  const original = { log: console.log, info: console.info, warn: console.warn, error: console.error, debug: console.debug }
  for (const k of Object.keys(original)) console[k] = (...a) => registros.push(a.join(' '))
  let r
  try {
    const { empresaId, lojas } = await lojasParaParear()
    assert.deepEqual(lojas, [{ id: 'lj-1', nome: 'Centro' }])
    r = await gerarCodigo({ empresaId, lojaId: lojas[0].id })
  } finally { Object.assign(console, original) }
  assert.deepEqual(r, { codigo: '855799', expiraEm: '2026-10-26T23:22:35Z' })
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.deepEqual(post.corpo, { empresaId: 'emp-1', lojaId: 'lj-1' })
  assert.ok(chamadas.every((c) => !c.url.includes('855799')), 'o código não vai na URL')
  assert.ok(registros.every((l) => !l.includes('855799')), 'o código não vai para o log')
})

await confere('A6: a gerente sem Configurações usa a loja dos aparelhos já pareados', async () => {
  const { lojasParaParear } = await import('../src/aplicacao/aparelhos.js')
  montarFetch([
    [(m, u) => m === 'GET' && u.endsWith('/api/lojas'), { status: 403 }],
    [(m, u) => m === 'GET' && u.includes('/api/mobile/devices?empresaId=emp-1'), { cru: [{ id: 'd-1', lojaId: 'lj-1', revoked: false }, { id: 'd-2', lojaId: 'lj-1', revoked: false }] }],
  ])
  assert.deepEqual((await lojasParaParear()).lojas.map((l) => l.id), ['lj-1'])
  montarFetch([[(m, u) => m === 'GET' && u.endsWith('/api/lojas'), { status: 403 }], [(m, u) => u.includes('/api/mobile/devices'), { cru: [] }]])
  await assert.rejects(lojasParaParear(), /Peça a quem administra/)
})

await confere('A6: o prazo do código em minutos e segundos; vencido não mostra prazo', async () => {
  const { tempoRestante, codigoEmGrupos } = await import('../src/aplicacao/aparelhos.js')
  const agora = Date.parse('2026-10-26T23:12:37Z')
  assert.equal(tempoRestante('2026-10-26T23:22:35Z', agora), '9:58')
  assert.equal(tempoRestante('2026-10-26T23:12:00Z', agora), null)
  assert.equal(codigoEmGrupos('855799'), '855 799')
})

// === A5/E7 e A16: perdas =================================================================
const lanc = (extra) => ({ id: 'm', data: '2026-10-26T23:08:48Z', produtoId: 'p', nome: 'Lasanha', lote: 'LOT-261026-001', quantidade: 1, valor: 0, motivo: 'PerdaNoPreparo', descricao: 'Perda · Perda no preparo', desfeita: false, ...extra })

await confere('A5: perda sem custo é "sem custo", não R$ 0,00, e conta à parte', async () => {
  const { semCusto, perdasSemCusto, textoDaPerda } = await import('../src/aplicacao/perdas.js')
  assert.equal(semCusto(lanc()), true)
  assert.equal(semCusto(lanc({ valor: 8.7 })), false)
  assert.equal(semCusto(lanc({ desfeita: true })), false)
  assert.equal(perdasSemCusto({ lancamentos: [lanc(), lanc({ quantidade: 12 }), lanc({ valor: 10.8 })] }), 2)
  assert.equal(textoDaPerda(lanc()), 'Perda no preparo', 'o motivo uma vez só')
  assert.equal(textoDaPerda(lanc({ motivo: 'Outro', descricao: 'Quebra da cadeia fria' })), 'Outro · Quebra da cadeia fria')
})

// === E9: custo do insumo ================================================================
await confere('E9: o custo lê "1.500" como mil e quinhentos e guarda as casas do grama', async () => {
  const { lerCusto, criarGestaoInsumos } = await import('../src/aplicacao/insumos.js')
  assert.equal(lerCusto('1.500'), 1500)
  assert.equal(lerCusto('1.500,25'), 1500.25)
  assert.equal(lerCusto('12,50'), 12.5)
  assert.equal(lerCusto('0,005'), 0.005)
  assert.equal(lerCusto('R$ 2,125'), 2.125)
  assert.equal(lerCusto('12.5'), 12.5)
  assert.equal(lerCusto(''), null)
  assert.ok(Number.isNaN(lerCusto('abc')))
  const chamadas = montarFetch([[(m) => m === 'POST', { data: {} }]])
  await criarGestaoInsumos({ recarregar: async () => {}, aoErro: (e) => { throw new Error(e) } }).criar({ nome: 'Molho', unidade: 'Kg', minimo: '', custo: '1.500' })
  assert.equal(chamadas.find((c) => c.metodo === 'POST').corpo.custo, 1500)
})

// === A14: passos ========================================================================
await confere('A14: os passos recolhem depois do primeiro lote (marca no navegador)', async () => {
  const { passosRecolhidos, marcarPassosVistos } = await import('../src/aplicacao/producaoDoDia.js')
  const mapa = new Map()
  globalThis.localStorage = { getItem: (k) => mapa.get(k) ?? null, setItem: (k, v) => mapa.set(k, v) }
  assert.equal(passosRecolhidos(), false)
  marcarPassosVistos()
  assert.equal(passosRecolhidos(), true)
  globalThis.localStorage = { getItem: () => { throw new Error('bloqueado') }, setItem: () => { throw new Error('bloqueado') } }
  assert.equal(passosRecolhidos(), false, 'sem armazenamento, os passos ficam abertos')
  delete globalThis.localStorage
})

// === Telas ===============================================================================
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
  andar(no.props?.rodape, fn, achados)
  return andar(no.props?.children, fn, achados)
}
const texto = (no) => (no == null ? '' : typeof no !== 'object' ? String(no) : Array.isArray(no) ? no.map(texto).join('') : texto(no.props?.children))
const doTipo = (no, tipo) => andar(no, (n) => n.type === tipo)
const daClasse = (no, classe) => andar(no, (n) => String(n.props?.className ?? '').split(' ').includes(classe))
const botao = (no, rotulo) => doTipo(no, 'Botao').find((b) => texto(b) === rotulo)
const evento = { preventDefault() {} }
globalThis.window = { print: () => {} }
const tela = async (nome) => (await import(new URL(`${nome}.jsx`, pastaDasTelas).href))[nome]

await confere('A4: confirmar a produção abre a prévia da etiqueta de verdade, que imprime o lote todo', async () => {
  globalThis.__prova2081.catalogo = { cardapio: [{ sku: 'i-1', nome: 'Lasanha Bolonhesa Clássica 1 kg', porcao: '1 kg', porcoes: [] }] }
  montarFetch([
    [(m, u) => m === 'POST' && u.endsWith('/api/atendimento/producao'), { status: 201, data: { loteId: 'l-1', codigoLote: 'LOT-261026-001', totalEtiquetas: 2, pratos: [{ cardapioItemId: 'i-1', nome: 'Lasanha', porcoes: 2, sobraG: null }] } }],
    [(m, u) => u.endsWith('/api/lotes/l-1/etiquetas/render'), ETIQUETAS()],
  ])
  const t = montar(await tela('AbaProducaoDoDiaApi'))
  await t.assentar()
  andar(t.arvore, (n) => n.type === 'button' && String(n.props['aria-label']).startsWith('Somar 1 porção'))[0].props.onClick()
  andar(t.renderizar(), (n) => n.type === 'button' && String(n.props['aria-label']).startsWith('Somar 1 porção'))[0].props.onClick()
  botao(t.renderizar(), 'Conferir produção').props.onClick()
  await doTipo(t.renderizar(), 'form')[0].props.onSubmit(evento)
  await t.assentar()
  const previa = doTipo(t.arvore, 'Modal').find((m) => m.props.titulo === 'Etiquetas do lote LOT-261026-001')
  assert.ok(previa, 'a prévia abre sozinha depois do lote')
  const [etiqueta] = doTipo(previa, 'EtiquetaDeProducao')
  assert.equal(etiqueta.props.empresa, 'Casa da Baba')
  assert.equal(etiqueta.props.etiqueta.codigo, 'LOT-261026-001-0001')
  assert.ok(doTipo(previa, 'Botao').some((b) => texto(b) === 'Imprimir 2 etiquetas' && !b.props.disabled))
  const folha = doTipo(t.arvore, 'FolhaImpressao')[0]
  assert.equal(doTipo(folha, 'EtiquetaDeProducao').length, 2, 'uma etiqueta 80x40 por porção na folha')
})

await confere('A11: no Conferir, as porções ficam à vista fora do "Ajustar"; A14: sem foto na grade, sem reserva', async () => {
  globalThis.__prova2081.catalogo = { cardapio: [{ sku: 'i-1', nome: 'Talharim', porcao: '500 g', foto: null, porcoes: [] }, { sku: 'i-2', nome: 'Molho', porcao: '300 g', foto: null, porcoes: [] }] }
  montarFetch()
  const t = montar(await tela('AbaProducaoDoDiaApi'))
  await t.assentar()
  assert.equal(daClasse(t.arvore, 'fotoDoPrato').length, 0, 'sem foto em nenhum prato, o cartão é compacto')
  andar(t.arvore, (n) => n.type === 'button' && String(n.props['aria-label']).startsWith('Somar 1 porção'))[0].props.onClick()
  botao(t.renderizar(), 'Conferir produção').props.onClick()
  const item = daClasse(t.renderizar(), 'itemDoLote')[0]
  const porcoes = doTipo(item, 'CampoTexto').find((c) => c.props.rotulo === 'Porções')
  assert.ok(porcoes, 'tem o campo de porções')
  assert.equal(doTipo(doTipo(item, 'details')[0], 'CampoTexto').some((c) => c.props.rotulo === 'Porções'), false, 'fora do Ajustar')
  globalThis.__prova2081.catalogo = { cardapio: [{ sku: 'i-1', nome: 'Talharim', porcao: '500 g', foto: '/midia/t.jpg', porcoes: [] }, { sku: 'i-2', nome: 'Molho', porcao: '300 g', foto: null, porcoes: [] }] }
  const comFoto = montar(await tela('AbaProducaoDoDiaApi'))
  await comFoto.assentar()
  assert.equal(daClasse(comFoto.arvore, 'fotoDoPrato').length, 2, 'com foto na grade, todos reservam o lugar (#1809)')
})

await confere('A8/A10: Estoque do dia mostra a origem e reimprime o lote do console, não o do aparelho', async () => {
  const chamadas = montarFetch([[ESTOQUE, estoque], [LOTES, lotes], [(m, u) => u.endsWith('/api/lotes/l-con/etiquetas/render'), ETIQUETAS('LOT-261026-001', 11)]])
  let impressoes = 0
  globalThis.window = { print: () => { impressoes += 1 } }
  const t = montar(await tela('AbaEstoqueDoDiaApi'))
  await t.assentar()
  const linhas = andar(t.arvore, (n) => n.type === 'li' && /LOT-/.test(texto(n)))
  assert.match(texto(linhas.find((l) => /LOT-251026-200000/.test(texto(l)))), /Aparelho · Rosa · fab\. 25\/10/)
  assert.match(texto(linhas.find((l) => /LOT-261026-001/.test(texto(l)))), /Console · você · fab\. 26\/10/)
  const reimprimir = doTipo(t.arvore, 'Botao').filter((b) => texto(b) === 'Reimprimir etiquetas')
  assert.equal(reimprimir.length, 1, 'só o lote do console')
  await reimprimir[0].props.onClick()
  await new Promise((r) => setTimeout(r, 80))
  const folha = doTipo(t.renderizar(), 'FolhaImpressao')[0]
  assert.equal(doTipo(folha, 'EtiquetaDeProducao').length, 11)
  assert.equal(impressoes, 1)
  assert.ok(chamadas.some((c) => c.url.endsWith('/api/lotes/l-con/etiquetas/render')))
  assert.doesNotMatch(texto(andar(t.arvore, (n) => n.type === 'li' && /Lasanha Bolonhesa Clássica 1 kg/.test(texto(n)) && !/LOT-/.test(texto(n)))[0]), /1 kg1 kg/, 'A16: a porção não se repete')
})

await confere('A5/E7/E11: Perdas mostra o total em destaque, "sem custo" na linha e o botão no topo', async () => {
  montarFetch([
    [(m, u) => m === 'GET' && u.includes('/producao/perdas/vencidos'), { data: [] }],
    [(m, u) => m === 'GET' && u.includes('/producao/perdas'), { data: { de: '2026-10-20', ate: '2026-10-26', valor: 19.5, porMotivo: [{ motivo: 'PerdaNoPreparo', rotulo: 'Perda no preparo', quantidade: 13, valor: 0 }], porProduto: [],
      lancamentos: [{ movimentacaoId: 'm-1', data: '2026-10-26T23:08:48Z', produtoId: 'p', produto: 'Lasanha', lote: 'LOT-1', quantidade: 1, valor: 0, motivo: 'PerdaNoPreparo', descricao: 'Perda · Perda no preparo', desfeita: false },
        { movimentacaoId: 'm-2', data: '2026-10-25T22:51:00Z', produtoId: 'q', produto: 'Massa', lote: 'LOT-2', quantidade: 2, valor: 10.8, motivo: 'Outro', descricao: 'Quebra', desfeita: false }] } }],
  ])
  const t = montar(await tela('AbaPerdasApi'))
  await t.assentar()
  const total = andar(t.arvore, (n) => n.type === 'dl' && n.props['aria-label'] === 'Total do período')[0]
  assert.match(texto(total), /R\$\s19,50/)
  assert.match(texto(total), /perda sem custo cadastrado, fora do total1$/)
  const [semCusto, comCusto] = andar(t.arvore, (n) => n.type === 'li' && /Lasanha|Massa/.test(texto(n)))
  assert.match(texto(semCusto), /sem custo/)
  assert.doesNotMatch(texto(semCusto), /R\$\s0,00/)
  assert.doesNotMatch(texto(semCusto), /Perda · Perda no preparo/, 'A16: o motivo uma vez')
  assert.match(texto(comCusto), /R\$\s10,80/)
  const topo = daClasse(t.arvore, 'topo')[0]
  assert.ok(doTipo(topo, 'Botao').some((b) => texto(b) === 'Lançar perda'), 'o botão fica no topo, ao lado do texto')
})

await confere('E11: Insumos tem "Cadastrar insumo" no topo, não uma barra solta no fim', async () => {
  montarFetch([[(m, u) => m === 'GET' && u.includes('/producao/insumos'), { data: [] }]])
  const t = montar(await tela('AbaInsumosApi'))
  await t.assentar()
  const topo = daClasse(t.arvore, 'topo')[0]
  assert.ok(doTipo(topo, 'Botao').some((b) => texto(b) === 'Cadastrar insumo'))
  assert.equal(doTipo(t.arvore, 'Botao').filter((b) => texto(b) === 'Cadastrar insumo').length, 1)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
