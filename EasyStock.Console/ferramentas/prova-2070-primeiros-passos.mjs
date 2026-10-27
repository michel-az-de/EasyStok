// Prova da #2070, Primeiros passos no console (seção 3 da especificação):
//
//   A  domínio puro: o mapa de passos, a retomada, a revisão, o progresso, a pendência e deveAbrirSozinho;
//   B  catálogo e acesso: a tela, as telas novas de Configurações e Entregas, a ação estrita e o ModalGestao sem ela;
//   C  rota: o passo no terceiro segmento só para esta tela, validado contra o mapa, e hashDoPasso;
//   D  montagem real da tela com a API simulada no fetch (o `chamarApi` é o de verdade): "Conferi, seguir"
//      bloqueado com pendência (do formulário novo, do contexto e da zona de frete real), "Fazer depois" faz o PUT,
//      recarregar retoma do primeiro passo em aberto, concluir, adiar, boas-vindas e o cartão do hall;
//   E  o catálogo C# de passos (EasyStock.Domain), quando existe neste worktree, bate com o mapa nos dois sentidos;
//   F  a entrada do App: só abre sozinho do hall, uma vez por visita e com a chave; nada disso na demonstração.
//
// Pixels e as larguras de 360, 768 e 1280 px ficam para o laboratório no navegador.
//
//   node ferramentas/prova-2070-primeiros-passos.mjs

import assert from 'node:assert/strict'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { transformWithOxc } from 'vite'

const src = (caminho) => new URL(`../src/${caminho}`, import.meta.url)
const fonte = (caminho) => readFileSync(src(caminho), 'utf8').replaceAll(String.fromCharCode(13), '')

// Contrato da API (GET api/configuracao-inicial): os ids, nesta ordem.
const IDS_DO_CONTRATO = [
  'boas-vindas', 'horarios', 'site-e-impressos', 'cardapio', 'janelas', 'frete', 'equipe',
  'dados-da-loja', 'atendimento', 'respostas', 'agente', 'producao', 'estoque', 'entregadores', 'impressora', 'avisos',
  'pronto',
]

// --- Ambiente: React mínimo que renderiza a árvore inteira, com hooks por instância ---------------

const COMPONENTES_REAIS = [
  'features/gestao/configuracao-inicial/TelaConfiguracaoInicial', 'features/gestao/configuracao-inicial/PassoBoasVindas',
  'features/gestao/configuracao-inicial/PassoPronto', 'features/gestao/configuracao-inicial/CartaoPrimeirosPassos',
  'features/gestao/ModalGestao', 'features/gestao/PartesDoAjuste', 'features/gestao/atendimento/AbaAtendimento',
  'features/entregas/CadastroEntregaApi', 'features/entregas/JanelasDaLoja', 'features/entregas/FreteRaioDaLoja',
]
const fontes = new Map()
for (const caminho of COMPONENTES_REAIS) {
  const arquivo = src(`${caminho}.jsx`)
  let codigo = ''
  try { codigo = readFileSync(arquivo, 'utf8') } catch { /* ainda não existe: a prova acusa abaixo */ }
  fontes.set(arquivo.href, (await transformWithOxc(codigo, arquivo.pathname, { jsx: { runtime: 'automatic' } })).code)
}

const iguais = (a, b) => Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => Object.is(v, b[i]))
let emRender = null
let contadorDeId = 0
const g = globalThis.__prova2070pp = {}
g.hooks = {
  createContext: (inicial) => ({ inicial }),
  useContext: (contexto) => contexto.inicial,
  useState(inicial) {
    const h = emRender, i = h.i++
    if (!(i in h.v)) h.v[i] = typeof inicial === 'function' ? inicial() : inicial
    return [h.v[i], (valor) => {
      if (h.morta) return
      const novo = typeof valor === 'function' ? valor(h.v[i]) : valor
      if (!Object.is(novo, h.v[i])) { h.v[i] = novo; h.raiz.sujo = true }
    }]
  },
  useRef(inicial) { const h = emRender, i = h.i++; return (h.v[i] ??= { current: inicial }) },
  useId() { const h = emRender, i = h.i++; return (h.v[i] ??= `id${++contadorDeId}`) },
  useMemo(fn, deps) {
    const h = emRender, i = h.i++, anterior = h.v[i]
    if (!anterior || !iguais(anterior.deps, deps)) h.v[i] = { valor: fn(), deps }
    return h.v[i].valor
  },
  useCallback(fn, deps) { return g.hooks.useMemo(() => fn, deps) },
  useEffect(fn, deps) {
    const h = emRender, i = h.i++, anterior = h.efeitos.get(i)
    if (!anterior || deps === undefined || !iguais(anterior.deps, deps)) {
      h.pendentes.push(() => { anterior?.limpar?.(); h.efeitos.set(i, { deps, limpar: fn() }) })
    }
  },
}
g.hooks.useLayoutEffect = g.hooks.useEffect
const ehElemento = (valor) => Array.isArray(valor) ? valor.some(ehElemento) : Boolean(valor && typeof valor === 'object' && 'type' in valor && 'props' in valor)

class Raiz {
  constructor(elemento) { this.elemento = elemento; this.instancias = new Map(); this.renderizar() }
  renderizar() {
    g.raizAtual = this
    let voltas = 0
    do {
      assert.ok(voltas++ < 40, 'sem ciclo de renderização')
      this.sujo = false; this.vistas = new Set(); this.ordem = []
      this.arvore = this.expandir(this.elemento, 'r')
      for (const [chave, inst] of this.instancias) {
        if (this.vistas.has(chave)) continue
        inst.morta = true; inst.efeitos.forEach((e) => e.limpar?.()); this.instancias.delete(chave)
      }
      for (const inst of this.ordem) inst.pendentes.splice(0).forEach((fn) => fn())
    } while (this.sujo)
    return this.arvore
  }
  expandir(el, caminho) {
    if (Array.isArray(el)) return el.map((e, i) => this.expandir(e, `${caminho}[${e?.key ?? i}]`))
    if (el === null || typeof el !== 'object') return el
    if (typeof el.type === 'function') {
      const chave = `${caminho}/${el.type.name}${el.key == null ? '' : `#${el.key}`}`
      let inst = this.instancias.get(chave)
      if (!inst) { inst = { v: [], efeitos: new Map(), pendentes: [], raiz: this }; this.instancias.set(chave, inst) }
      this.vistas.add(chave); inst.i = 0; inst.pendentes = []
      const anterior = emRender; emRender = inst
      let saida
      try { saida = el.type(el.props ?? {}) } finally { emRender = anterior }
      this.ordem.push(inst)
      return this.expandir(saida, chave)
    }
    const props = { ...el.props }
    for (const [nome, valor] of Object.entries(props)) {
      if (nome === 'children' || ehElemento(valor)) props[nome] = this.expandir(valor, `${caminho}.${nome}`)
    }
    return { type: el.type, key: el.key, props }
  }
  async atualizar(voltas = 20) {
    for (let i = 0; i < voltas; i++) { await new Promise((r) => setImmediate(r)); this.renderizar() }
    return this.arvore
  }
  desmontar() {
    for (const inst of this.instancias.values()) { inst.morta = true; inst.efeitos.forEach((e) => e.limpar?.()) }
    this.instancias.clear()
  }
}

// --- Fronteiras: sessão, base da API, navegador e a rede ------------------------------------------

const SESSAO = { token: 'token', persistente: false, expiraEm: Date.now() + 3_600_000, empresa: { id: 'emp-1', nome: 'Casa' }, usuario: { id: 'usu-1', nome: 'Ana Teste' } }
g.sessao = SESSAO
g.contexto = { acoes: {}, estado: {} }
g.acesso = { acoes: {} }
const ouvintes = new Map()
const janela = globalThis.window = {
  location: {
    hash: '',
    replace(destino) { janela.location.substituicoes.push(destino); janela.location.hash = destino },
    substituicoes: [],
  },
  addEventListener: (nome, fn) => { ouvintes.set(nome, [...(ouvintes.get(nome) ?? []), fn]) },
  removeEventListener: (nome, fn) => { ouvintes.set(nome, (ouvintes.get(nome) ?? []).filter((f) => f !== fn)) },
  dispatchEvent: () => true,
  matchMedia: () => ({ matches: true, addEventListener() {}, removeEventListener() {} }),
}
globalThis.requestAnimationFrame = (fn) => { fn(); return 0 }
globalThis.document = { getElementById: () => null, querySelector: () => null }

const inline = (source) => ({ format: 'module', shortCircuit: true, source })
registerHooks({
  resolve(spec, ctx, next) {
    if (spec === 'react') return { url: 'data:text/javascript,const h=globalThis.__prova2070pp.hooks;export const {createContext,useContext,useState,useRef,useCallback,useEffect,useLayoutEffect,useMemo,useId}=h;export const forwardRef=(f)=>f', shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: 'data:text/javascript,export const jsx=(type,props,key)=>({type,props,key});export const jsxs=jsx;export const Fragment="fragment"', shortCircuit: true }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return next(spec + ext, ctx) } catch { /* próxima extensão */ } }
    }
    return next(spec, ctx)
  },
  load(caminho, ctx, next) {
    if (fontes.has(caminho)) return inline(fontes.get(caminho))
    if (caminho.endsWith('/infra/fonteDados.js')) return inline('export const API_BASE="";export const FONTE_API=true')
    if (caminho.endsWith('/infra/api/sessao.js')) return inline('const g=globalThis.__prova2070pp;export const lerSessao=()=>g.sessao;export const identidadeDaSessao=(s)=>`${s?.empresa?.id??"-"}:${s?.usuario?.id??"-"}`;export const gravarSessao=(s)=>{g.sessaoGravada=s;g.sessao=s;return s};export const limparSessao=()=>{};export const atualizarTokens=(s)=>s;export const empresaDoToken=()=>null;export const EVENTO_SESSAO_ALTERADA="x"')
    if (caminho.endsWith('/aplicacao/contextos.js')) return inline('export const useAcoes=()=>globalThis.__prova2070pp.contexto.acoes;export const useAtendimento=()=>globalThis.__prova2070pp.contexto.estado;export const useCatalogo=()=>({})')
    if (caminho.endsWith('/aplicacao/acessoModulos.jsx')) return inline('export const useAcessoModulos=()=>globalThis.__prova2070pp.acesso')
    if (caminho.endsWith('.css')) return inline('export default new Proxy({},{get:(_,p)=>String(p)})')
    if (caminho.endsWith('.jsx')) return inline([...readFileSync(new URL(caminho), 'utf8').matchAll(/^export (?:function|const) (\w+)/gm)].map((m) => `export const ${m[1]}='${m[1]}'`).join('\n'))
    return next(caminho, ctx)
  },
})

// A API simulada: cada teste diz o que cada rota devolve; a prova guarda método, caminho e corpo.
const rede = { chamadas: [], rotas: [] }
const responder = (metodo, padrao, resposta) => rede.rotas.unshift({ metodo, padrao, resposta })
globalThis.fetch = async (url, opcoes = {}) => {
  const metodo = opcoes.method ?? 'GET'
  const corpo = typeof opcoes.body === 'string' ? JSON.parse(opcoes.body) : opcoes.body
  rede.chamadas.push({ metodo, url, corpo })
  const rota = rede.rotas.find((r) => r.metodo === metodo && (typeof r.padrao === 'string' ? r.padrao === url : r.padrao.test(url)))
  if (!rota) return new Response(JSON.stringify({ error: { code: 'NAO_SIMULADA', message: `Rota não simulada: ${metodo} ${url}` } }), { status: 599 })
  const { status = 200, dados, erro } = typeof rota.resposta === 'function' ? await rota.resposta({ url, corpo }) : rota.resposta
  if (status === 204) return new Response(null, { status })
  return new Response(JSON.stringify(erro ? { error: erro } : { data: dados ?? null }), { status, headers: { 'Content-Type': 'application/json' } })
}
const escritas = () => rede.chamadas.filter((c) => c.metodo !== 'GET')
const chamadasDe = (metodo, padrao) => rede.chamadas.filter((c) => c.metodo === metodo && (typeof padrao === 'string' ? c.url === padrao : padrao.test(c.url)))

// --- Leitura da árvore ------------------------------------------------------------------------------

const andar = (no) => {
  if (!no || typeof no !== 'object') return []
  if (Array.isArray(no)) return no.flatMap(andar)
  return [no, ...Object.values(no.props ?? {}).flatMap((v) => (ehElemento(v) ? andar(v) : []))]
}
const texto = (no) => {
  if (no === null || no === undefined || typeof no === 'boolean') return ''
  if (typeof no !== 'object') return String(no)
  if (Array.isArray(no)) return no.map(texto).join('')
  return texto(no.props?.children)
}
const todoTexto = (raiz) => andar(raiz.arvore).map((n) => [n.props?.rotulo, n.props?.dica, n.props?.titulo, n.props?.['aria-label']].filter(Boolean).join(' ')).join(' ') + ' ' + texto(raiz.arvore)
const campo = (raiz, rotulo) => andar(raiz.arvore).find((n) => n.props?.rotulo === rotulo)
const rotulos = (raiz) => andar(raiz.arvore).map((n) => n.props?.rotulo).filter(Boolean)
const botao = (raiz, nome) => andar(raiz.arvore).find((n) => n.type === 'Botao' && (texto(n).trim() === nome || n.props?.['aria-label'] === nome))
const links = (raiz) => andar(raiz.arvore).filter((n) => n.type === 'a')
const mudar = (raiz, rotulo, valor) => {
  const alvo = campo(raiz, rotulo)
  assert.ok(alvo, `campo "${rotulo}" na tela (há: ${rotulos(raiz).join(', ')})`)
  alvo.props.onChange({ target: { value: valor, checked: valor, setCustomValidity() {} } })
  raiz.renderizar()
}
const enviar = async (raiz, formulario) => {
  assert.ok(formulario, 'há um formulário para enviar')
  await formulario.props.onSubmit({ preventDefault() {}, currentTarget: { reportValidity: () => true, querySelectorAll: () => [] } })
  return raiz.atualizar()
}
const clicar = async (raiz, nome) => {
  const alvo = botao(raiz, nome)
  assert.ok(alvo, `botão "${nome}" na tela (há: ${andar(raiz.arvore).filter((n) => n.type === 'Botao').map((n) => texto(n).trim()).join(' | ')})`)
  assert.notEqual(alvo.props.disabled, true, `botão "${nome}" habilitado`)
  await alvo.props.onClick?.({ preventDefault() {}, currentTarget: {} })
  return raiz.atualizar()
}
const avisoVivo = (raiz) => andar(raiz.arvore).filter((n) => n.props?.['aria-live'] === 'polite' || n.props?.role === 'alert').map(texto).join(' ')

let passou = 0
const falhas = []
async function provar(nome, fn) {
  rede.chamadas = []; rede.rotas = []; g.acesso = { acoes: {} }; g.sessao = SESSAO; g.sessaoGravada = null
  janela.location.hash = ''; janela.location.substituicoes = []; ouvintes.clear()
  try { await fn(); passou++; console.log('ok    ' + nome) }
  catch (erro) { falhas.push(nome); console.log('FALHA ' + nome + '\n      ' + String(erro?.stack ?? erro).split('\n').slice(0, 4).join('\n      ')) }
}
const carregar = async (caminho) => import(src(caminho).href)

// Estado como a API devolve: os passos do contrato, com as situações pedidas.
const passoDaApi = (passo, situacao = null, extra = {}) => ({
  passo, parte: IDS_DO_CONTRATO.indexOf(passo) < 7 ? 'essencial' : passo === 'pronto' ? 'fim' : 'ajustes', ordem: IDS_DO_CONTRATO.indexOf(passo) + 1,
  situacao, em: situacao ? '2026-10-26T12:00:00Z' : null, porNome: situacao ? 'Ana Teste' : null, disponivel: true, motivo: null, prontidao: null, ...extra,
})
const estadoDaApi = (situacoes = {}, extras = {}, topo = {}) => ({
  pendente: true, abrirSozinho: true, ehDona: true, podeConcluir: true, concluidaEm: null, adiadaEm: null,
  passos: IDS_DO_CONTRATO.map((id) => passoDaApi(id, situacoes[id] ?? null, extras[id])), ...topo,
})

const rascunhosDosAjustes = await carregar('aplicacao/rascunhosDosAjustes.js')
// O contexto de rascunhos da visita (o do AppComAcesso): `definir` grava e pede outra volta de render.
const contextoDosRascunhos = () => {
  const valor = {
    rascunhos: {}, gravando: {},
    definir(chave, novo) {
      const proximos = { ...valor.rascunhos }
      if (novo === null) delete proximos[chave]
      else proximos[chave] = novo
      valor.rascunhos = proximos
      if (g.raizAtual) g.raizAtual.sujo = true
    },
    iniciarGravacao: () => () => {}, estaGravando: () => false,
  }
  rascunhosDosAjustes.ContextoRascunhosDosAjustes.inicial = valor
  return valor
}

// --- A. Domínio puro ---------------------------------------------------------------------------------

const dominio = await carregar('dominio/configuracaoInicial.js')

await provar('A1 mapa: os 17 ids do contrato na mesma ordem, com rótulo, parte, o que monta e chaves de rascunho', async () => {
  const { PASSOS } = dominio
  assert.deepEqual(PASSOS.map((p) => p.id), IDS_DO_CONTRATO)
  assert.deepEqual(PASSOS.filter((p) => p.parte === 'essencial').map((p) => p.id), IDS_DO_CONTRATO.slice(0, 7))
  assert.deepEqual(PASSOS.filter((p) => p.parte === 'ajustes').map((p) => p.id), IDS_DO_CONTRATO.slice(7, 16))
  assert.deepEqual(PASSOS.filter((p) => p.parte === 'fim').map((p) => p.id), ['pronto'])
  for (const p of PASSOS) {
    assert.ok(p.rotulo && p.descricao, `${p.id} tem rótulo e descrição`)
    assert.ok(Array.isArray(p.monta) && p.monta.length > 0 && p.monta.every((m) => m.aba || m.componente), `${p.id} diz o que monta`)
    assert.ok(Array.isArray(p.chavesDeRascunho), `${p.id} lista as chaves de rascunho`)
  }
  assert.deepEqual(dominio.passoDoMapa('horarios').monta, [{ aba: 'atendimento', secoes: ['horarios'] }])
  assert.deepEqual(dominio.passoDoMapa('atendimento').monta, [{ aba: 'atendimento', secoes: ['configuracao'] }], 'sem o liga e desliga do agente')
  assert.deepEqual(dominio.passoDoMapa('agente').monta, [{ aba: 'caderno' }, { aba: 'atendimento', secoes: ['agente'] }])
  assert.deepEqual(dominio.passoDoMapa('janelas').monta, [{ aba: 'janelas', secao: 'janelas' }])
  assert.deepEqual(dominio.passoDoMapa('frete').monta, [{ aba: 'janelas', secao: 'zonas' }])
  assert.deepEqual(dominio.passoDoMapa('boas-vindas').monta, [{ componente: 'boas-vindas' }])
  assert.deepEqual(dominio.passoDoMapa('pronto').monta, [{ componente: 'pronto' }])
  assert.deepEqual(dominio.passoDoMapa('horarios').chavesDeRascunho, ['horarios-atendimento', 'mensagens-expediente', 'expediente-atendimento'])
  assert.deepEqual(dominio.passoDoMapa('atendimento').chavesDeRascunho, ['configuracao-atendimento'])
  assert.ok(dominio.passoDoMapa('frete').chavesDeRascunho.includes('zona-de-frete:'))
  assert.equal(dominio.passoDoMapa('nao-existe'), null)
  assert.equal(dominio.ehPassoConhecido('frete'), true)
  assert.equal(dominio.ehPassoConhecido('Frete'), false)
  assert.equal(dominio.ehPassoConhecido(undefined), false)
})

await provar('A2 retomada: o primeiro passo em aberto, pulando o conferido, o "depois", o sem acesso e o id desconhecido', async () => {
  const { passoDaRetomada } = dominio
  assert.equal(passoDaRetomada(null), 'boas-vindas', 'sem estado começa do começo')
  assert.equal(passoDaRetomada(estadoDaApi()), 'boas-vindas')
  assert.equal(passoDaRetomada(estadoDaApi({ 'boas-vindas': 'conferido' })), 'horarios')
  assert.equal(passoDaRetomada(estadoDaApi({ 'boas-vindas': 'conferido', horarios: 'depois' })), 'site-e-impressos')
  assert.equal(passoDaRetomada(estadoDaApi({ 'boas-vindas': 'conferido' }, { horarios: { disponivel: false, motivo: 'Sem o módulo Atendimento.' } })), 'site-e-impressos')
  const essencialFeito = Object.fromEntries(IDS_DO_CONTRATO.slice(0, 7).map((id) => [id, 'conferido']))
  assert.equal(passoDaRetomada(estadoDaApi(essencialFeito)), 'dados-da-loja', 'a Parte 2 entra na retomada')
  const tudo = Object.fromEntries(IDS_DO_CONTRATO.slice(0, 16).map((id) => [id, 'depois']))
  assert.equal(passoDaRetomada(estadoDaApi(tudo)), 'pronto')
  const comDesconhecido = estadoDaApi(essencialFeito)
  comDesconhecido.passos.splice(7, 0, passoDaApi('passo-novo-da-api'))
  assert.equal(passoDaRetomada(comDesconhecido), 'dados-da-loja', 'id desconhecido não vira rota')
})

await provar('A3 revisão: Pronto, Falta X, Deixado para depois, Sem acesso e o id desconhecido com "Abrir em Configurações"', async () => {
  const estado = estadoDaApi(
    { 'boas-vindas': 'conferido', horarios: 'conferido', cardapio: 'conferido', frete: 'depois' },
    { cardapio: { prontidao: { ok: false, falta: ['cadastrar um item', 'uma categoria'] } }, horarios: { prontidao: { ok: true, falta: [] } }, equipe: { disponivel: false, motivo: 'Só quem administra a loja convida.' } },
  )
  estado.passos.push(passoDaApi('passo-novo-da-api'))
  const r = dominio.resumoDaRevisao(estado)
  assert.deepEqual(r.pronto.map((p) => p.passo), ['boas-vindas', 'horarios'])
  assert.deepEqual(r.falta.find((p) => p.passo === 'cardapio'), { passo: 'cardapio', rotulo: 'Cardápio', texto: 'Falta cadastrar um item, uma categoria' })
  assert.equal(r.falta.find((p) => p.passo === 'site-e-impressos').texto, 'Falta conferir')
  assert.ok(!r.falta.some((p) => p.passo === 'pronto'), 'o próprio passo final não entra na revisão')
  assert.deepEqual(r.depois.map((p) => p.passo), ['frete'])
  assert.deepEqual(r.semAcesso, [{ passo: 'equipe', rotulo: 'Equipe', texto: 'Só quem administra a loja convida.' }])
  assert.deepEqual(r.desconhecidos, [{ passo: 'passo-novo-da-api', texto: 'Abrir em Configurações', href: '#/m/configuracoes' }])
  const todos = [...r.pronto, ...r.falta, ...r.depois, ...r.semAcesso].map((p) => p.passo).sort()
  assert.deepEqual(todos, IDS_DO_CONTRATO.slice(0, 16).sort(), 'cada passo cai em um grupo só')
})

await provar('A4 progresso: "Passo 2 de 7 · Horários", a Parte 2 conta 9, o fim é a revisão e o hall conta só a Parte 1', async () => {
  assert.equal(dominio.textoDoProgresso('boas-vindas'), 'Passo 1 de 7 · Boas-vindas')
  assert.equal(dominio.textoDoProgresso('horarios'), 'Passo 2 de 7 · Horários')
  assert.equal(dominio.textoDoProgresso('equipe'), 'Passo 7 de 7 · Equipe')
  assert.equal(dominio.textoDoProgresso('dados-da-loja'), 'Passo 1 de 9 · Dados da loja')
  assert.equal(dominio.textoDoProgresso('respostas'), 'Passo 3 de 9 · Respostas')
  assert.equal(dominio.textoDoProgresso('pronto'), 'Revisão final')
  assert.deepEqual(dominio.progressoDaParte(estadoDaApi({ 'boas-vindas': 'conferido', horarios: 'depois', cardapio: 'conferido', respostas: 'conferido' }), 'essencial'), { feitos: 3, total: 7 })
  assert.deepEqual(dominio.progressoDaParte(null, 'essencial'), { feitos: 0, total: 7 })
})

await provar('A5 vizinhos: Voltar e seguir na ordem do mapa, pulando o passo sem acesso', async () => {
  const estado = estadoDaApi({}, { 'site-e-impressos': { disponivel: false } })
  assert.equal(dominio.passoVizinho('boas-vindas', -1, estado), null)
  assert.equal(dominio.passoVizinho('boas-vindas', 1, estado), 'horarios')
  assert.equal(dominio.passoVizinho('horarios', 1, estado), 'cardapio', 'pula o passo sem acesso')
  assert.equal(dominio.passoVizinho('cardapio', -1, estado), 'horarios')
  assert.equal(dominio.passoVizinho('equipe', 1, estado), 'dados-da-loja', 'da Parte 1 para a Parte 2')
  assert.equal(dominio.passoVizinho('avisos', 1, estado), 'pronto')
  assert.equal(dominio.passoVizinho('pronto', 1, estado), null)
})

await provar('A6 pendência do passo: a chave exata e o prefixo terminado em ":" no contexto de rascunhos ou de gravação', async () => {
  const { pendenciaDoPasso } = dominio
  assert.equal(pendenciaDoPasso('horarios', { rascunhos: {}, gravando: {} }), false)
  assert.equal(pendenciaDoPasso('horarios', { rascunhos: { 'mensagens-expediente': { mensagemForaDoHorario: 'x' } } }), true)
  assert.equal(pendenciaDoPasso('horarios', { gravando: { 'expediente-atendimento': true } }), true, 'gravação em curso também segura')
  assert.equal(pendenciaDoPasso('horarios', { rascunhos: { 'configuracao-atendimento': {} } }), false, 'rascunho de outro passo não segura')
  assert.equal(pendenciaDoPasso('frete', { rascunhos: { 'zona-de-frete:nova': true } }), true)
  assert.equal(pendenciaDoPasso('frete', { rascunhos: { 'zona-de-frete': true } }), false, 'prefixo exige o ":"')
  assert.equal(pendenciaDoPasso('boas-vindas', null), false)
  assert.equal(pendenciaDoPasso('nao-existe', { rascunhos: { x: 1 } }), false)
})

await provar('A7 deveAbrirSozinho: cada caso da especificação', async () => {
  const { deveAbrirSozinho } = dominio
  const dona = estadoDaApi()
  const base = { hashAtual: '', jaAbriuNaVisita: false }
  assert.equal(deveAbrirSozinho(dona, base), true, 'dona com abrirSozinho, hash vazia')
  assert.equal(deveAbrirSozinho(dona, { ...base, hashAtual: '#/' }), true, 'hash do hall')
  assert.equal(deveAbrirSozinho(dona, { ...base, hashAtual: '#' }), true)
  assert.equal(deveAbrirSozinho({ ...dona, abrirSozinho: false, adiadaEm: '2026-10-26T12:00:00Z' }, base), false, 'depois de "Continuar depois"')
  assert.equal(deveAbrirSozinho(dona, { ...base, hashAtual: '#/m/cardapio/itens' }), false, 'link direto não é sequestrado')
  assert.equal(deveAbrirSozinho({ ...dona, ehDona: false, abrirSozinho: false }, base), false, 'Administração não abre sozinho')
  assert.equal(deveAbrirSozinho(dona, { ...base, jaAbriuNaVisita: true }), false, 'uma vez por visita')
  assert.equal(deveAbrirSozinho(null, base), false, 'chave ausente: sem leitura, sem redirecionamento')
  assert.equal(deveAbrirSozinho({ ...dona, abrirSozinho: 'true' }, base), false, 'só o booleano verdadeiro')
  // A entrada do perfil troca o hall pela porta antes da leitura chegar: vale a hash da entrada.
  assert.equal(deveAbrirSozinho(dona, { hashAtual: '#/m/atendimento', hashNaEntrada: '', hashDaPorta: '#/m/atendimento', jaAbriuNaVisita: false }), true)
  assert.equal(deveAbrirSozinho(dona, { hashAtual: '#/m/atendimento', hashNaEntrada: '#/m/atendimento/balcao', hashDaPorta: '#/m/atendimento', jaAbriuNaVisita: false }), false)
  assert.equal(deveAbrirSozinho(dona, { hashAtual: '#/m/atendimento', hashNaEntrada: '', hashDaPorta: null, jaAbriuNaVisita: false }), false)
})

await provar('A8 o suporte já ligou: cobrança online, WhatsApp e mapa só leitura, a partir das integrações e do status', async () => {
  const lista = [
    { provider: 'mercadopago', nome: 'Mercado Pago', ativo: true }, { provider: 'googlemaps', nome: 'Google Maps', ativo: false },
    { provider: 'lalamove', nome: 'Lalamove', ativo: true },
  ]
  const status = { phoneNumberId: '123', provider: 'meta', ultimaMensagemRecebidaEm: null, webhookVerificadoEm: null }
  assert.deepEqual(dominio.ligadosPeloSuporte({ integracoes: lista, whatsapp: status }).map(({ nome, situacao, ligado }) => [nome, situacao, ligado]), [
    ['Cobrança online', 'Ligada', true], ['WhatsApp', 'WhatsApp ligado', true], ['Mapa', 'Ainda não ligado: fale com o suporte', false],
  ])
  const carregando = dominio.ligadosPeloSuporte({ integracoes: null, whatsapp: undefined })
  assert.deepEqual(carregando.map((s) => s.situacao), ['Conferindo…', 'Conferindo…', 'Conferindo…'])
  const falhou = dominio.ligadosPeloSuporte({ integracoes: null, whatsapp: undefined, erroIntegracoes: 'x', erroWhatsApp: 'y' })
  assert.deepEqual(falhou.map((s) => s.situacao), ['Não foi possível conferir agora', 'Não foi possível conferir agora', 'Não foi possível conferir agora'])
  assert.equal(dominio.ligadosPeloSuporte({ integracoes: [], whatsapp: null })[1].situacao, 'O atendimento por WhatsApp não está ligado para a loja')
})

// --- B. Catálogo e acesso ----------------------------------------------------------------------------

const modulos = await carregar('dominio/modulos.js')
const acessoModulos = await carregar('dominio/acessoModulos.js')
const congelados = await carregar('dominio/congelados.js')
const ADMIN = { configurarLoja: true, editarAtendimento: true, controlarLoja: true, verIntegracoes: true, ajustarEntrega: true }
const GERENCIA = { configurarLoja: false, editarAtendimento: false, controlarLoja: true, verIntegracoes: false, ajustarEntrega: false }

await provar('B1 catálogo: Primeiros passos em Configurações, só na API, ação estrita, fora da Gestão e em tela cheia', async () => {
  const configuracoes = modulos.moduloPorId('configuracoes')
  const tela = configuracoes.telas.find((t) => t.id === 'primeiros-passos')
  assert.deepEqual(tela, {
    id: 'primeiros-passos', rotulo: 'Primeiros passos', tipo: modulos.TELA_AJUSTE, aba: 'primeiros-passos', soApi: true,
    acao: 'configurarLoja', foraDaGestao: true, telaCheia: true,
  })
  const ids = (acoes, fonteApi = true) => modulos.telasDoMenu(configuracoes, { fonteApi, acoes }).map((t) => t.id)
  assert.ok(ids(ADMIN).includes('primeiros-passos'))
  assert.ok(!ids(GERENCIA).includes('primeiros-passos'))
  assert.ok(!ids({}).includes('primeiros-passos'), 'chave ausente = falso')
  assert.ok(!ids({ configurarLoja: 'true' }).includes('primeiros-passos'), 'estrita: só o booleano verdadeiro')
  assert.ok(!ids(ADMIN, false).includes('primeiros-passos'), 'no modo demonstração não existe')
  assert.equal(acessoModulos.podeConfigurarLoja(ADMIN), true)
  assert.equal(acessoModulos.podeConfigurarLoja({ editarAtendimento: true }), false, 'sem fallback de outra chave')
  assert.equal(acessoModulos.podeConfigurarLoja(null), false)
  assert.equal(acessoModulos.PODE_NA_TELA.configurarLoja, acessoModulos.podeConfigurarLoja)
  assert.equal(acessoModulos.PODE_NA_TELA.ajustarEntrega, acessoModulos.podeAjustarEntrega)
})

await provar('B2 catálogo: as telas dos formulários novos em Configurações e Entregadores em Entregas, cada uma com a ação certa', async () => {
  const configuracoes = modulos.moduloPorId('configuracoes')
  const esperadas = {
    'dados-da-loja': { rotulo: 'Dados da loja', aba: 'dados-da-loja', acao: 'configurarLoja' },
    'site-e-impressos': { rotulo: 'Site e impressos', aba: 'site-e-impressos', acao: 'configurarLoja' },
    'alertas-de-estoque': { rotulo: 'Alertas de estoque', aba: 'alertas-de-estoque', acao: 'configurarLoja' },
    caderno: { rotulo: 'Caderno do agente', aba: 'caderno', acao: 'editarAtendimento' },
    avisos: { rotulo: 'Avisos', aba: 'avisos' },
  }
  for (const [id, esperado] of Object.entries(esperadas)) {
    const tela = configuracoes.telas.find((t) => t.id === id)
    assert.ok(tela, `${id} no catálogo`)
    assert.deepEqual({ rotulo: tela.rotulo, aba: tela.aba, acao: tela.acao }, { ...esperado, acao: esperado.acao }, id)
    assert.equal(tela.tipo, modulos.TELA_AJUSTE)
    assert.equal(tela.soApi, true, `${id} só existe no modo API`)
    assert.equal(tela.foraDaGestao, undefined, `${id} também abre pela Gestão`)
  }
  for (const acao of new Set(Object.values(esperadas).map((e) => e.acao).filter(Boolean))) assert.equal(typeof acessoModulos.PODE_NA_TELA[acao], 'function', acao)
  const ids = (acoes) => modulos.telasDoMenu(configuracoes, { fonteApi: true, acoes }).map((t) => t.id)
  assert.deepEqual(ids(ADMIN).filter((id) => id in esperadas), Object.keys(esperadas))
  assert.deepEqual(ids({ ...ADMIN, configurarLoja: false }).filter((id) => id in esperadas), ['caderno', 'avisos'])
  assert.deepEqual(ids({ ...ADMIN, editarAtendimento: false }).filter((id) => id in esperadas), ['dados-da-loja', 'site-e-impressos', 'alertas-de-estoque', 'avisos'])
  const entregas = modulos.moduloPorId('entregas')
  const entregadores = entregas.telas.find((t) => t.id === 'entregadores')
  assert.deepEqual(entregadores, { id: 'entregadores', rotulo: 'Entregadores', tipo: modulos.TELA_AJUSTE, aba: 'entregadores', soApi: true })
  assert.ok(modulos.telasDoMenu(entregas, { fonteApi: true, acoes: GERENCIA }).some((t) => t.id === 'entregadores'), 'a rota de entregadores é da policy Operador')
  assert.ok(!modulos.telasDoMenu(entregas, { fonteApi: false }).some((t) => t.id === 'entregadores'))
})

await provar('B3 nenhum passo aponta para tela congelada, e cada aba que um passo monta existe no catálogo', async () => {
  for (const p of dominio.PASSOS) {
    for (const aba of dominio.abasDoPasso(p.id)) {
      const lugar = modulos.MODULOS.flatMap((m) => m.telas.map((t) => ({ m, t }))).find(({ t }) => t.aba === aba)
      assert.ok(lugar, `${p.id}: a aba ${aba} existe no catálogo`)
      assert.ok(!congelados.moduloCongelado(lugar.m.id), `${p.id}: ${lugar.m.id} congelado`)
      assert.ok(!congelados.telaCongelada(lugar.m.id, lugar.t.id), `${p.id}: ${lugar.m.id}/${lugar.t.id} congelada`)
    }
  }
  assert.deepEqual(dominio.abasDoPasso('cardapio'), ['categorias', 'cardapio'])
  assert.deepEqual(dominio.abasDoPasso('boas-vindas'), [])
})

await provar('B4 ModalGestao não mostra Primeiros passos; as telas novas aparecem nas áreas', async () => {
  const { ModalGestao } = await carregar('features/gestao/ModalGestao.jsx')
  g.acesso = { permite: () => true, acoes: ADMIN }
  g.contexto = { acoes: {}, estado: { fonteApi: true } }
  const raiz = new Raiz({ type: ModalGestao, props: { aoFechar() {}, abaInicial: 'dados-da-loja' } })
  await raiz.atualizar(2)
  const abas = andar(raiz.arvore).filter((n) => n.type === 'button' && n.props?.role === 'tab').map((n) => texto(n))
  assert.ok(abas.some((t) => t.includes('Dados da loja')), `Dados da loja nas telas (há: ${abas.join(' | ')})`)
  assert.ok(!abas.some((t) => t.includes('Primeiros passos')), 'Primeiros passos fica fora da Gestão')
  const painel = andar(raiz.arvore).find((n) => n.type === 'AbaDadosDaLoja')
  assert.ok(painel, 'a aba dados-da-loja monta o formulário novo')
  assert.match(fonte('features/gestao/ModalGestao.jsx'), /\.filter\(\(tela\) => tela\.tipo === TELA_AJUSTE && !tela\.foraDaGestao\)/)
})

await provar('B5 PainelDeAjuste: abas novas com o contrato dos formulários, e os slots chegam ao passo', async () => {
  const { PainelDeAjuste } = await carregar('features/gestao/ModalGestao.jsx')
  g.acesso = { permite: () => true, acoes: ADMIN }
  g.contexto = { acoes: {}, estado: { fonteApi: true } }
  const aoMudarPendencia = () => {}
  const contrato = { aoMudarPendencia, aoSalvo: () => {}, compacto: true }
  const slots = {
    horariosAtendimento: { type: 'SlotHorarios', props: {} },
    horariosDaSemana: (props) => ({ type: 'FormularioHorariosDaSemana', props }),
    janelasApi: (props) => ({ type: 'CadastroEntregaApi', props }),
    entregadoresApi: (props) => ({ type: 'CadastroEntregadoresApi', props }),
    renderizarEditorCardapio: () => null,
  }
  const montar = (aba, extra = {}) => new Raiz({ type: PainelDeAjuste, props: { aba, ...slots, ...contrato, ...extra } })
  for (const [aba, tipo] of [['dados-da-loja', 'AbaDadosDaLoja'], ['site-e-impressos', 'AbaSiteEImpressos'], ['alertas-de-estoque', 'AbaAlertasDeEstoque'], ['caderno', 'AbaCadernoDoAgente']]) {
    const no = andar(montar(aba).arvore).find((n) => n.type === tipo)
    assert.ok(no, `${aba} monta ${tipo}`)
    assert.equal(no.props.aoMudarPendencia, aoMudarPendencia, `${aba} recebe aoMudarPendencia`)
    assert.equal(no.props.compacto, true)
  }
  assert.ok(andar(montar('avisos').arvore).find((n) => n.type === 'AbaAvisos'))
  const entregadores = andar(montar('entregadores').arvore).find((n) => n.type === 'CadastroEntregadoresApi')
  assert.equal(entregadores?.props.aoMudarPendencia, aoMudarPendencia, 'entregadores chega pelo slot do App')
  assert.equal(andar(montar('janelas', { secao: 'zonas' }).arvore).find((n) => n.type === 'CadastroEntregaApi')?.props.secao, 'zonas')
  // A AbaAtendimento é a real: o passo de horários recebe a semana pelo slot, e sem secoes ela fica como era.
  g.contexto = {
    estado: { fonteApi: true, expediente: { carregado: true, mensagemForaDoHorario: '', mensagemLojaFechada: '' }, aberta: true, lojaAberta: null, agora: 0 },
    acoes: { recarregarExpediente: () => {}, carregarConfiguracao: () => new Promise(() => {}), alternarLoja() {}, voltarAoHorario() {} },
  }
  contextoDosRascunhos()
  const passo = andar(montar('atendimento', { secoes: ['horarios'] }).arvore)
  const semana = passo.find((n) => n.type === 'FormularioHorariosDaSemana')
  assert.ok(semana, 'a semana chega pelo slot')
  assert.equal(semana.props.aoMudarPendencia, aoMudarPendencia)
  assert.ok(!passo.some((n) => n.type === 'SlotHorarios'), 'o passo não monta o "Loja agora"')
  const semSecoes = andar(montar('atendimento').arvore)
  assert.ok(semSecoes.some((n) => n.type === 'SlotHorarios'), 'sem secoes, a aba fica igual')
  assert.ok(!semSecoes.some((n) => n.type === 'FormularioHorariosDaSemana'))
})

// --- C. Rota -------------------------------------------------------------------------------------------

const rota = await carregar('dominio/rota.js')

await provar('C1 rota: o passo no terceiro segmento só para Primeiros passos, validado contra o mapa', async () => {
  const r = rota.rotaDaHash('#/m/configuracoes/primeiros-passos/frete', { fonteApi: true })
  assert.deepEqual(r, { tipo: rota.ROTA_MODULO, modulo: 'configuracoes', tela: 'primeiros-passos', aba: 'primeiros-passos', passo: 'frete' })
  assert.equal(rota.rotaDaHash('#/m/configuracoes/primeiros-passos', { fonteApi: true }).passo, null, 'sem passo, a tela retoma')
  assert.equal(rota.rotaDaHash('#/m/configuracoes/primeiros-passos/nao-existe', { fonteApi: true }).passo, null, 'desconhecido cai na retomada')
  assert.equal('passo' in rota.rotaDaHash('#/m/configuracoes/canais/frete', { fonteApi: true }), false, 'outra tela não ganha passo')
  const naDemonstracao = rota.rotaDaHash('#/m/configuracoes/primeiros-passos/frete', { fonteApi: false })
  assert.notEqual(naDemonstracao.tela, 'primeiros-passos', 'na demonstração a tela não existe')
  assert.equal('passo' in naDemonstracao, false)
  assert.equal(rota.hashDoPasso('frete'), '#/m/configuracoes/primeiros-passos/frete')
  for (const id of IDS_DO_CONTRATO) assert.equal(rota.rotaDaHash(rota.hashDoPasso(id), { fonteApi: true }).passo, id, id)
})

// --- D. Montagem com a API simulada -------------------------------------------------------------------

const BASE = '/api/configuracao-inicial'
const { TelaConfiguracaoInicial } = await carregar('features/gestao/configuracao-inicial/TelaConfiguracaoInicial.jsx')
const { CartaoPrimeirosPassos } = await carregar('features/gestao/configuracao-inicial/CartaoPrimeirosPassos.jsx')

// Servidor com estado: o PUT grava, o GET devolve o que ficou.
function servidor(inicial) {
  const s = { estado: structuredClone(inicial) }
  responder('GET', BASE, () => ({ dados: structuredClone(s.estado) }))
  responder('PUT', /^\/api\/configuracao-inicial\/passos\/[a-z-]+$/, ({ url, corpo }) => {
    const id = url.split('/').at(-1)
    const p = s.estado.passos.find((x) => x.passo === id)
    if (!p) return { status: 400, erro: { code: 'PASSO_DESCONHECIDO', message: 'Passo desconhecido.' } }
    p.situacao = corpo.situacao
    return { status: 204 }
  })
  responder('POST', `${BASE}/adiar`, () => { s.estado.abrirSozinho = false; s.estado.adiadaEm = '2026-10-27T10:00:00Z'; return { status: 204 } })
  responder('POST', `${BASE}/concluir`, () => { s.estado.pendente = false; s.estado.concluidaEm = '2026-10-27T10:00:00Z'; return { status: 204 } })
  return s
}

const renderizador = () => {
  const chamadas = []
  const fn = (props) => { chamadas.push(props); return { type: 'PainelDeAjuste', props } }
  fn.chamadas = chamadas
  return fn
}
const montarTela = async (props = {}) => {
  const renderizarAba = props.renderizarAba ?? renderizador()
  const raiz = new Raiz({ type: TelaConfiguracaoInicial, props: { passo: null, renderizarAba, ...props } })
  await raiz.atualizar()
  return { raiz, renderizarAba }
}
const trocarPasso = async (raiz, passo) => { raiz.elemento = { ...raiz.elemento, props: { ...raiz.elemento.props, passo } }; return raiz.atualizar() }
const paineis = (raiz) => andar(raiz.arvore).filter((n) => n.type === 'PainelDeAjuste')
const AVISO_PENDENCIA = 'Há alterações não salvas neste passo. Salve ou desfaça antes de seguir.'

await provar('D1 sem passo na rota: retoma do primeiro em aberto, troca a hash e mostra "Passo 2 de 7 · Horários"', async () => {
  servidor(estadoDaApi({ 'boas-vindas': 'conferido' }))
  g.acesso = { acoes: ADMIN, entrada: '#/' }
  contextoDosRascunhos()
  const { raiz } = await montarTela()
  assert.deepEqual(janela.location.substituicoes, ['#/m/configuracoes/primeiros-passos/horarios'], 'a hash passa a dizer o passo')
  assert.match(todoTexto(raiz), /Passo 2 de 7 · Horários/)
  const atuais = andar(raiz.arvore).filter((n) => n.props?.['aria-current'] === 'step')
  assert.equal(atuais.length, 1, 'um passo atual')
  assert.match(texto(atuais[0]), /Horários/)
  assert.match(todoTexto(raiz), /Parte 1 · O essencial/)
  assert.match(todoTexto(raiz), /Parte 2 · Mais ajustes/)
  assert.match(todoTexto(raiz), /Cada passo desta parte pode ser pulado\./)
  const [painel] = paineis(raiz)
  assert.deepEqual({ aba: painel.props.aba, secoes: painel.props.secoes }, { aba: 'atendimento', secoes: ['horarios'] })
  assert.equal(typeof painel.props.aoMudarPendencia, 'function')
  const titulo = andar(raiz.arvore).find((n) => n.type === 'h2' && texto(n) === 'Horários')
  assert.ok(titulo, 'o título do passo é um h2')
  assert.equal(titulo.props.tabIndex, -1, 'o título recebe o foco ao trocar de passo')
  assert.doesNotMatch(todoTexto(raiz), /Abrir loja agora/)
})

await provar('D2 "Conferi, seguir" fica bloqueado com aviso enquanto o formulário novo avisa pendência', async () => {
  servidor(estadoDaApi())
  g.acesso = { acoes: ADMIN, entrada: '#/' }
  contextoDosRascunhos()
  const { raiz } = await montarTela({ passo: 'site-e-impressos' })
  assert.notEqual(botao(raiz, 'Conferi, seguir').props.disabled, true)
  paineis(raiz)[0].props.aoMudarPendencia(true)
  await raiz.atualizar(2)
  assert.equal(botao(raiz, 'Conferi, seguir').props.disabled, true, 'bloqueado com pendência')
  assert.match(avisoVivo(raiz), new RegExp(AVISO_PENDENCIA))
  assert.ok((ouvintes.get('beforeunload') ?? []).length > 0, 'guarda ao fechar a aba com pendência')
  const evento = { preventDefault() { evento.prevenido = true }, returnValue: undefined }
  ouvintes.get('beforeunload')[0](evento)
  assert.equal(evento.prevenido, true)
  paineis(raiz)[0].props.aoMudarPendencia(false)
  await raiz.atualizar(2)
  assert.notEqual(botao(raiz, 'Conferi, seguir').props.disabled, true, 'liberado depois de salvar ou desfazer')
  assert.equal((ouvintes.get('beforeunload') ?? []).length, 0, 'sem pendência, sem guarda')
  assert.equal(escritas().length, 0, 'bloquear não grava nada')
})

await provar('D3 pelo contexto de rascunhos das abas reaproveitadas: a chave do passo segura, a de outro passo não', async () => {
  servidor(estadoDaApi())
  g.acesso = { acoes: ADMIN, entrada: '#/' }
  const ctx = contextoDosRascunhos()
  const { raiz } = await montarTela({ passo: 'horarios' })
  ctx.definir('configuracao-atendimento', { tom: 'x' })
  await raiz.atualizar(2)
  assert.notEqual(botao(raiz, 'Conferi, seguir').props.disabled, true, 'rascunho de outro passo não segura')
  ctx.definir('mensagens-expediente', { mensagemForaDoHorario: 'Voltamos às 18h' })
  await raiz.atualizar(2)
  assert.equal(botao(raiz, 'Conferi, seguir').props.disabled, true)
  ctx.definir('mensagens-expediente', null)
  await raiz.atualizar(2)
  assert.notEqual(botao(raiz, 'Conferi, seguir').props.disabled, true)
})

await provar('D4 frete: editar uma zona sem salvar (cadastro real) deixa "Conferi, seguir" bloqueado', async () => {
  servidor(estadoDaApi())
  g.acesso = { acoes: ADMIN, entrada: '#/' }
  contextoDosRascunhos()
  responder('GET', '/api/minha-vitrine/entrega/janelas', { dados: [] })
  responder('GET', '/api/minha-vitrine/entrega/zonas', { dados: [{ id: 'z1', label: 'Centro', valor: 8, tempoEstimadoMinutos: 40, ordem: 1, bairros: ['Centro'], cepInicio: null, cepFim: null, ativa: true }] })
  responder('GET', /^\/api\/minha-vitrine\/entrega\/bloqueios/, { dados: [] })
  responder('GET', '/api/minha-vitrine/entrega/raio', { status: 404, erro: { code: 'NOT_FOUND', message: 'Não encontrado.' } })
  const { CadastroEntregaApi } = await carregar('features/entregas/CadastroEntregaApi.jsx')
  const renderizarAba = (props) => (props.aba === 'janelas' ? { type: CadastroEntregaApi, props: { secao: props.secao } } : null)
  const { raiz } = await montarTela({ passo: 'frete', renderizarAba })
  assert.ok(andar(raiz.arvore).some((n) => n.type === 'h2' && texto(n) === 'Zonas de frete'), 'o passo monta as zonas')
  assert.ok(!andar(raiz.arvore).some((n) => n.props?.role === 'tablist'), 'só a seção do passo, sem as abas do cadastro')
  assert.ok(!andar(raiz.arvore).some((n) => n.type === 'h2' && texto(n) === 'Bloqueios'), 'sem bloqueios no passo')
  assert.notEqual(botao(raiz, 'Conferi, seguir').props.disabled, true)
  await clicar(raiz, 'Editar zona Centro')
  // O formulário da zona nova fica montado (escondido) antes da lista: o campo da edição é o último.
  const valorDaEdicao = andar(raiz.arvore).filter((n) => n.props?.rotulo === 'Valor (R$)').at(-1)
  valorDaEdicao.props.onChange({ target: { value: '9,50' } })
  raiz.renderizar()
  await raiz.atualizar(2)
  assert.equal(botao(raiz, 'Conferi, seguir').props.disabled, true, 'zona editada e não salva segura o passo')
  assert.match(avisoVivo(raiz), new RegExp(AVISO_PENDENCIA))
  // O formulário da zona nova fica montado (escondido) antes da lista: o Cancelar da edição é o último.
  const cancelarEdicao = andar(raiz.arvore).filter((n) => n.type === 'Botao' && texto(n).trim() === 'Cancelar').at(-1)
  cancelarEdicao.props.onClick()
  await raiz.atualizar(2)
  assert.notEqual(botao(raiz, 'Conferi, seguir').props.disabled, true, 'cancelar a edição libera')
  await clicar(raiz, 'Nova zona')
  mudar(raiz, 'Nome', 'Jardim')
  await raiz.atualizar(2)
  assert.equal(botao(raiz, 'Conferi, seguir').props.disabled, true, 'zona nova começada também segura')
})

await provar('D5 "Fazer depois" faz o PUT com "depois" e segue; "Conferi, seguir" faz o PUT com "conferido"', async () => {
  const s = servidor(estadoDaApi({ 'boas-vindas': 'conferido' }))
  g.acesso = { acoes: ADMIN, entrada: '#/' }
  contextoDosRascunhos()
  const { raiz } = await montarTela({ passo: 'horarios' })
  await clicar(raiz, 'Fazer depois')
  assert.deepEqual(escritas(), [{ metodo: 'PUT', url: `${BASE}/passos/horarios`, corpo: { situacao: 'depois' } }])
  assert.equal(janela.location.hash, '#/m/configuracoes/primeiros-passos/site-e-impressos')
  assert.equal(s.estado.passos.find((p) => p.passo === 'horarios').situacao, 'depois')
  await trocarPasso(raiz, 'site-e-impressos')
  await clicar(raiz, 'Conferi, seguir')
  assert.deepEqual(escritas().at(-1), { metodo: 'PUT', url: `${BASE}/passos/site-e-impressos`, corpo: { situacao: 'conferido' } })
  assert.equal(janela.location.hash, '#/m/configuracoes/primeiros-passos/cardapio')
  await trocarPasso(raiz, 'cardapio')
  await clicar(raiz, 'Voltar')
  assert.equal(janela.location.hash, '#/m/configuracoes/primeiros-passos/site-e-impressos', 'Voltar só navega')
  assert.equal(escritas().length, 2)
  assert.deepEqual(paineis(raiz).map((p) => p.props.aba), ['categorias', 'cardapio'])
})

await provar('D6 o PUT que falha não segue: o erro fica em aria-live e o passo continua', async () => {
  servidor(estadoDaApi())
  responder('PUT', `${BASE}/passos/horarios`, { status: 500, erro: { code: 'ERRO', message: 'O EasyStok caiu.' } })
  g.acesso = { acoes: ADMIN, entrada: '#/' }
  contextoDosRascunhos()
  const { raiz } = await montarTela({ passo: 'horarios' })
  janela.location.hash = '#/m/configuracoes/primeiros-passos/horarios'
  await clicar(raiz, 'Conferi, seguir')
  assert.equal(janela.location.hash, '#/m/configuracoes/primeiros-passos/horarios')
  assert.match(avisoVivo(raiz), /O EasyStok caiu\./)
})

await provar('D7 recarregar retoma do primeiro passo em aberto (o que já foi marcado fica)', async () => {
  const s = servidor(estadoDaApi({ 'boas-vindas': 'conferido' }))
  g.acesso = { acoes: ADMIN, entrada: '#/' }
  contextoDosRascunhos()
  const primeira = await montarTela({ passo: 'horarios' })
  await clicar(primeira.raiz, 'Conferi, seguir')
  await trocarPasso(primeira.raiz, 'site-e-impressos')
  await clicar(primeira.raiz, 'Fazer depois')
  primeira.raiz.desmontar()
  assert.equal(s.estado.passos.find((p) => p.passo === 'horarios').situacao, 'conferido')
  assert.equal(s.estado.passos.find((p) => p.passo === 'site-e-impressos').situacao, 'depois')
  janela.location.substituicoes = []
  const { raiz } = await montarTela()
  assert.deepEqual(janela.location.substituicoes, ['#/m/configuracoes/primeiros-passos/cardapio'])
  assert.match(todoTexto(raiz), /Passo 4 de 7 · Cardápio/)
})

await provar('D8 passo final: revisão, "Concluir" faz o POST e vai à porta de entrada; o atalho leva ao gesto de abrir loja e caixa', async () => {
  servidor(estadoDaApi({ 'boas-vindas': 'conferido', horarios: 'depois' }, { cardapio: { prontidao: { ok: false, falta: ['cadastrar um item'] } } }))
  g.acesso = { acoes: ADMIN, entrada: '#/m/atendimento' }
  contextoDosRascunhos()
  const { raiz } = await montarTela({ passo: 'pronto' })
  const t = todoTexto(raiz)
  for (const grupo of ['Pronto', 'Falta', 'Deixado para depois']) assert.match(t, new RegExp(grupo))
  assert.match(t, /Falta cadastrar um item/)
  assert.match(t, /Revisão final/)
  assert.ok(!botao(raiz, 'Conferi, seguir'), 'o passo final não tem "Conferi, seguir"')
  const atalho = links(raiz).find((a) => a.props.href === '#/m/atendimento/horarios')
  assert.ok(atalho, 'atalho para abrir a loja e o caixa onde o gesto já existe')
  assert.doesNotMatch(t, /Abrir loja agora/)
  await clicar(raiz, 'Concluir')
  assert.deepEqual(escritas(), [{ metodo: 'POST', url: `${BASE}/concluir`, corpo: undefined }])
  assert.equal(janela.location.hash, '#/m/atendimento')
})

await provar('D9 "Concluir" recusado com 403 HIERARQUIA mostra o motivo e não sai; sem podeConcluir nem oferece', async () => {
  servidor(estadoDaApi())
  responder('POST', `${BASE}/concluir`, { status: 403, erro: { code: 'HIERARQUIA', message: 'Só a dona conclui.' } })
  g.acesso = { acoes: ADMIN, entrada: '#/' }
  contextoDosRascunhos()
  const { raiz } = await montarTela({ passo: 'pronto' })
  janela.location.hash = '#/m/configuracoes/primeiros-passos/pronto'
  await clicar(raiz, 'Concluir')
  assert.equal(janela.location.hash, '#/m/configuracoes/primeiros-passos/pronto')
  assert.match(avisoVivo(raiz), /Só a dona ou o suporte concluem os primeiros passos\./)
  raiz.desmontar()
  rede.rotas = []
  servidor(estadoDaApi({}, {}, { ehDona: false, podeConcluir: false, abrirSozinho: false }))
  const outra = await montarTela({ passo: 'pronto' })
  assert.ok(!botao(outra.raiz, 'Concluir'), 'Administração não vê Concluir')
  assert.match(todoTexto(outra.raiz), /Só a dona conclui os primeiros passos\./)
})

await provar('D10 "Continuar depois": a dona adia (POST) e vai à porta de entrada; a Administração só sai', async () => {
  servidor(estadoDaApi())
  g.acesso = { acoes: ADMIN, entrada: '#/m/atendimento' }
  contextoDosRascunhos()
  const { raiz } = await montarTela({ passo: 'cardapio' })
  await clicar(raiz, 'Continuar depois')
  assert.deepEqual(escritas(), [{ metodo: 'POST', url: `${BASE}/adiar`, corpo: undefined }])
  assert.equal(janela.location.hash, '#/m/atendimento')
  raiz.desmontar()
  rede.rotas = []; rede.chamadas = []
  servidor(estadoDaApi({}, {}, { ehDona: false, podeConcluir: false, abrirSozinho: false }))
  janela.location.hash = ''
  const outra = await montarTela({ passo: 'cardapio' })
  await clicar(outra.raiz, 'Continuar depois')
  assert.equal(escritas().length, 0, 'sem POST para quem não pode adiar')
  assert.equal(janela.location.hash, '#/m/atendimento')
})

await provar('D11 boas-vindas: confere o nome com PATCH só do nome, mostra o que o suporte ligou sem pedir chave', async () => {
  servidor(estadoDaApi())
  g.acesso = { acoes: ADMIN, entrada: '#/' }
  contextoDosRascunhos()
  responder('GET', '/api/integracoes', { dados: [{ provider: 'mercadopago', nome: 'Mercado Pago', ativo: true }, { provider: 'googlemaps', nome: 'Google Maps', ativo: true }] })
  responder('GET', '/api/integracoes/whatsapp/status', { status: 404, erro: { code: 'NOT_FOUND', message: 'Não encontrado.' } })
  responder('PATCH', '/api/auth/me', ({ corpo }) => ({ dados: { id: 'usu-1', nome: corpo.nome, temaPreferido: 'light' } }))
  g.acesso.conta = { nome: 'Ana Teste', casa: 'Casa' }
  const { raiz } = await montarTela({ passo: 'boas-vindas' })
  const t = todoTexto(raiz)
  assert.match(t, /O que ter em mãos/)
  assert.match(t, /O que você salvar aqui já vale para os clientes\./)
  assert.match(t, /Cobrança online/)
  assert.match(t, /Ligada/)
  assert.match(t, /O atendimento por WhatsApp não está ligado para a loja/)
  assert.ok(!andar(raiz.arvore).some((n) => n.props?.tipo === 'password' || n.props?.type === 'password'), 'nenhum campo de chave')
  assert.equal(campo(raiz, 'Seu nome').props.value, 'Ana Teste')
  mudar(raiz, 'Seu nome', 'Ana Maria Teste')
  await raiz.atualizar(2)
  assert.equal(botao(raiz, 'Conferi, seguir').props.disabled, true, 'nome editado e não salvo segura o passo')
  const formulario = andar(raiz.arvore).find((n) => n.type === 'form')
  await enviar(raiz, formulario)
  assert.deepEqual(chamadasDe('PATCH', '/api/auth/me').map((c) => c.corpo), [{ nome: 'Ana Maria Teste' }], 'só o nome, sem e-mail nem senha')
  assert.equal(g.sessaoGravada?.usuario?.nome, 'Ana Maria Teste', 'a sessão passa a mostrar o nome novo')
  assert.notEqual(botao(raiz, 'Conferi, seguir').props.disabled, true)
  assert.match(todoTexto(raiz), /Nome salvo\./)
})

await provar('D12 id desconhecido vindo da API: aparece com "Abrir em Configurações", sem quebrar a tela', async () => {
  const estado = estadoDaApi()
  estado.passos.push(passoDaApi('passo-novo-da-api'))
  servidor(estado)
  g.acesso = { acoes: ADMIN, entrada: '#/' }
  contextoDosRascunhos()
  const { raiz } = await montarTela({ passo: 'pronto' })
  const link = links(raiz).find((a) => texto(a) === 'Abrir em Configurações')
  assert.ok(link, 'o passo novo vira link para Configurações')
  assert.equal(link.props.href, '#/m/configuracoes')
})

await provar('D13 passo sem acesso: diz o motivo, não monta o ajuste e só oferece seguir', async () => {
  servidor(estadoDaApi({}, { equipe: { disponivel: false, motivo: 'Só quem administra a loja convida.' } }))
  g.acesso = { acoes: ADMIN, entrada: '#/' }
  contextoDosRascunhos()
  const { raiz } = await montarTela({ passo: 'equipe' })
  assert.match(todoTexto(raiz), /Só quem administra a loja convida\./)
  assert.equal(paineis(raiz).length, 0)
  assert.ok(!botao(raiz, 'Fazer depois'))
  await clicar(raiz, 'Seguir')
  assert.equal(escritas().length, 0)
  assert.equal(janela.location.hash, '#/m/configuracoes/primeiros-passos/dados-da-loja')
})

await provar('D14 a leitura que falha oferece tentar de novo', async () => {
  responder('GET', BASE, { status: 500, erro: { code: 'ERRO', message: 'O EasyStok caiu.' } })
  g.acesso = { acoes: ADMIN, entrada: '#/' }
  contextoDosRascunhos()
  const { raiz } = await montarTela()
  assert.match(todoTexto(raiz), /O EasyStok caiu\./)
  servidor(estadoDaApi())
  await clicar(raiz, 'Tentar novamente')
  assert.match(todoTexto(raiz), /Passo 1 de 7 · Boas-vindas/)
})

await provar('D15 cartão do hall: "Primeiros passos: 3 de 7 · Continuar" enquanto pendente, nada depois de concluir', async () => {
  const estado = estadoDaApi({ 'boas-vindas': 'conferido', horarios: 'depois', 'site-e-impressos': 'conferido' })
  const raiz = new Raiz({ type: CartaoPrimeirosPassos, props: { estado } })
  assert.match(todoTexto(raiz), /Primeiros passos: 3 de 7/)
  const continuar = links(raiz).find((a) => texto(a).includes('Continuar'))
  assert.equal(continuar?.props.href, '#/m/configuracoes/primeiros-passos/cardapio')
  assert.equal(new Raiz({ type: CartaoPrimeirosPassos, props: { estado: { ...estado, pendente: false } } }).arvore, null)
  assert.equal(new Raiz({ type: CartaoPrimeirosPassos, props: { estado: null } }).arvore, null)
})

await provar('D16 AbaAtendimento: secoes monta só horários, só a configuração sem o liga e desliga, ou a situação do agente', async () => {
  const { AbaAtendimento } = await carregar('features/gestao/atendimento/AbaAtendimento.jsx')
  const configuracao = {
    ativo: false, tom: 'acolhedor', nivelSugestao: 'Discreto', saudacaoPrimeiroContato: 'Oi', saudacaoRetorno: 'Oi de novo',
    fraseEspera: 'Um instante', mensagemForaArea: 'Não atendemos aí', modeloRetomadaNome: null, respiroMinutos: 5, tempoPreparoPadraoMinutos: 30, slaRespostaMinutos: 5,
  }
  g.contexto = {
    estado: { expediente: { carregado: true, mensagemForaDoHorario: 'Fechado', mensagemLojaFechada: 'Fechada' }, aberta: true, lojaAberta: null, agora: 0 },
    acoes: { recarregarExpediente: () => {}, carregarConfiguracao: async () => configuracao, salvarConfiguracao: async (c) => c, salvarMensagensExpediente: async () => {}, alternarLoja() {}, voltarAoHorario() {} },
  }
  g.acesso = { acoes: ADMIN }
  contextoDosRascunhos()
  const montar = async (props) => { const raiz = new Raiz({ type: AbaAtendimento, props }); await raiz.atualizar(); return raiz }
  const completa = await montar({ horariosAtendimento: { type: 'SlotHorarios', props: {} } })
  assert.match(todoTexto(completa), /Abrir loja agora|Fechar loja agora/, 'sem secoes a aba fica igual')
  assert.ok(campo(completa, 'Tom'))
  const horarios = await montar({ secoes: ['horarios'], horariosDaSemana: { type: 'FormularioHorariosDaSemana', props: {} } })
  assert.ok(andar(horarios.arvore).some((n) => n.type === 'FormularioHorariosDaSemana'))
  assert.ok(campo(horarios, 'Fora do horário'))
  assert.ok(!campo(horarios, 'Tom'))
  assert.doesNotMatch(todoTexto(horarios), /Abrir loja agora|Fechar loja agora/)
  const config = await montar({ secoes: ['configuracao'] })
  assert.ok(campo(config, 'Tom'))
  assert.doesNotMatch(todoTexto(config), /Atendimento automático/, 'sem o liga e desliga do agente')
  assert.ok(!andar(config.arvore).some((n) => n.type === 'input' && n.props?.type === 'checkbox'))
  const agente = await montar({ secoes: ['agente'] })
  assert.match(todoTexto(agente), /Atendimento automático: desligado/)
  assert.ok(links(agente).some((a) => a.props.href === '#/m/atendimento/horarios'), 'link para ligar em Atendimento')
  assert.ok(!andar(agente.arvore).some((n) => n.type === 'input' && n.props?.type === 'checkbox'))
})

// --- E. Catálogo C# de passos ----------------------------------------------------------------------------

await provar('E1 os ids batem com o catálogo C# (EasyStock.Domain) nos dois sentidos, ou com a lista do contrato', async () => {
  const raizDominio = fileURLToPath(new URL('../../EasyStock.Domain/', import.meta.url))
  const arquivos = (dir) => readdirSync(dir).flatMap((nome) => {
    const caminho = join(dir, nome)
    if (['bin', 'obj'].includes(nome)) return []
    return statSync(caminho).isDirectory() ? arquivos(caminho) : caminho.endsWith('.cs') ? [caminho] : []
  })
  const catalogo = arquivos(raizDominio).filter((c) => /ConfiguracaoInicial/.test(c)).map((c) => readFileSync(c, 'utf8')).find((c) => /"boas-vindas"/.test(c))
  const ids = dominio.PASSOS.map((p) => p.id)
  if (!catalogo) {
    console.log('      (catálogo C# ConfiguracaoInicial ausente neste worktree: conferido contra a lista do contrato)')
    assert.deepEqual(ids, IDS_DO_CONTRATO)
    return
  }
  const doCs = [...new Set([...catalogo.matchAll(/"([a-z]+(?:-[a-z]+)*)"/g)].map((m) => m[1]).filter((id) => IDS_DO_CONTRATO.includes(id) || /-/.test(id)))]
  for (const id of ids) assert.ok(doCs.includes(id), `${id} do console existe no C#`)
  for (const id of doCs.filter((x) => !['essencial', 'ajustes', 'fim', 'conferido', 'depois'].includes(x))) assert.ok(ids.includes(id), `${id} do C# existe no console`)
})

// --- F. Entrada do App -----------------------------------------------------------------------------------

await provar('F1 App: só abre sozinho do hall, uma vez por visita, com a chave, e o hall ganha o cartão; nada na demonstração', async () => {
  const app = fonte('app/App.jsx')
  const comAcesso = app.slice(app.indexOf('function AppComAcesso('))
  assert.match(comAcesso, /useConfiguracaoInicialApi\(\{ ativo: podeConfigurarLoja\(dados\?\.acoes\) \}\)/, 'chave ausente: nem lê')
  assert.match(comAcesso, /deveAbrirSozinho\(configuracaoInicial\.estado, \{ hashAtual: window\.location\.hash, hashNaEntrada, hashDaPorta: entradaDoPerfil\(dados\), jaAbriuNaVisita: primeirosPassosAbertos\.current \}\)/)
  assert.match(comAcesso, /primeirosPassosAbertos: true/)
  assert.match(comAcesso, /const \[hashNaEntrada\] = useState\(\(\) => window\.location\.hash\)/)
  assert.match(comAcesso, /<CartaoPrimeirosPassos estado=\{configuracaoInicial\.estado\} \/>/)
  const antes = app.slice(0, app.indexOf('function AppComAcesso(')).replace(/^import [\s\S]*?from '[^']+'$/gm, '')
  assert.doesNotMatch(antes, /useConfiguracaoInicialApi|deveAbrirSozinho|CartaoPrimeirosPassos/, 'fora do AppComAcesso (modo API) nada disso existe')
  const visita = await carregar('infra/api/visitaDaSessao.js')
  const dados = new Map()
  globalThis.sessionStorage = { getItem: (k) => dados.get(k) ?? null, setItem: (k, v) => dados.set(k, v), removeItem: (k) => dados.delete(k), get length() { return dados.size }, key: (i) => [...dados.keys()][i] }
  const sessao = { empresa: { id: 'emp-1' }, usuario: { id: 'usu-1' } }
  assert.equal(visita.lerVisita(sessao).primeirosPassosAbertos, undefined)
  visita.gravarVisita(sessao, { comandas: {}, ultimaConversa: null, primeirosPassosAbertos: true })
  assert.equal(visita.lerVisita(sessao).primeirosPassosAbertos, true)
  visita.gravarVisita(sessao, { comandas: {}, ultimaConversa: null, primeirosPassosAbertos: 'sim' })
  assert.equal(visita.lerVisita(sessao).primeirosPassosAbertos, undefined, 'só o booleano verdadeiro')
  for (const chamada of comAcesso.matchAll(/gravarVisita\(sessao, \{([^}]*)\}\)/g)) assert.match(chamada[1], /primeirosPassosAbertos/, 'toda gravação da visita leva a marca')
})

await provar('F2 tela de rota: Primeiros passos monta a tela com os mesmos slots e a moldura em tela cheia', async () => {
  const app = fonte('app/App.jsx')
  assert.match(app, /const SLOTS_DOS_AJUSTES = \{/)
  for (const slot of ['renderizarEditorCardapio', 'horariosAtendimento', 'janelasApi', 'horariosDaSemana', 'entregadoresApi', 'abasDaDemonstracao']) {
    assert.match(app.slice(app.indexOf('const SLOTS_DOS_AJUSTES = {')), new RegExp(`\\b${slot}\\b`), slot)
  }
  assert.equal((app.match(/\{\.\.\.SLOTS_DOS_AJUSTES\}/g) ?? []).length >= 3, true, 'ModalGestao, Respostas e a tela de rota recebem o mesmo objeto')
  assert.match(app, /<TelaConfiguracaoInicial passo=\{rota\.passo\} renderizarAba=\{renderizarAbaDoPasso\} \/>/)
  const moldura = fonte('features/hall/MolduraDoModulo.jsx')
  assert.match(moldura, /tela\?\.telaCheia === true/)
  assert.match(fonte('features/hall/moldura.module.css'), /\.telaCheia/)
  const tela = fonte('features/gestao/configuracao-inicial/TelaConfiguracaoInicial.jsx')
  assert.match(tela, /tituloRef\.current\?\.focus\(/, 'foco no título ao trocar de passo')
  assert.match(fonte('features/gestao/configuracao-inicial/configuracaoInicial.module.css'), /@media \(max-width: 640px\)/)
})

console.log(`\n${passou} verificações passaram${falhas.length ? `, ${falhas.length} falharam` : ''}.`)
if (falhas.length) process.exit(1)
