// Prova da #2070, formulários novos do console: Dados da loja, Site e impressos, Alertas de
// estoque, Caderno do agente, Avisos, Entregadores (extraído) e Horários (extraído).
//
//   A  domínio puro: normalização e validação de cada formulário;
//   B  montagem real dos componentes com a API simulada no fetch (o `chamarApi` é o de verdade):
//      editar sem salvar chama aoMudarPendencia(true), salvar chama a rota certa com o corpo certo,
//      sair da tela com edição avisa false, o erro da API aparece em português, o site não mostra os
//      campos proibidos e o foco do caderno vai e volta pelo `id` (document.getElementById simulado).
//
// Pixels e as larguras de 360, 768 e 1280 px ficam para o laboratório no navegador.
//
//   node ferramentas/prova-2070-formularios.mjs

import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { transformWithOxc } from 'vite'

const src = (caminho) => new URL(`../src/${caminho}`, import.meta.url)
const fonte = (caminho) => readFileSync(src(caminho), 'utf8')

// --- Ambiente: React mínimo que renderiza a árvore inteira, com hooks por instância ---------------

const COMPONENTES_REAIS = [
  'features/gestao/PartesDoAjuste', 'features/gestao/loja/AbaDadosDaLoja', 'features/gestao/vitrine/AbaSiteEImpressos',
  'features/gestao/estoque/AbaAlertasDeEstoque', 'features/gestao/caderno/AbaCadernoDoAgente', 'features/gestao/avisos/AbaAvisos',
  'features/entregas/CadastroEntregadores', 'features/automacoes/FormularioHorariosDaSemana',
  'features/automacoes/ModalHorariosDaSemana', 'features/automacoes/ModalAutomacoes',
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
const g = globalThis.__prova2070 = {}
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

g.sessao = { token: 'token', persistente: false, expiraEm: Date.now() + 3_600_000, empresa: { id: 'emp-1', nome: 'Casa' }, usuario: { id: 'usu-1', nome: 'Ana' } }
g.contexto = { acoes: {}, estado: {} }
g.acesso = { acoes: {} }
g.push = {}
// Foco: o elemento com o `id` na árvore atual; quem recebeu o foco fica em `g.foco`.
globalThis.document = {
  getElementById(id) {
    const no = g.raizAtual && andar(g.raizAtual.arvore).find((n) => n.props?.id === id)
    return no ? { focus: () => { g.foco = no } } : null
  },
}
const inline = (source) => ({ format: 'module', shortCircuit: true, source })
registerHooks({
  resolve(spec, ctx, next) {
    if (spec === 'react') return { url: 'data:text/javascript,const h=globalThis.__prova2070.hooks;export const {createContext,useContext,useState,useRef,useCallback,useEffect,useLayoutEffect,useMemo,useId}=h;export const forwardRef=(f)=>f', shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: 'data:text/javascript,export const jsx=(type,props,key)=>({type,props,key});export const jsxs=jsx;export const Fragment="fragment"', shortCircuit: true }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return next(spec + ext, ctx) } catch { /* próxima extensão */ } }
    }
    return next(spec, ctx)
  },
  load(caminho, ctx, next) {
    if (fontes.has(caminho)) return inline(fontes.get(caminho))
    if (caminho.endsWith('/infra/fonteDados.js')) return inline('export const API_BASE="";export const FONTE_API=true')
    if (caminho.endsWith('/infra/api/sessao.js')) return inline('export const lerSessao=()=>globalThis.__prova2070.sessao;export const identidadeDaSessao=(s)=>`${s?.empresa?.id??"-"}:${s?.usuario?.id??"-"}`;export const gravarSessao=(s)=>s;export const limparSessao=()=>{};export const atualizarTokens=(s)=>s;export const empresaDoToken=()=>null')
    if (caminho.endsWith('/infra/pushNavegador.js')) return inline('const p=()=>globalThis.__prova2070.push;export const pushSuportado=()=>p().suportado();export const permissaoPush=()=>p().permissao();export const pedirPermissaoPush=()=>p().pedirPermissao();export const inscricaoPushAtual=(...a)=>p().inscricaoAtual(...a);export const inscreverPushNoNavegador=(...a)=>p().inscrever(...a);export const associarInscricaoPush=(...a)=>p().associar(...a);export const desinscreverPushNoNavegador=(...a)=>p().desinscrever(...a)')
    if (caminho.endsWith('/aplicacao/contextos.js')) return inline('export const useAcoes=()=>globalThis.__prova2070.contexto.acoes;export const useAtendimento=()=>globalThis.__prova2070.contexto.estado;export const useCatalogo=()=>({})')
    if (caminho.endsWith('/aplicacao/acessoModulos.jsx')) return inline('export const useAcessoModulos=()=>globalThis.__prova2070.acesso')
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
const todoTexto = (raiz) => andar(raiz.arvore).map((n) => [n.props?.rotulo, n.props?.dica, n.props?.titulo, n.props?.descricao, n.props?.['aria-label']].filter(Boolean).join(' ')).join(' ') + ' ' + texto(raiz.arvore)
const campo = (raiz, rotulo) => andar(raiz.arvore).find((n) => n.props?.rotulo === rotulo)
const rotulos = (raiz) => andar(raiz.arvore).map((n) => n.props?.rotulo).filter(Boolean)
const botao = (raiz, nome) => andar(raiz.arvore).find((n) => n.type === 'Botao' && (texto(n).trim() === nome || n.props?.['aria-label'] === nome))
const formularios = (raiz) => andar(raiz.arvore).filter((n) => n.type === 'form')
const mudar = (raiz, rotulo, valor) => {
  const alvo = campo(raiz, rotulo)
  assert.ok(alvo, `campo "${rotulo}" na tela (há: ${rotulos(raiz).join(', ')})`)
  if (alvo.type === 'CampoReais') alvo.props.aoMudar(valor)
  else alvo.props.onChange({ target: { value: valor, checked: valor, setCustomValidity() {} } })
  raiz.renderizar()
}
const marcar = (raiz, rotulo, valor) => {
  const alvo = andar(raiz.arvore).find((n) => n.type === 'input' && n.props?.type === 'checkbox' && (n.props['aria-label'] === rotulo || texto(n).includes(rotulo)))
    ?? andar(raiz.arvore).find((n) => n.type === 'label' && texto(n).includes(rotulo))?.props.children.find?.((n) => n?.type === 'input')
  assert.ok(alvo, `caixa "${rotulo}" na tela`)
  alvo.props.onChange({ target: { checked: valor } })
  raiz.renderizar()
}
const enviar = async (raiz, formulario = formularios(raiz)[0], valido = true) => {
  assert.ok(formulario, 'há um formulário para enviar')
  await formulario.props.onSubmit({ preventDefault() {}, currentTarget: { reportValidity: () => valido, querySelectorAll: () => [] } })
  return raiz.atualizar()
}
const clicar = async (raiz, nome) => {
  const alvo = botao(raiz, nome)
  assert.ok(alvo, `botão "${nome}" na tela`)
  assert.notEqual(alvo.props.disabled, true, `botão "${nome}" habilitado`)
  await alvo.props.onClick?.({ preventDefault() {}, currentTarget: {} })
  return raiz.atualizar()
}
const espiao = () => { const chamadas = []; const fn = (...a) => { chamadas.push(a.length > 1 ? a : a[0]) }; fn.chamadas = chamadas; return fn }
const montar = async (Componente, props = {}) => {
  const contrato = { aoMudarPendencia: espiao(), aoSalvo: espiao(), ...props }
  const raiz = new Raiz({ type: Componente, props: contrato })
  await raiz.atualizar()
  return { raiz, ...contrato }
}

let passou = 0
const falhas = []
async function provar(nome, fn) {
  rede.chamadas = []; rede.rotas = []; g.acesso = { acoes: {} }; g.foco = null
  try { await fn(); passou++; console.log('ok    ' + nome) }
  catch (erro) { falhas.push(nome); console.log('FALHA ' + nome + '\n      ' + String(erro?.message ?? erro).split('\n').join('\n      ')) }
}
const carregar = async (caminho) => import(src(caminho).href)

// --- A. Domínio puro ---------------------------------------------------------------------------------

await provar('A1 dados da loja: os 5 campos voltam como vieram, o editado vai aparado e o apagado vira nulo', async () => {
  const d = await carregar('dominio/dadosDaLoja.js')
  assert.equal(d.lojaTemOsCincoCampos({ id: 'l1', nome: 'Casa' }), false, 'a API de hoje só devolve id, nome, ativa e logo')
  const loja = { id: 'l1', empresaId: 'emp-1', nome: 'Casa da Baba', ativa: true, descricao: 'Massas', documento: null, endereco: 'Rua A, 10', telefone: '(11) 3456-7890' }
  assert.equal(d.lojaTemOsCincoCampos(loja), true)
  assert.deepEqual(d.formularioDaLoja(loja), { nome: 'Casa da Baba', descricao: 'Massas', documento: '', endereco: 'Rua A, 10', telefone: '(11) 3456-7890' })
  assert.deepEqual(d.lojasAtivas([loja, { ...loja, id: 'l2', ativa: false }]).map((l) => l.id), ['l1'])
  const form = { ...d.formularioDaLoja(loja), telefone: ' (11) 98765-4321 ', descricao: '  ' }
  assert.equal(d.lojaMudou(form, loja), true)
  assert.equal(d.lojaMudou(d.formularioDaLoja(loja), loja), false)
  assert.deepEqual(d.corpoDaLoja(loja, form), {
    lojaId: 'l1', empresaId: 'emp-1', nome: 'Casa da Baba', descricao: null, documento: null, endereco: 'Rua A, 10', telefone: '(11) 98765-4321',
  })
  assert.deepEqual(d.errosDaLoja({ ...form, nome: '   ' }), { nome: 'Informe o nome da loja.' })
  assert.deepEqual(d.errosDaLoja({ ...form, nome: 'x'.repeat(151), telefone: '1'.repeat(21) }), {
    nome: 'Use até 150 caracteres.', telefone: 'Use até 20 caracteres.',
  })
  assert.deepEqual(d.errosDaLoja(form), {})
})

await provar('A2 site: endereço válido, corpo do PUT reenvia o que a tela não mostra e nunca leva campo proibido', async () => {
  const d = await carregar('dominio/vitrineDoSite.js')
  assert.equal(d.sugerirEnderecoDoSite('Casa da Babá!'), 'casa-da-baba')
  assert.deepEqual(d.errosDaCriacao({ tituloPublico: '', slug: 'ab' }), {
    tituloPublico: 'Informe o nome do site.', slug: 'Use de 3 a 40 letras minúsculas, números ou hífen.',
  })
  for (const ruim of ['casa--baba', '-casa', 'casa-', 'Casa Baba']) assert.ok(d.errosDaCriacao({ tituloPublico: 'Casa', slug: ruim }).slug, ruim)
  assert.deepEqual(d.errosDaCriacao({ tituloPublico: 'Casa', slug: 'casa-da-baba' }), {})
  assert.deepEqual(d.corpoDaCriacao({ tituloPublico: ' Casa da Baba ', slug: ' casa-da-baba ' }), { tituloPublico: 'Casa da Baba', slug: 'casa-da-baba' })

  const config = {
    tituloPublico: 'Casa da Baba', slug: 'casa-da-baba', subtituloPublico: null, logoUrl: '/uploads/logo-antigo.png', corPrimaria: '#8b1e3f',
    whatsappPedidos: '11999990000', mensagemForaArea: 'Ainda não chegamos aí', pedidoMinimoEntrega: 30, freteGratisAcima: null,
    dominioCustom: 'casadababa.com', modeloFiscal: 'manual', nfeAutomaticaHabilitada: false, lojaPadraoId: 'l1', ativo: true,
  }
  const form = d.formularioDoSite(config)
  assert.deepEqual(form, { subtituloPublico: '', corPrimaria: '#8b1e3f', whatsappPedidos: '11999990000', pedidoMinimoCentavos: 3000, freteGratisCentavos: null })
  assert.equal(d.siteMudou(form, config), false)
  const editado = { ...form, subtituloPublico: ' Massas frescas ' }
  assert.equal(d.siteMudou(editado, config), true)
  const corpo = d.corpoDoSite(config, editado)
  assert.deepEqual(corpo, {
    subtituloPublico: 'Massas frescas', logoUrl: '/uploads/logo-antigo.png', corPrimaria: '#8b1e3f', whatsappPedidos: '11999990000',
    mensagemForaArea: 'Ainda não chegamos aí',
  }, 'o grupo de marca é gravado inteiro: o que a tela não mostra vai exatamente como veio')
  for (const proibido of ['ativo', 'dominioCustom', 'lojaPadraoId', 'modeloFiscal', 'habilitarNfeAutomatica', 'tituloPublico']) assert.equal(proibido in corpo, false, proibido)
  assert.deepEqual(d.corpoDoSite(config, { ...form, pedidoMinimoCentavos: 2550, freteGratisCentavos: 15000 }), {
    subtituloPublico: '', logoUrl: '/uploads/logo-antigo.png', corPrimaria: '#8b1e3f', whatsappPedidos: '11999990000',
    mensagemForaArea: 'Ainda não chegamos aí', pedidoMinimoEntrega: 25.5, freteGratisAcima: 150,
  }, 'subtítulo sem valor vai vazio: o grupo de marca nunca chega todo nulo')
  assert.equal(d.corpoDoSite(config, { ...form, corPrimaria: '' }).corPrimaria, null, 'cor apagada volta à cor padrão do site')
  assert.equal(d.corpoDoSite(config, { ...form, whatsappPedidos: '11987654321' }).whatsappPedidos, '(11) 98765-4321')
  // A API só grava a marca se um dos cinco vier não nulo (EditarStorefrontAdminUseCase); vazio limpa.
  const soWhatsApp = { ...config, subtituloPublico: null, logoUrl: null, corPrimaria: null, whatsappPedidos: '(11) 99999-0000', mensagemForaArea: null }
  assert.deepEqual(d.corpoDoSite(soWhatsApp, { ...d.formularioDoSite(soWhatsApp), whatsappPedidos: '' }), {
    subtituloPublico: '', logoUrl: null, corPrimaria: null, whatsappPedidos: '', mensagemForaArea: null,
  }, 'apagar o único WhatsApp manda vazio, e a marca é gravada')
  const soCor = { ...soWhatsApp, whatsappPedidos: null, corPrimaria: '#8b1e3f' }
  assert.deepEqual(d.corpoDoSite(soCor, { ...d.formularioDoSite(soCor), corPrimaria: '' }), {
    subtituloPublico: '', logoUrl: null, corPrimaria: null, whatsappPedidos: null, mensagemForaArea: null,
  }, 'voltar a única cor para a padrão: cor nula (a API recusa vazio) e o subtítulo vazio faz a marca ser gravada')
  assert.equal(d.corpoDoSite({ ...config, subtituloPublico: 'Massas' }, { ...form, subtituloPublico: '  ' }).subtituloPublico, '', 'subtítulo apagado vai vazio')
  assert.equal(d.logoDosImpressos('/uploads/loja.png', '/uploads/site.png'), '/uploads/loja.png', 'os impressos usam o logo da loja antes do do site')
  assert.equal(d.logoDosImpressos(null, '/uploads/site.png'), '/uploads/site.png')
  assert.equal(d.logoDosImpressos('', ''), null)
  assert.equal(d.siteComOutroLogo('/uploads/loja.png', '/uploads/site.png'), true)
  assert.equal(d.siteComOutroLogo('/uploads/loja.png', null), true)
  assert.equal(d.siteComOutroLogo('/uploads/loja.png', '/uploads/loja.png'), false)
  assert.equal(d.siteComOutroLogo(null, '/uploads/site.png'), false, 'sem logo na loja não há o que levar ao site')
  assert.deepEqual(d.errosDoSite({ ...form, corPrimaria: 'vermelho', whatsappPedidos: '123' }, config), {
    corPrimaria: 'Use o código da cor, como #8B1E3F.', whatsappPedidos: 'Informe o WhatsApp com DDD.',
  })
  assert.deepEqual(d.errosDoSite({ ...form, subtituloPublico: 'x'.repeat(241) }, config), { subtituloPublico: 'Use até 240 caracteres.' })
  assert.deepEqual(d.errosDoSite({ ...form, pedidoMinimoCentavos: 10_000_000 }, config), { pedidoMinimoCentavos: 'Valor alto demais.' })
  assert.deepEqual(d.errosDoSite({ ...form, freteGratisCentavos: null }, { ...config, freteGratisAcima: 100 }), {
    freteGratisCentavos: 'O EasyStok ainda não apaga o frete grátis. Informe um valor.',
  })
  assert.deepEqual(d.errosDoSite(form, config), {})
  assert.deepEqual(d.corpoDoLogoNoSite(config, '/uploads/novo.png'), {
    subtituloPublico: '', logoUrl: '/uploads/novo.png', corPrimaria: '#8b1e3f', whatsappPedidos: '11999990000', mensagemForaArea: 'Ainda não chegamos aí',
  })
  assert.equal(d.motivoDoArquivoDeLogo({ type: 'image/gif', size: 10 }), 'Escolha uma imagem PNG ou JPG.')
  assert.equal(d.motivoDoArquivoDeLogo({ type: 'image/png', size: 5 * 1024 * 1024 + 1 }), 'A imagem não pode ser maior que 5 MB.')
  assert.equal(d.motivoDoArquivoDeLogo({ type: 'image/jpeg', size: 1000 }), null)
})

await provar('A3 alertas de estoque: limites do Web, crítico menor que o mínimo e PATCH só com o que mudou', async () => {
  const d = await carregar('dominio/alertasDeEstoque.js')
  const config = { lojaId: 'l1', diasAlertaValidade: 7, diasAlertaParado: 30, quantidadeMinimaPadrao: 10, quantidadeCriticaPadrao: 3, fifoAtivo: true, moeda: 'BRL', timezone: 'America/Sao_Paulo', kdsHabilitado: true }
  const form = d.formularioDosAlertas(config)
  assert.deepEqual(form, { diasAlertaValidade: '7', diasAlertaParado: '30', quantidadeMinimaPadrao: '10', quantidadeCriticaPadrao: '3', ordemDeSaida: 'fefo' })
  assert.deepEqual(d.ORDENS_DE_SAIDA.map((o) => o.valor), ['fefo', 'fifo'])
  assert.equal(d.alertasMudaram(form, config), false)
  assert.deepEqual(d.corpoDosAlertas(config, { ...form, diasAlertaValidade: '15', ordemDeSaida: 'fifo' }, { empresaId: 'emp-1', lojaId: 'l1' }), {
    empresaId: 'emp-1', lojaId: 'l1', diasAlertaValidade: 15, fifoAtivo: false,
  })
  assert.deepEqual(d.errosDosAlertas({ ...form, diasAlertaValidade: '0', diasAlertaParado: '366' }), {
    diasAlertaValidade: 'Use um número inteiro de 1 a 365.', diasAlertaParado: 'Use um número inteiro de 1 a 365.',
  })
  assert.deepEqual(d.errosDosAlertas({ ...form, quantidadeMinimaPadrao: '', quantidadeCriticaPadrao: '2.5' }), {
    quantidadeMinimaPadrao: 'Use um número inteiro de 0 a 9999.', quantidadeCriticaPadrao: 'Use um número inteiro de 0 a 9999.',
  })
  assert.deepEqual(d.errosDosAlertas({ ...form, quantidadeCriticaPadrao: '10' }), { quantidadeCriticaPadrao: 'O nível crítico deve ser menor que o mínimo.' })
  assert.deepEqual(d.errosDosAlertas(form), {})
})

await provar('A4 caderno: palavras-chave como a API grava, limites e teto do núcleo sem contar o trecho duas vezes', async () => {
  const d = await carregar('dominio/cadernoDoAgente.js')
  assert.equal(d.normalizarPalavrasChave(' Frete; Bairro, frete ,, '), 'frete, bairro')
  const trechos = [
    { id: 't1', titulo: 'Entrega', texto: 'x'.repeat(5000), palavrasChave: '', nucleo: true, arquivado: false },
    { id: 't2', titulo: 'Alergias', texto: 'Sem glúten não', palavrasChave: 'gluten', nucleo: false, arquivado: false },
    { id: 't3', titulo: 'Antigo', texto: 'y'.repeat(3000), palavrasChave: '', nucleo: true, arquivado: true },
  ]
  assert.deepEqual(d.trechosAtivos(trechos).map((t) => t.id), ['t2', 't1'], 'ativos em ordem de título')
  assert.equal(d.tamanhoDoNucleo(trechos, { ...d.TRECHO_VAZIO, texto: 'z'.repeat(900), nucleo: true }, null), 5900)
  assert.equal(d.tamanhoDoNucleo(trechos, { ...d.formularioDoTrecho(trechos[0]), texto: 'x'.repeat(5500) }, 't1'), 5500)
  assert.deepEqual(d.errosDoTrecho({ ...d.TRECHO_VAZIO, texto: 'z'.repeat(1500), nucleo: true }, trechos, null), {
    titulo: 'Informe o título.', nucleo: 'O núcleo passaria de 6.000 caracteres (ficaria com 6.500). Deixe este trecho fora do núcleo: o agente ainda o encontra pelo índice.',
  })
  assert.deepEqual(d.errosDoTrecho({ titulo: 't'.repeat(121), texto: 'z'.repeat(4001), palavrasChave: 'p'.repeat(301), nucleo: false }, trechos, null), {
    titulo: 'Use até 120 caracteres.', texto: 'Use até 4.000 caracteres.', palavrasChave: 'Use até 300 caracteres.',
  })
  assert.deepEqual(d.errosDoTrecho({ ...d.TRECHO_VAZIO, titulo: 'Oi' }, trechos, null), { texto: 'Escreva o texto do trecho.' })
  assert.equal(d.trechoMudou(d.TRECHO_VAZIO, null), false)
  assert.equal(d.trechoMudou({ ...d.TRECHO_VAZIO, titulo: 'a' }, null), true)
  assert.equal(d.trechoMudou(d.formularioDoTrecho(trechos[1]), trechos[1]), false)
  assert.deepEqual(d.corpoDoTrecho({ titulo: ' Frete ', texto: ' Até 5 km grátis ', palavrasChave: 'Frete;KM', nucleo: true }), {
    titulo: 'Frete', texto: 'Até 5 km grátis', palavrasChave: 'frete, km', nucleo: true,
  })
})

// O catálogo de rotinas da API, lido do seed: código, evento e canais de cada rotina ativa.
const raizDoRepo = new URL('../../', import.meta.url)
function rotinasDoCatalogo() {
  const seed = readFileSync(new URL('EasyStock.Api/Data/NotificacoesGlobaisSeed.cs', raizDoRepo), 'utf8')
  const constantes = Object.fromEntries([...seed.matchAll(/const string (\w+) = """(.*?)""";/g)].map(([, nome, valor]) => [nome, valor]))
  const canaisDe = (literal) => JSON.parse(literal.startsWith('"') ? JSON.parse(literal) : constantes[literal])
  const corpo = seed.slice(seed.indexOf('BuildDefaultRotinas'))
  const rotinas = new Map()
  for (const [, codigo, evento, fallback] of corpo.matchAll(/codigo: "(\w+)",[^;]*?tipoEvento: TipoEventoNotificacao\.(\w+)[^;]*?;\s*\w+\.DefinirFallback\(("(?:[^"\\]|\\.)*")/g)) {
    rotinas.set(codigo, { evento, canais: canaisDe(fallback) })
  }
  for (const [, codigo, evento, fallback] of corpo.matchAll(/MakeRotina\("(\w+)",\s*"[^"]*",\s*TipoEventoNotificacao\.(\w+),\s*"\w+",\s*CategoriaConteudoNotificacao\.\w+,\s*("(?:[^"\\]|\\.)*"|\w+)/g)) {
    rotinas.set(codigo, { evento, canais: canaisDe(fallback) })
  }
  for (const [, variavel, codigo] of corpo.matchAll(/var (\w+) = MakeRotina\("(\w+)"/g)) {
    if (new RegExp(`${variavel}\\.Desativar\\(`).test(corpo)) rotinas.delete(codigo)
  }
  return rotinas
}

// Quem publica cada evento fora do catálogo, da política de validade e dos exemplos.
function eventosPublicados() {
  const ignorar = /[\\/](bin|obj|Migrations)[\\/]|NotificacoesGlobaisSeed|RemetentePorTipoEvento|PoliticaValidadeNotificacao|ExemplosDeEvento|TipoEventoNotificacao\.cs$/
  const publicados = new Set()
  const andarPasta = (pasta) => {
    for (const entrada of readdirSync(pasta, { withFileTypes: true })) {
      const caminho = join(pasta, entrada.name)
      if (ignorar.test(caminho)) continue
      if (entrada.isDirectory()) andarPasta(caminho)
      else if (entrada.name.endsWith('.cs')) for (const [, evento] of readFileSync(caminho, 'utf8').matchAll(/TipoEventoNotificacao\.(\w+)/g)) publicados.add(evento)
    }
  }
  const raiz = fileURLToPath(raizDoRepo)
  for (const projeto of readdirSync(raiz)) {
    if (/^EasyStock\.(Application|Domain|Api|Infra\.[A-Za-z]+)$/.test(projeto)) andarPasta(join(raiz, projeto))
  }
  return publicados
}

// Rotina ativa do catálogo que não chega à dona, com o motivo. Rotina nova no seed sem lugar aqui
// nem em AVISOS_DA_DONA reprova a prova.
const SEM_PUBLICADOR = 'nenhum código publica o evento'
const FORA_DA_DONA = {
  assinatura_expirando_global: SEM_PUBLICADOR, assinatura_expirada_dunning_global: SEM_PUBLICADOR,
  alerta_estoque_critico_global: SEM_PUBLICADOR, produto_vencido_global: SEM_PUBLICADOR, tarefa_pendente_global: SEM_PUBLICADOR,
  ticket_criado_global: SEM_PUBLICADOR, ticket_respondido_admin_global: SEM_PUBLICADOR, ticket_status_alterado_global: SEM_PUBLICADOR,
  ticket_atribuido_global: SEM_PUBLICADOR, ticket_encaminhado_global: SEM_PUBLICADOR, sla_proximo_vencer_global: SEM_PUBLICADOR,
  sla_violado_global: SEM_PUBLICADOR, bug_fix_criado_global: SEM_PUBLICADOR, ticket_respondido_cliente_global: SEM_PUBLICADOR,
  convite_csat_global: SEM_PUBLICADOR, fatura_criada_global: SEM_PUBLICADOR, fatura_vencendo_global: SEM_PUBLICADOR,
  fatura_paga_global: SEM_PUBLICADOR, fatura_vencida_global: SEM_PUBLICADOR, pagamento_confirmado_global: SEM_PUBLICADOR,
  pagamento_falhou_global: SEM_PUBLICADOR, broadcast_super_admin_global: SEM_PUBLICADOR, relatorio_pronto_global: SEM_PUBLICADOR,
  relatorio_falhou_global: SEM_PUBLICADOR, relatorio_expirado_global: SEM_PUBLICADOR,
  produto_vencendo_global: 'o coletor publica sem destinatário e a rotina não tem audiência: o e-mail não tem para quem ir',
  pedido_pago_confirmado_global: 'vai ao cliente', pedido_em_preparo_global: 'vai ao cliente', pedido_reagendado_global: 'vai ao cliente',
  comanda_atualizada_global: 'vai ao cliente', pedido_saiu_para_entrega_global: 'vai ao cliente', pedido_entregue_global: 'vai ao cliente',
  avaliacao_solicitada_global: 'vai ao cliente', reembolso_efetuado_global: 'vai ao cliente',
  incidente_sistema_global: 'vai ao suporte da plataforma (superadmins)',
}

await provar('A5 avisos: cada aviso bate com o catálogo da API e com os avisos da tela; toda rotina ativa chega à dona ou tem motivo', async () => {
  const d = await carregar('dominio/avisosDaDona.js')
  const { TIPOS_DE_AVISO } = await carregar('dominio/aviso.js')
  const { TELA, APARELHO, SINO, EMAIL, WHATSAPP } = d.CANAIS_DE_AVISO
  const catalogo = rotinasDoCatalogo()
  assert.equal(catalogo.has('resumo_diario_global'), false, 'o molde inativo do resumo diário não conta')
  assert.deepEqual(catalogo.get('contato_novo_balcao_global'), { evento: 'ContatoNovoNoBalcao', canais: ['Push', 'InApp'] }, 'a leitura do seed resolve a constante')

  const daDona = d.AVISOS_DA_DONA.flatMap((aviso) => aviso.rotinas)
  assert.equal(new Set(daDona).size, daDona.length, 'cada rotina num aviso só')
  assert.deepEqual(daDona.filter((codigo) => codigo in FORA_DA_DONA), [], 'rotina da dona não está na lista de fora')
  assert.deepEqual([...new Set([...daDona, ...Object.keys(FORA_DA_DONA)])].sort(), [...catalogo.keys()].sort(),
    'toda rotina ativa do catálogo está num aviso da dona ou tem motivo escrito, e nenhum aviso cita rotina que não existe')

  const publicados = eventosPublicados()
  const semPublicador = Object.keys(FORA_DA_DONA).filter((codigo) => FORA_DA_DONA[codigo] === SEM_PUBLICADOR)
  assert.deepEqual(semPublicador.filter((codigo) => publicados.has(catalogo.get(codigo).evento)), [], 'quem é "sem publicador" não tem publicador')
  assert.deepEqual(daDona.filter((codigo) => !publicados.has(catalogo.get(codigo).evento)), [], 'todo aviso da dona tem quem publique o evento')

  const ORDEM = [TELA, APARELHO, SINO, EMAIL, WHATSAPP]
  for (const aviso of d.AVISOS_DA_DONA) {
    const esperado = new Set(aviso.rotinas.flatMap((codigo) => catalogo.get(codigo).canais.map((canal) => d.CANAL_DA_ROTINA[canal])))
    if (aviso.tela) esperado.add(TELA)
    if (aviso.rotinas.length === 0 && !aviso.tela) esperado.add(SINO) // alertas do analisador de estoque
    assert.deepEqual(aviso.canais, ORDEM.filter((canal) => esperado.has(canal)), aviso.titulo)
  }
  assert.deepEqual(d.AVISOS_DA_DONA.map((a) => a.tela).filter(Boolean).sort(), Object.values(TIPOS_DE_AVISO).sort(), 'os cinco avisos da tela do Balcão')
  assert.deepEqual(d.AVISOS_DA_DONA.filter((a) => a.rotinas.length === 0 && !a.tela).map((a) => a.titulo), ['Alertas de estoque'])
  assert.match(readFileSync(new URL('EasyStock.Api/Services/GeradorNotificacoesAutomaticas.cs', raizDoRepo), 'utf8'), /sino da equipe/,
    'os alertas de estoque vão ao sino pelo analisador, fora do catálogo')
  assert.equal(new Set(d.AVISOS_DA_DONA.map((a) => a.titulo)).size, d.AVISOS_DA_DONA.length, 'títulos únicos')
})

// --- B. Montagem com a API simulada -------------------------------------------------------------------

// A loja como `GET api/lojas` devolve hoje (`LojaResult`). A prova abaixo confere os nomes contra o
// record: quando o backend passar a devolver os 5 campos, esta fixture e o cartão só leitura mudam.
const LOJA = { id: 'l1', empresaId: 'emp-1', nome: 'Casa da Baba', ativa: true, logoUrl: null }
// Só para o caminho de edição, que depende de o `LojaResult` ganhar descrição, documento, endereço e telefone.
const LOJA_COM_OS_CINCO = { ...LOJA, descricao: 'Massas', documento: null, endereco: 'Rua A, 10', telefone: '(11) 3456-7890' }
const LOJAS = '/api/lojas?empresaId=emp-1'

await provar('B1 dados da loja: a fixture LOJA é a forma real do LojaResult, e com ela cada cartão é só leitura', async () => {
  const record = readFileSync(new URL('EasyStock.Application/UseCases/Loja/LojaResult.cs', raizDoRepo), 'utf8')
  const parametros = record.match(/record LojaResult\(([^)]*)\)/)[1].split(',')
    .map((parte) => parte.trim().split('=')[0].trim().split(/\s+/).at(-1))
    .map((nome) => nome[0].toLowerCase() + nome.slice(1))
  assert.deepEqual(Object.keys(LOJA), parametros, 'LOJA tem exatamente os campos que a API devolve')
  const { AbaDadosDaLoja } = await carregar('features/gestao/loja/AbaDadosDaLoja.jsx')
  responder('GET', LOJAS, { dados: [LOJA] })
  const { raiz, aoMudarPendencia } = await montar(AbaDadosDaLoja)
  assert.equal(formularios(raiz).length, 0, 'com a leitura de hoje não há o que salvar sem apagar')
  assert.match(texto(raiz.arvore), /Estes dados ainda não podem ser editados aqui; fale com o suporte\./)
  assert.deepEqual(aoMudarPendencia.chamadas, [])
  assert.equal(escritas().length, 0)
})

await provar('B1 dados da loja (quando a leitura trouxer os 5 campos): um cartão por loja ativa; editar avisa pendência; salvar reenvia os 5 no PUT', async () => {
  const { AbaDadosDaLoja } = await carregar('features/gestao/loja/AbaDadosDaLoja.jsx')
  responder('GET', LOJAS, { dados: [LOJA_COM_OS_CINCO, { ...LOJA_COM_OS_CINCO, id: 'l2', nome: 'Fechada', ativa: false }] })
  responder('PUT', '/api/lojas/l1', { status: 204 })
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(AbaDadosDaLoja)
  assert.equal(andar(raiz.arvore).filter((n) => n.props?.rotulo === 'Nome da loja').length, 1, 'só a loja ativa')
  assert.equal(campo(raiz, 'Descrição').props.value, 'Massas')
  assert.equal(botao(raiz, 'Salvar dados da loja').props.disabled, true, 'nada para salvar ainda')
  mudar(raiz, 'Telefone', '(11) 98765-4321')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  assert.equal(escritas().length, 0, 'editar não grava')
  await enviar(raiz)
  assert.deepEqual(chamadasDe('PUT', '/api/lojas/l1').map((c) => c.corpo), [{
    lojaId: 'l1', empresaId: 'emp-1', nome: 'Casa da Baba', descricao: 'Massas', documento: null, endereco: 'Rua A, 10', telefone: '(11) 98765-4321',
  }])
  assert.equal(aoSalvo.chamadas.length, 1)
  assert.equal(aoMudarPendencia.chamadas.at(-1), false)
  assert.match(texto(raiz.arvore), /Salvo no EasyStok/)
})

await provar('B1 dados da loja: erro da API em português e a edição fica; sem os 5 campos na leitura não deixa apagar', async () => {
  const { AbaDadosDaLoja } = await carregar('features/gestao/loja/AbaDadosDaLoja.jsx')
  responder('GET', LOJAS, { dados: [LOJA_COM_OS_CINCO] })
  responder('PUT', '/api/lojas/l1', { status: 400, erro: { code: 'VALIDACAO', message: 'Requisição inválida', detail: 'Nome da loja é obrigatório.' } })
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(AbaDadosDaLoja)
  mudar(raiz, 'Endereço', 'Rua B, 20')
  await enviar(raiz)
  assert.match(texto(raiz.arvore), /Nome da loja é obrigatório\./)
  assert.equal(campo(raiz, 'Endereço').props.value, 'Rua B, 20')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  assert.equal(aoSalvo.chamadas.length, 0)

  rede.rotas = []; rede.chamadas = []
  responder('GET', LOJAS, { dados: [LOJA] })
  const curta = await montar(AbaDadosDaLoja)
  assert.deepEqual(rotulos(curta.raiz), [], 'sem a leitura completa nenhum campo aparece vazio, como se não existisse')
  assert.equal(botao(curta.raiz, 'Salvar dados da loja'), undefined)
  assert.equal(formularios(curta.raiz).length, 0, 'sem a leitura completa, salvar apagaria descrição, documento, endereço e telefone')
  assert.match(texto(curta.raiz.arvore), /Casa da Baba/)
  assert.match(texto(curta.raiz.arvore), /Estes dados ainda não podem ser editados aqui; fale com o suporte\./)
  assert.doesNotMatch(texto(curta.raiz.arvore), /versão|devolve/, 'o texto fala com a dona, não da versão da API')
  assert.deepEqual(curta.aoMudarPendencia.chamadas, [])
  assert.equal(escritas().length, 0)
})

await provar('B1 dados da loja: sair da tela com a edição aberta avisa que a pendência acabou', async () => {
  const { AbaDadosDaLoja } = await carregar('features/gestao/loja/AbaDadosDaLoja.jsx')
  responder('GET', LOJAS, { dados: [LOJA_COM_OS_CINCO] })
  const { raiz, aoMudarPendencia } = await montar(AbaDadosDaLoja)
  mudar(raiz, 'Telefone', '(11) 98765-4321')
  assert.deepEqual(aoMudarPendencia.chamadas, [true])
  raiz.desmontar()
  assert.deepEqual(aoMudarPendencia.chamadas, [true, false], 'a edição local se perde ao sair: quem monta deixa de bloquear')
  assert.equal(escritas().length, 0)
})

const CONFIG_SITE = {
  id: 's1', empresaId: 'emp-1', slug: 'casa-da-baba', tituloPublico: 'Casa da Baba', subtituloPublico: 'Massas', logoUrl: '/uploads/antigo.png',
  corPrimaria: '#8b1e3f', whatsappPedidos: '(11) 99999-0000', pedidoMinimoEntrega: 30, freteGratisAcima: null, mensagemForaArea: 'Ainda não chegamos aí',
  dominioCustom: 'casadababa.com', modeloFiscal: 'manual', nfeAutomaticaHabilitada: false, lojaPadraoId: 'l1', ativo: true,
}
const PROIBIDOS = /ativar|desativar|publicar|domínio|dominio|fora da área|loja padrão|fiscal|nota fiscal|nf-?e/i
// O PUT de `configuracao` como a API faz (EditarStorefrontAdminUseCase e Storefront.AtualizarBranding):
// nulo não muda; a marca só é gravada se um dos cinco vier não nulo, e aí os cinco ficam como vieram.
const MARCA_DO_SITE = ['subtituloPublico', 'logoUrl', 'corPrimaria', 'whatsappPedidos', 'mensagemForaArea']
const putDoSite = (gravado, corpo) => {
  const novo = { ...gravado }
  if (MARCA_DO_SITE.some((c) => corpo[c] != null)) for (const c of MARCA_DO_SITE) novo[c] = corpo[c] == null ? null : String(corpo[c]).trim()
  if (corpo.pedidoMinimoEntrega != null) novo.pedidoMinimoEntrega = corpo.pedidoMinimoEntrega
  if (corpo.freteGratisAcima != null) novo.freteGratisAcima = corpo.freteGratisAcima
  return novo
}
const VITRINE = { dados: { storefrontId: 's1', slug: 'casa-da-baba', tituloPublico: 'Casa da Baba', ativo: true } }
const PUT_DO_SITE = '/api/minha-vitrine/configuracao'
const previaDoLogo = (raiz) => andar(raiz.arvore).find((n) => n.type === 'img')

await provar('B2 site: sem site oferece criar com nome e código; criar faz POST e mostra a configuração', async () => {
  const { AbaSiteEImpressos } = await carregar('features/gestao/vitrine/AbaSiteEImpressos.jsx')
  let criado = false
  responder('GET', '/api/minha-vitrine', () => (criado ? { dados: { storefrontId: 's1', slug: 'casa-da-baba', tituloPublico: 'Casa da Baba', ativo: false } } : { status: 404, erro: { code: 'NAO_ENCONTRADO', message: 'Sua vitrine ainda não foi criada.' } }))
  responder('GET', '/api/minha-vitrine/configuracao', { dados: { ...CONFIG_SITE, ativo: false } })
  responder('GET', LOJAS, { dados: [LOJA] })
  responder('POST', '/api/minha-vitrine', () => { criado = true; return { status: 201, dados: { storefrontId: 's1', slug: 'casa-da-baba' } } })
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(AbaSiteEImpressos)
  assert.deepEqual(rotulos(raiz), ['Nome do site', 'Código do site'])
  assert.doesNotMatch(todoTexto(raiz), /endereço|link do site/i, 'o código não é o link que o cliente abre')
  assert.match(texto(raiz.arvore), /O site nasce fora do ar: quem coloca no ar é o suporte\./, 'criar não promete site no ar')
  mudar(raiz, 'Nome do site', 'Casa da Babá')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  assert.match(texto(raiz.arvore), /Alterações não salvas\. Crie o site ou apague o que digitou antes de sair\./)
  assert.doesNotMatch(texto(raiz.arvore), /desfaça/, 'a criação não tem botão de desfazer')
  assert.equal(campo(raiz, 'Código do site').props.value, 'casa-da-baba', 'o código nasce do nome enquanto ela não mexe nele')
  await enviar(raiz)
  assert.deepEqual(chamadasDe('POST', '/api/minha-vitrine').map((c) => c.corpo), [{ tituloPublico: 'Casa da Babá', slug: 'casa-da-baba' }])
  assert.equal(aoSalvo.chamadas.length, 1)
  assert.equal(aoMudarPendencia.chamadas.at(-1), false)
  assert.ok(campo(raiz, 'Subtítulo'), 'depois de criar, a configuração aparece')
  assert.match(texto(raiz.arvore), /Fora do ar: os clientes ainda não abrem o site\. Quem coloca o site no ar é o suporte\./)
  assert.deepEqual(andar(raiz.arvore).filter((n) => n.type === 'Botao' && /no ar/i.test(texto(n))), [], 'a situação é só leitura')
})

await provar('B2 site: mostra só os campos do passo e o PUT reenvia o grupo de marca inteiro', async () => {
  const { AbaSiteEImpressos } = await carregar('features/gestao/vitrine/AbaSiteEImpressos.jsx')
  responder('GET', '/api/minha-vitrine', VITRINE)
  responder('GET', '/api/minha-vitrine/configuracao', { dados: CONFIG_SITE })
  responder('GET', LOJAS, { dados: [LOJA] })
  responder('PUT', PUT_DO_SITE, ({ corpo }) => ({ dados: putDoSite(CONFIG_SITE, corpo) }))
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(AbaSiteEImpressos)
  assert.deepEqual(rotulos(raiz), ['WhatsApp de pedidos', 'Subtítulo', 'Cor principal', 'Pedido mínimo para entrega', 'Frete grátis acima de'])
  assert.doesNotMatch(todoTexto(raiz), PROIBIDOS, 'nada de ativar o site, domínio, mensagem de fora da área, loja padrão ou fiscal')
  assert.doesNotMatch(todoTexto(raiz), /Ainda não chegamos aí|casadababa\.com/, 'o valor dos campos fora do passo não aparece')
  assert.match(texto(raiz.arvore), /No ar: os clientes já abrem o site\./)
  const identificacao = andar(raiz.arvore).find((n) => n.type === 'p' && /casa-da-baba/.test(texto(n)))
  assert.equal(texto(identificacao), 'Site “Casa da Baba”, código casa-da-baba. O nome e o código ficam como foram criados.')
  // Só o WhatsApp e o logo mudam algo hoje (impressos); o resto fica guardado e a tela diz isso.
  assert.match(campo(raiz, 'WhatsApp de pedidos').props.dica, /Sai nos impressos dos pedidos\./)
  const aviso = andar(raiz.arvore).find((n) => /ficam guardados/.test(texto(n)) && n.props?.id)
  assert.ok(aviso, 'o aviso do que ainda não vale para os clientes')
  assert.equal(texto(aviso), 'Subtítulo, cor, pedido mínimo e frete grátis ficam guardados, mas o site e o fechamento do pedido ainda não usam: por enquanto o cliente não vê nem é cobrado por eles.')
  for (const rotulo of ['Subtítulo', 'Cor principal', 'Pedido mínimo para entrega', 'Frete grátis acima de']) {
    assert.equal(campo(raiz, rotulo).props['aria-describedby'], aviso.props.id, `${rotulo} aponta para o aviso`)
  }
  assert.doesNotMatch(todoTexto(raiz), /sem mínimo|no topo do site|cor padrão do site/, 'nenhuma dica promete efeito que o site ainda não tem')
  mudar(raiz, 'Subtítulo', 'Massas frescas')
  mudar(raiz, 'Pedido mínimo para entrega', 4000)
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  await enviar(raiz, formularios(raiz).find((f) => texto(f).includes('Salvar o site')))
  assert.deepEqual(chamadasDe('PUT', '/api/minha-vitrine/configuracao').map((c) => c.corpo), [{
    subtituloPublico: 'Massas frescas', logoUrl: '/uploads/antigo.png', corPrimaria: '#8b1e3f', whatsappPedidos: '(11) 99999-0000',
    mensagemForaArea: 'Ainda não chegamos aí', pedidoMinimoEntrega: 40,
  }])
  assert.equal(chamadasDe('POST', /ativar|desativar/).length, 0)
  assert.equal(aoSalvo.chamadas.length, 1)
  assert.equal(aoMudarPendencia.chamadas.at(-1), false)
})

await provar('B2 site: um envio de logo grava o da loja e depois o do site, sem levar a edição ainda não salva', async () => {
  const { AbaSiteEImpressos } = await carregar('features/gestao/vitrine/AbaSiteEImpressos.jsx')
  responder('GET', '/api/minha-vitrine', { dados: { storefrontId: 's1', slug: 'casa-da-baba', tituloPublico: 'Casa da Baba', ativo: true } })
  responder('GET', '/api/minha-vitrine/configuracao', { dados: CONFIG_SITE })
  responder('GET', LOJAS, { dados: [LOJA] })
  responder('POST', '/api/uploads/loja/logo?lojaId=l1', { dados: { url: '/uploads/lojas/emp-1/l1/logo/novo.png' } })
  responder('PUT', PUT_DO_SITE, ({ corpo }) => ({ dados: putDoSite(CONFIG_SITE, corpo) }))
  const { raiz, aoMudarPendencia } = await montar(AbaSiteEImpressos)
  mudar(raiz, 'Subtítulo', 'Ainda pensando')
  const arquivo = andar(raiz.arvore).find((n) => n.type === 'input' && n.props?.type === 'file')
  assert.ok(arquivo, 'há o campo de arquivo do logo')
  const evento = { target: { files: [new File([new Uint8Array(10)], 'logo.png', { type: 'image/png' })], value: 'C:\\logo.png' } }
  await arquivo.props.onChange(evento)
  await raiz.atualizar()
  assert.equal(evento.target.value, '', 'o mesmo arquivo pode ser escolhido de novo')
  const upload = chamadasDe('POST', '/api/uploads/loja/logo?lojaId=l1')
  assert.equal(upload.length, 1)
  assert.ok(upload[0].corpo instanceof FormData)
  assert.deepEqual(chamadasDe('PUT', '/api/minha-vitrine/configuracao').map((c) => c.corpo), [{
    subtituloPublico: 'Massas', logoUrl: '/uploads/lojas/emp-1/l1/logo/novo.png', corPrimaria: '#8b1e3f', whatsappPedidos: '(11) 99999-0000', mensagemForaArea: 'Ainda não chegamos aí',
  }])
  assert.equal(campo(raiz, 'Subtítulo').props.value, 'Ainda pensando', 'a edição continua na tela')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  assert.match(texto(raiz.arvore), /Logo salvo/)
  assert.equal(previaDoLogo(raiz).props.src, '/uploads/lojas/emp-1/l1/logo/novo.png', 'a prévia é o logo novo')
  assert.doesNotMatch(texto(raiz.arvore), /O site ainda não usa este logo/, 'loja e site ficaram iguais')

  const gif = { target: { files: [new File([new Uint8Array(10)], 'logo.gif', { type: 'image/gif' })], value: 'x' } }
  await andar(raiz.arvore).find((n) => n.type === 'input' && n.props?.type === 'file').props.onChange(gif)
  await raiz.atualizar()
  assert.match(texto(raiz.arvore), /Escolha uma imagem PNG ou JPG\./)
  assert.equal(chamadasDe('POST', /uploads/).length, 1, 'arquivo recusado não sobe')
})

await provar('B2 site: logo e Salvar o site não correm juntos; o logo conta como pendência e avisa aoSalvo', async () => {
  const { AbaSiteEImpressos } = await carregar('features/gestao/vitrine/AbaSiteEImpressos.jsx')
  let gravado = { ...CONFIG_SITE }
  const espera = {}
  const segurar = (nome, resposta) => new Promise((resolver) => { espera[nome] = () => resolver(resposta()) })
  responder('GET', '/api/minha-vitrine', { dados: { storefrontId: 's1', slug: 'casa-da-baba', tituloPublico: 'Casa da Baba', ativo: true } })
  responder('GET', '/api/minha-vitrine/configuracao', () => ({ dados: gravado }))
  responder('GET', LOJAS, { dados: [LOJA] })
  responder('POST', '/api/uploads/loja/logo?lojaId=l1', () => segurar('upload', () => ({ dados: { url: '/uploads/lojas/emp-1/l1/logo/novo.png' } })))
  responder('PUT', PUT_DO_SITE, ({ corpo }) => { gravado = putDoSite(gravado, corpo); return { dados: gravado } })
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(AbaSiteEImpressos)
  const formularioDoSite = () => formularios(raiz).find((f) => texto(f).includes('Salvar o site'))
  const arquivo = () => andar(raiz.arvore).find((n) => n.type === 'input' && n.props?.type === 'file')

  const escolha = arquivo().props.onChange({ target: { files: [new File([new Uint8Array(10)], 'logo.png', { type: 'image/png' })], value: 'x' } })
  await raiz.atualizar()
  assert.deepEqual(aoMudarPendencia.chamadas, [true], 'o logo subindo segura "Conferi, seguir"')
  mudar(raiz, 'Subtítulo', 'Massas frescas')
  assert.equal(botao(raiz, 'Salvar o site').props.disabled, true, 'Salvar espera o logo')
  await enviar(raiz, formularioDoSite())
  assert.equal(chamadasDe('PUT', '/api/minha-vitrine/configuracao').length, 0, 'nada do site vai enquanto o logo sobe')
  espera.upload()
  await escolha
  await raiz.atualizar()
  assert.deepEqual(chamadasDe('PUT', '/api/minha-vitrine/configuracao').map((c) => c.corpo), [{
    subtituloPublico: 'Massas', logoUrl: '/uploads/lojas/emp-1/l1/logo/novo.png', corPrimaria: '#8b1e3f', whatsappPedidos: '(11) 99999-0000', mensagemForaArea: 'Ainda não chegamos aí',
  }], 'o logo leva a marca gravada, não a edição aberta')
  assert.equal(aoSalvo.chamadas.length, 1, 'logo gravado avisa aoSalvo')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true, 'o subtítulo editado continua pendente')

  await enviar(raiz, formularioDoSite())
  assert.deepEqual(chamadasDe('PUT', '/api/minha-vitrine/configuracao').map((c) => c.corpo).at(-1), {
    subtituloPublico: 'Massas frescas', logoUrl: '/uploads/lojas/emp-1/l1/logo/novo.png', corPrimaria: '#8b1e3f', whatsappPedidos: '(11) 99999-0000', mensagemForaArea: 'Ainda não chegamos aí',
  }, 'o Salvar seguinte parte do logo novo')
  assert.equal(aoSalvo.chamadas.length, 2)
  assert.equal(aoMudarPendencia.chamadas.at(-1), false)

  rede.rotas = rede.rotas.filter((r) => r.metodo !== 'PUT')
  responder('PUT', PUT_DO_SITE, ({ corpo }) => segurar('salvar', () => { gravado = putDoSite(gravado, corpo); return { dados: gravado } }))
  mudar(raiz, 'Cor principal', '#000000')
  const salvando = formularioDoSite().props.onSubmit({ preventDefault() {} })
  await raiz.atualizar()
  assert.equal(botao(raiz, 'Trocar logo').props.disabled, true, 'o logo espera o Salvar')
  espera.salvar()
  await salvando
  await raiz.atualizar()
  assert.equal(botao(raiz, 'Trocar logo').props.disabled, false)
})

await provar('B2 site: sair da tela com a edição aberta avisa que a pendência acabou', async () => {
  const { AbaSiteEImpressos } = await carregar('features/gestao/vitrine/AbaSiteEImpressos.jsx')
  responder('GET', '/api/minha-vitrine', VITRINE)
  responder('GET', '/api/minha-vitrine/configuracao', { dados: CONFIG_SITE })
  responder('GET', LOJAS, { dados: [LOJA] })
  const { raiz, aoMudarPendencia } = await montar(AbaSiteEImpressos)
  mudar(raiz, 'Subtítulo', 'Massas frescas')
  assert.deepEqual(aoMudarPendencia.chamadas, [true])
  raiz.desmontar()
  assert.deepEqual(aoMudarPendencia.chamadas, [true, false])
  assert.equal(escritas().length, 0)
})

await provar('B2 site: apagar o único WhatsApp ou voltar a única cor para a padrão grava de verdade', async () => {
  const { AbaSiteEImpressos } = await carregar('features/gestao/vitrine/AbaSiteEImpressos.jsx')
  const semMarca = { ...CONFIG_SITE, subtituloPublico: null, logoUrl: null, corPrimaria: null, whatsappPedidos: null, mensagemForaArea: null }
  let gravado = { ...semMarca, whatsappPedidos: '(11) 99999-0000' }
  responder('GET', '/api/minha-vitrine', VITRINE)
  responder('GET', '/api/minha-vitrine/configuracao', () => ({ dados: gravado }))
  responder('GET', LOJAS, { dados: [LOJA] })
  responder('PUT', PUT_DO_SITE, ({ corpo }) => { gravado = putDoSite(gravado, corpo); return { dados: gravado } })
  const doSite = (raiz) => formularios(raiz).find((f) => texto(f).includes('Salvar o site'))
  const whats = await montar(AbaSiteEImpressos)
  mudar(whats.raiz, 'WhatsApp de pedidos', '')
  await enviar(whats.raiz, doSite(whats.raiz))
  assert.equal(gravado.whatsappPedidos, '', 'o número saiu da vitrine e dos impressos')
  assert.equal(campo(whats.raiz, 'WhatsApp de pedidos').props.value, '', 'o campo não volta preenchido')
  assert.equal(whats.aoSalvo.chamadas.length, 1)
  whats.raiz.desmontar()

  gravado = { ...semMarca, corPrimaria: '#8b1e3f' }
  const cor = await montar(AbaSiteEImpressos)
  mudar(cor.raiz, 'Cor principal', '')
  await enviar(cor.raiz, doSite(cor.raiz))
  assert.equal(gravado.corPrimaria, null, 'a cor voltou para a padrão')
  assert.equal(campo(cor.raiz, 'Cor principal').props.value, '')
  assert.equal(cor.aoMudarPendencia.chamadas.at(-1), false)
})

await provar('B2 site: site sem nome começa a frase com maiúscula e não chama o código de endereço', async () => {
  const { AbaSiteEImpressos } = await carregar('features/gestao/vitrine/AbaSiteEImpressos.jsx')
  responder('GET', '/api/minha-vitrine', VITRINE)
  responder('GET', '/api/minha-vitrine/configuracao', { dados: { ...CONFIG_SITE, tituloPublico: null } })
  responder('GET', LOJAS, { dados: [LOJA] })
  const { raiz } = await montar(AbaSiteEImpressos)
  const identificacao = andar(raiz.arvore).find((n) => n.type === 'p' && /casa-da-baba/.test(texto(n)))
  assert.equal(texto(identificacao), 'Site sem nome, código casa-da-baba. O nome e o código ficam como foram criados.')
  assert.doesNotMatch(todoTexto(raiz), /endereço/i)
})

const LOGO_A = '/uploads/lojas/emp-1/l1/logo/a.png'
const LOGO_B = '/uploads/lojas/emp-1/l1/logo/b.png'

await provar('B2 site: depois de trocar o logo pela Impressão, a prévia é o da loja e o site pode passar a usá-lo', async () => {
  const { AbaSiteEImpressos } = await carregar('features/gestao/vitrine/AbaSiteEImpressos.jsx')
  // A Impressão gravou o B só na loja; o site ainda aponta para o A, que o upload apagou.
  let gravado = { ...CONFIG_SITE, logoUrl: LOGO_A }
  responder('GET', '/api/minha-vitrine', VITRINE)
  responder('GET', '/api/minha-vitrine/configuracao', () => ({ dados: gravado }))
  responder('GET', LOJAS, { dados: [{ ...LOJA, logoUrl: LOGO_B }] })
  responder('PUT', PUT_DO_SITE, ({ corpo }) => { gravado = putDoSite(gravado, corpo); return { dados: gravado } })
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(AbaSiteEImpressos)
  assert.equal(previaDoLogo(raiz).props.src, LOGO_B, 'a prévia é o logo que sai nos impressos')
  assert.match(texto(raiz.arvore), /O site ainda não usa este logo\./)
  mudar(raiz, 'Subtítulo', 'Ainda pensando')
  await clicar(raiz, 'Usar este logo no site')
  assert.deepEqual(chamadasDe('PUT', PUT_DO_SITE).map((c) => c.corpo), [{
    subtituloPublico: 'Massas', logoUrl: LOGO_B, corPrimaria: '#8b1e3f', whatsappPedidos: '(11) 99999-0000', mensagemForaArea: 'Ainda não chegamos aí',
  }], 'só o logo muda; a edição aberta não vai junto')
  assert.equal(chamadasDe('POST', /uploads/).length, 0, 'nenhum arquivo sobe de novo')
  assert.equal(gravado.logoUrl, LOGO_B)
  assert.doesNotMatch(texto(raiz.arvore), /O site ainda não usa este logo/)
  assert.equal(botao(raiz, 'Usar este logo no site'), undefined)
  assert.match(texto(raiz.arvore), /O site agora usa o mesmo logo dos impressos\./)
  assert.equal(aoSalvo.chamadas.length, 1)
  assert.equal(campo(raiz, 'Subtítulo').props.value, 'Ainda pensando')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
})

await provar('B2 site: loja com logo e site sem logo mostra o da loja; sem logo nenhum pede o envio; iguais não avisam', async () => {
  const { AbaSiteEImpressos } = await carregar('features/gestao/vitrine/AbaSiteEImpressos.jsx')
  const cenario = async (logoDaLoja, logoDoSite) => {
    rede.rotas = []; rede.chamadas = []
    responder('GET', '/api/minha-vitrine', VITRINE)
    responder('GET', '/api/minha-vitrine/configuracao', { dados: { ...CONFIG_SITE, logoUrl: logoDoSite } })
    responder('GET', LOJAS, { dados: [{ ...LOJA, logoUrl: logoDaLoja }] })
    return (await montar(AbaSiteEImpressos)).raiz
  }
  const soNaLoja = await cenario(LOGO_B, null)
  assert.equal(previaDoLogo(soNaLoja).props.src, LOGO_B, 'os impressos já saem com o logo da loja')
  assert.doesNotMatch(texto(soNaLoja.arvore), /O logo aparece aqui depois do envio/)
  assert.match(texto(soNaLoja.arvore), /O site ainda não usa este logo\./)
  assert.ok(botao(soNaLoja, 'Usar este logo no site'))
  soNaLoja.desmontar()

  const nenhum = await cenario(null, null)
  assert.equal(previaDoLogo(nenhum), undefined)
  assert.match(texto(nenhum.arvore), /O logo aparece aqui depois do envio\./)
  assert.equal(botao(nenhum, 'Usar este logo no site'), undefined)
  nenhum.desmontar()

  const iguais = await cenario(LOGO_B, LOGO_B)
  assert.equal(previaDoLogo(iguais).props.src, LOGO_B)
  assert.doesNotMatch(texto(iguais.arvore), /O site ainda não usa este logo/)
  assert.equal(botao(iguais, 'Usar este logo no site'), undefined)
  assert.equal(escritas().length, 0, 'ler não grava nada')
})

await provar('B2 site: com duas lojas ativas a tela diz para qual vai o logo', async () => {
  const { AbaSiteEImpressos } = await carregar('features/gestao/vitrine/AbaSiteEImpressos.jsx')
  responder('GET', '/api/minha-vitrine', VITRINE)
  responder('GET', '/api/minha-vitrine/configuracao', { dados: CONFIG_SITE })
  responder('GET', LOJAS, { dados: [LOJA, { ...LOJA, id: 'l2', nome: 'Quiosque' }] })
  const { raiz } = await montar(AbaSiteEImpressos)
  assert.match(texto(raiz.arvore), /Esta empresa tem 2 lojas ativas: o logo vai para Casa da Baba\./)
  rede.rotas = []
  responder('GET', '/api/minha-vitrine', VITRINE)
  responder('GET', '/api/minha-vitrine/configuracao', { dados: CONFIG_SITE })
  responder('GET', LOJAS, { dados: [LOJA] })
  const uma = await montar(AbaSiteEImpressos)
  assert.doesNotMatch(texto(uma.raiz.arvore), /lojas ativas/)
})

await provar('B3 alertas de estoque: a loja da empresa, sem moeda, fuso nem KDS, e PATCH só do que mudou', async () => {
  const { AbaAlertasDeEstoque } = await carregar('features/gestao/estoque/AbaAlertasDeEstoque.jsx')
  const config = { lojaId: 'l1', diasAlertaValidade: 7, diasAlertaParado: 30, quantidadeMinimaPadrao: 10, quantidadeCriticaPadrao: 3, notificarEstoqueCritico: true, notificarValidade: true, notificarParado: true, notificarReposicao: true, fifoAtivo: true, moeda: 'BRL', timezone: 'America/Sao_Paulo', kdsHabilitado: true }
  responder('GET', LOJAS, { dados: [LOJA] })
  responder('GET', '/api/configuracoes?empresaId=emp-1&lojaId=l1', { dados: config })
  responder('PATCH', '/api/configuracoes', ({ corpo }) => ({ dados: { ...config, ...corpo } }))
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(AbaAlertasDeEstoque)
  assert.equal(texto(andar(raiz.arvore).find((n) => n.type === 'h3')), 'Alertas de estoque · Casa da Baba', 'a tela diz de qual loja são os alertas')
  assert.doesNotMatch(texto(raiz.arvore), /lojas ativas/, 'com uma loja só não há aviso')
  assert.deepEqual(rotulos(raiz), ['Validade próxima (dias)', 'Produto parado (dias sem saída)', 'Quantidade mínima padrão', 'Quantidade crítica padrão', 'Ordem de saída do estoque'])
  assert.doesNotMatch(todoTexto(raiz), /moeda|fuso|KDS|conta a receber|conta a pagar|restaurar|padrão de fábrica/i)
  mudar(raiz, 'Quantidade crítica padrão', '10')
  await enviar(raiz)
  assert.equal(escritas().length, 0, 'crítico igual ao mínimo não vai')
  assert.match(texto(raiz.arvore), /O nível crítico deve ser menor que o mínimo\./)
  mudar(raiz, 'Quantidade crítica padrão', '3')
  mudar(raiz, 'Validade próxima (dias)', '15')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  await enviar(raiz)
  assert.deepEqual(chamadasDe('PATCH', '/api/configuracoes').map((c) => c.corpo), [{ empresaId: 'emp-1', lojaId: 'l1', diasAlertaValidade: 15 }])
  assert.equal(aoSalvo.chamadas.length, 1)
  assert.equal(aoMudarPendencia.chamadas.at(-1), false)
  assert.equal(campo(raiz, 'Validade próxima (dias)').props.value, '15')
})

await provar('B3 alertas de estoque: com duas lojas ativas a tela avisa que valem só para uma', async () => {
  const { AbaAlertasDeEstoque } = await carregar('features/gestao/estoque/AbaAlertasDeEstoque.jsx')
  responder('GET', LOJAS, { dados: [LOJA, { ...LOJA, id: 'l2', nome: 'Quiosque' }, { ...LOJA, id: 'l3', nome: 'Fechada', ativa: false }] })
  responder('GET', '/api/configuracoes?empresaId=emp-1&lojaId=l1', { dados: { lojaId: 'l1', diasAlertaValidade: 7, diasAlertaParado: 30, quantidadeMinimaPadrao: 10, quantidadeCriticaPadrao: 3, fifoAtivo: true } })
  const { raiz } = await montar(AbaAlertasDeEstoque, { compacto: true })
  assert.equal(texto(andar(raiz.arvore).find((n) => n.type === 'h3')), 'Alertas · Casa da Baba')
  assert.match(texto(raiz.arvore), /Esta empresa tem 2 lojas ativas: estes alertas valem só para Casa da Baba\./)
})

await provar('B4 caderno: explica o uso, cria, edita e remove trechos pelas rotas do caderno', async () => {
  const { AbaCadernoDoAgente } = await carregar('features/gestao/caderno/AbaCadernoDoAgente.jsx')
  let trechos = [
    { id: 't1', codigo: 't1', titulo: 'Entrega', texto: 'Entregamos até 5 km.', palavrasChave: 'entrega, frete', nucleo: true, arquivado: false, alteradoEm: '2026-10-20T10:00:00Z' },
  ]
  responder('GET', '/api/atendimento/caderno', () => ({ dados: trechos }))
  responder('POST', '/api/atendimento/caderno', ({ corpo }) => { trechos = [...trechos, { id: 't2', codigo: 't2', ...corpo, arquivado: false }]; return { dados: trechos.at(-1) } })
  responder('PUT', '/api/atendimento/caderno/t1', ({ corpo }) => { trechos = trechos.map((t) => (t.id === 't1' ? { ...t, ...corpo } : t)); return { dados: trechos[0] } })
  responder('POST', '/api/atendimento/caderno/t2/arquivar?arquivado=true', () => { trechos = trechos.filter((t) => t.id !== 't2'); return { dados: {} } })
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(AbaCadernoDoAgente)
  assert.match(texto(raiz.arvore), /agente/i)
  assert.match(texto(raiz.arvore), /núcleo/i)
  assert.match(texto(raiz.arvore), /Entregamos até 5 km\./)
  const focado = () => [g.foco?.type, g.foco?.props?.['aria-label'] ?? texto(g.foco).trim()]
  await clicar(raiz, 'Novo trecho')
  assert.deepEqual(focado(), ['h3', 'Novo trecho'], 'o foco vai ao título do formulário')
  mudar(raiz, 'Título', 'Alergias')
  mudar(raiz, 'Texto', 'Não temos opção sem glúten.')
  mudar(raiz, 'Palavras-chave', 'Glúten; alergia')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  await enviar(raiz)
  assert.deepEqual(chamadasDe('POST', '/api/atendimento/caderno').map((c) => c.corpo), [{ titulo: 'Alergias', texto: 'Não temos opção sem glúten.', palavrasChave: 'glúten, alergia', nucleo: false }])
  assert.equal(aoSalvo.chamadas.length, 1)
  assert.equal(aoMudarPendencia.chamadas.at(-1), false)
  assert.match(texto(raiz.arvore), /Alergias/)
  assert.deepEqual(focado(), ['Botao', 'Novo trecho'], 'salvo, o foco volta ao botão de origem')

  await clicar(raiz, 'Editar: Entrega')
  assert.deepEqual(focado(), ['h3', 'Editar “Entrega”'])
  assert.equal(campo(raiz, 'Texto').props.value, 'Entregamos até 5 km.')
  mudar(raiz, 'Texto', 'Entregamos até 7 km.')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  assert.match(texto(raiz.arvore), /Alterações não salvas\. Salve ou cancele antes de sair\./)
  assert.doesNotMatch(texto(raiz.arvore), /desfaça/, 'a edição do trecho tem Cancelar, não Desfazer')
  await enviar(raiz)
  assert.deepEqual(chamadasDe('PUT', '/api/atendimento/caderno/t1').map((c) => c.corpo), [{ titulo: 'Entrega', texto: 'Entregamos até 7 km.', palavrasChave: 'entrega, frete', nucleo: true }])
  assert.equal(aoSalvo.chamadas.length, 2)
  assert.deepEqual(focado(), ['Botao', 'Editar: Entrega'])

  await clicar(raiz, 'Remover: Alergias')
  assert.equal(chamadasDe('POST', /arquivar/).length, 0, 'remover pede confirmação antes')
  assert.deepEqual(focado(), ['Botao', 'Remover do caderno'], 'o foco vai à confirmação')
  assert.equal(andar(raiz.arvore).some((n) => n.props?.role === 'alertdialog'), false, 'não promete um diálogo modal')
  const grupo = andar(raiz.arvore).find((n) => n.type === 'fieldset' && texto(n).includes('do caderno?'))
  assert.ok(grupo, 'a confirmação é um grupo (fieldset)')
  assert.equal(texto(andar(grupo).find((n) => n.type === 'legend')), 'Remover Alergias', 'com nome para o leitor de tela')
  await clicar(raiz, 'Manter')
  assert.deepEqual(focado(), ['Botao', 'Remover: Alergias'], 'manter devolve o foco ao botão de origem')
  await clicar(raiz, 'Remover: Alergias')
  await clicar(raiz, 'Remover do caderno')
  assert.equal(chamadasDe('POST', '/api/atendimento/caderno/t2/arquivar?arquivado=true').length, 1)
  assert.doesNotMatch(texto(raiz.arvore), /Alergias/)
  assert.deepEqual(focado(), ['h3', 'Caderno do agente'], 'o trecho saiu: o foco vai ao título do caderno')
})

await provar('B4 caderno: cancelar devolve o foco, remoção que falha fala da remoção e o compacto encurta a explicação', async () => {
  const { AbaCadernoDoAgente } = await carregar('features/gestao/caderno/AbaCadernoDoAgente.jsx')
  responder('GET', '/api/atendimento/caderno', { dados: [{ id: 't1', codigo: 't1', titulo: 'Entrega', texto: 'Até 5 km.', palavrasChave: '', nucleo: false, arquivado: false }] })
  responder('POST', '/api/atendimento/caderno/t1/arquivar?arquivado=true', { status: 403 })
  const { raiz, aoSalvo } = await montar(AbaCadernoDoAgente, { compacto: true })
  assert.match(texto(raiz.arvore), /O agente do atendimento consulta este caderno para responder os clientes\./)
  assert.doesNotMatch(texto(raiz.arvore), /índice/, 'no compacto fica só a explicação curta')
  const completo = await montar(AbaCadernoDoAgente)
  assert.match(texto(completo.raiz.arvore), /Os outros ficam num índice/)
  completo.raiz.desmontar()
  await raiz.atualizar()

  await clicar(raiz, 'Editar: Entrega')
  await clicar(raiz, 'Cancelar')
  assert.deepEqual([g.foco?.type, g.foco?.props?.['aria-label']], ['Botao', 'Editar: Entrega'], 'cancelar devolve o foco ao botão de origem')

  await clicar(raiz, 'Remover: Entrega')
  await clicar(raiz, 'Remover do caderno')
  assert.match(texto(raiz.arvore), /Não foi possível remover o trecho\. Seu usuário não tem permissão para isso\./)
  assert.doesNotMatch(texto(raiz.arvore), /Sua edição continua na tela|Não foi possível salvar/, 'não houve edição, houve remoção')
  assert.equal(aoSalvo.chamadas.length, 0)
})

await provar('B4 caderno: enquanto remove, a tela diz "Removendo…", não "Salvando…"', async () => {
  const { AbaCadernoDoAgente } = await carregar('features/gestao/caderno/AbaCadernoDoAgente.jsx')
  responder('GET', '/api/atendimento/caderno', { dados: [{ id: 't1', codigo: 't1', titulo: 'Entrega', texto: 'Até 5 km.', palavrasChave: '', nucleo: false, arquivado: false }] })
  let soltar
  responder('POST', '/api/atendimento/caderno/t1/arquivar?arquivado=true', () => new Promise((resolver) => { soltar = () => resolver({ dados: {} }) }))
  const { raiz } = await montar(AbaCadernoDoAgente)
  await clicar(raiz, 'Remover: Entrega')
  const remocao = botao(raiz, 'Remover do caderno').props.onClick()
  await raiz.atualizar()
  assert.match(texto(raiz.arvore), /Removendo…/)
  assert.doesNotMatch(texto(raiz.arvore), /Salvando…/)
  soltar()
  await remocao
  await raiz.atualizar()
  assert.match(texto(raiz.arvore), /Trecho removido do caderno\./)
})

await provar('B4 caderno: núcleo acima do teto não envia e o erro da API aparece', async () => {
  const { AbaCadernoDoAgente } = await carregar('features/gestao/caderno/AbaCadernoDoAgente.jsx')
  responder('GET', '/api/atendimento/caderno', { dados: [{ id: 't1', codigo: 't1', titulo: 'Grande', texto: 'x'.repeat(5800), palavrasChave: '', nucleo: true, arquivado: false }] })
  responder('POST', '/api/atendimento/caderno', { status: 400, erro: { code: 'VALIDACAO', message: 'Requisição inválida', detail: 'Texto acima de 4000 caracteres.' } })
  const { raiz } = await montar(AbaCadernoDoAgente)
  await clicar(raiz, 'Novo trecho')
  mudar(raiz, 'Título', 'Mais')
  mudar(raiz, 'Texto', 'y'.repeat(300))
  marcar(raiz, 'Vai em toda resposta (núcleo)', true)
  await enviar(raiz)
  assert.equal(escritas().length, 0)
  assert.match(texto(raiz.arvore), /O núcleo passaria de 6\.000 caracteres/)
  marcar(raiz, 'Vai em toda resposta (núcleo)', false)
  await enviar(raiz)
  assert.equal(escritas().length, 1)
  assert.match(texto(raiz.arvore), /Texto acima de 4000 caracteres\./)
})

await provar('B5 avisos: só leitura do que chega e por onde; ligar neste aparelho usa a ativação que já existe', async () => {
  const { AbaAvisos } = await carregar('features/gestao/avisos/AbaAvisos.jsx')
  const { AVISOS_DA_DONA } = await carregar('dominio/avisosDaDona.js')
  let ativou = 0
  const externo = await montar(AbaAvisos, { aparelho: { estado: 'desligado', mensagem: null, ativar: () => { ativou++ }, desligar() {} } })
  for (const aviso of AVISOS_DA_DONA) assert.match(texto(externo.raiz.arvore), new RegExp(aviso.titulo))
  assert.equal(rotulos(externo.raiz).length, 0, 'nenhum campo para editar')
  await clicar(externo.raiz, 'Ativar avisos neste aparelho')
  assert.equal(ativou, 1)
  assert.equal(externo.aoMudarPendencia.chamadas.includes(true), false)


  // A ativação do App (a mesma do sino) chega pelo contexto de acesso: uma instância só.
  let desligou = 0
  g.acesso = { acoes: {}, aparelho: { estado: 'ativo', mensagem: null, ativar() {}, desligar: () => { desligou++ } } }
  const doApp = await montar(AbaAvisos)
  assert.match(texto(doApp.raiz.arvore), /Avisos desta conta ativados neste aparelho\./)
  await clicar(doApp.raiz, 'Desativar avisos neste aparelho')
  assert.equal(desligou, 1)

  // Sem a ativação do App (hoje o App não põe `aparelho` no contexto de acesso: depende do pacote de
  // registro), a aba não cria outra e não manda tocar num sino que pode estar atrás desta tela.
  const tocou = []
  g.push = new Proxy({}, { get: (_, nome) => () => { tocou.push(nome); throw new Error(`push.${String(nome)} não devia ser chamado`) } })
  g.acesso = { acoes: {} }
  const solta = await montar(AbaAvisos)
  assert.equal(botao(solta.raiz, 'Ativar avisos neste aparelho'), undefined)
  assert.match(texto(solta.raiz.arvore), /Ligar os avisos neste aparelho não está disponível nesta tela\. Ao sair dela, use o sino de Avisos no topo do Console\./)
  assert.doesNotMatch(texto(solta.raiz.arvore), /toque no sino/, 'não aponta para um sino que talvez não esteja à vista')
  assert.deepEqual(tocou, [])
  assert.deepEqual(rede.chamadas, [], 'nem conferir nem inscrever de novo')
  for (const aviso of AVISOS_DA_DONA) assert.match(texto(solta.raiz.arvore), new RegExp(aviso.titulo))
  assert.doesNotMatch(fonte('aplicacao/useAvisosNoAparelho.js'), /useAvisosNoAparelhoDaSessao/, 'a segunda instância não existe mais')
})

await provar('B6 entregadores: o cadastro extraído avisa pendência e chama a mesma ação de antes', async () => {
  const { CadastroEntregadores } = await carregar('features/entregas/CadastroEntregadores.jsx')
  const criados = []
  const acoes = { criarEntregador: async (corpo) => { criados.push(corpo); return true }, desativarEntregador: async () => true }
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(CadastroEntregadores, { entregadores: [{ id: 'e1', nome: 'João', veiculo: 'Moto', placa: 'ABC1D23', telefone: '' }], ocupado: false, acoes })
  assert.deepEqual(rotulos(raiz), ['Nome', 'Tipo', 'Empresa', 'Telefone', 'Veículo', 'Placa'])
  assert.match(texto(raiz.arvore), /João/)
  mudar(raiz, 'Nome', '  Maria  ')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  await enviar(raiz)
  assert.deepEqual(criados, [{ nome: 'Maria', tipo: 'Motoboy', empresa: 'Propria', telefone: '', veiculo: '', placa: '' }])
  assert.equal(aoSalvo.chamadas.length, 1)
  assert.equal(aoMudarPendencia.chamadas.at(-1), false)
  assert.match(texto(raiz.arvore), /Entregador cadastrado/)
  await clicar(raiz, 'Desativar')
  assert.equal(aoSalvo.chamadas.length, 2, 'desativar grava na API: quem montou fica sabendo')
  acoes.desativarEntregador = async () => false
  await clicar(raiz, 'Desativar')
  assert.equal(aoSalvo.chamadas.length, 2, 'desativação recusada não conta como salvo')
  const conteudo = fonte('features/entregas/ConteudoEntregasApi.jsx')
  assert.match(conteudo, /import \{ CadastroEntregadores \} from '\.\/CadastroEntregadores'/)
  assert.match(conteudo, /\{aba === 'entregadores' && <CadastroEntregadores entregadores=\{entregadores\} ocupado=\{ocupado\} acoes=\{acoes\} \/>\}/)
  assert.doesNotMatch(conteudo, /function Entregadores\(/, 'o cadastro mora num lugar só')
})

await provar('B6 entregadores: a versão autônoma lê e cadastra pela rota de entregadores', async () => {
  const { CadastroEntregadoresApi } = await carregar('features/entregas/CadastroEntregadores.jsx')
  let lista = []
  responder('GET', '/api/atendimento/entregadores', () => ({ dados: lista }))
  responder('POST', '/api/atendimento/entregadores', ({ corpo }) => { lista = [{ id: 'e9', ...corpo }]; return { dados: lista[0] } })
  const { raiz, aoSalvo } = await montar(CadastroEntregadoresApi)
  assert.match(texto(raiz.arvore), /Nenhum entregador cadastrado/)
  mudar(raiz, 'Nome', 'Carla')
  await enviar(raiz)
  assert.deepEqual(chamadasDe('POST', '/api/atendimento/entregadores').map((c) => c.corpo), [{ nome: 'Carla', tipo: 'Motoboy', empresa: 'Propria', telefone: '', veiculo: '', placa: '' }])
  assert.equal(chamadasDe('GET', '/api/atendimento/entregadores').length, 2, 'relê depois de cadastrar')
  assert.match(texto(raiz.arvore), /Carla/)
  assert.equal(aoSalvo.chamadas.length, 1)

  rede.rotas = []
  responder('POST', '/api/atendimento/entregadores', { status: 403 })
  mudar(raiz, 'Nome', 'Bia')
  await enviar(raiz)
  const alerta = andar(raiz.arvore).find((n) => n.props?.role === 'alert')
  assert.equal(texto(alerta).trim(), 'O entregador não foi cadastrado: Seu usuário não tem permissão para isso.')
  assert.equal(botao(raiz, 'Tentar de novo'), undefined, 'erro de cadastro não oferece reler a lista')
  assert.equal(campo(raiz, 'Nome').props.value, 'Bia', 'o formulário fica para enviar de novo')
  assert.equal(botao(raiz, 'Cadastrar').props.disabled, false)
})

await provar('B6 entregadores: a lista que não carregou oferece tentar de novo e relê', async () => {
  const { CadastroEntregadoresApi } = await carregar('features/entregas/CadastroEntregadores.jsx')
  let falhar = true
  responder('GET', '/api/atendimento/entregadores', () => (falhar ? { status: 500, erro: { code: 'ERRO', message: 'Falha no servidor.' } } : { dados: [{ id: 'e1', nome: 'João' }] }))
  const { raiz } = await montar(CadastroEntregadoresApi)
  assert.match(texto(raiz.arvore), /Os entregadores não carregaram:/)
  falhar = false
  await clicar(raiz, 'Tentar de novo')
  assert.equal(chamadasDe('GET', '/api/atendimento/entregadores').length, 2)
  assert.match(texto(raiz.arvore), /João/)
  assert.equal(botao(raiz, 'Tentar de novo'), undefined)
})

await provar('B7 horários: o formulário extraído grava a semana, avisa pendência e o modal segue como casca', async () => {
  const { FormularioHorariosDaSemana } = await carregar('features/automacoes/FormularioHorariosDaSemana.jsx')
  const { ModalHorariosDaSemana } = await carregar('features/automacoes/ModalHorariosDaSemana.jsx')
  const { criarAcoesExpedienteApi } = await carregar('aplicacao/api/expediente.js')
  const semana = { horarios: [1, 2, 3, 4, 5, 6].map((d) => ({ diaDaSemana: d, abre: '08:00:00', fecha: '18:00:00' })), controleManual: 'Automatico', mensagemForaDoHorario: '', mensagemLojaFechada: '' }
  responder('GET', '/api/atendimento/expediente', { dados: semana })
  responder('PUT', '/api/atendimento/expediente', ({ corpo }) => ({ dados: { ...semana, horarios: corpo.horarios.map((h) => ({ ...h, abre: `${h.abre}:00`, fecha: `${h.fecha}:00` })) } }))
  g.contexto = { acoes: criarAcoesExpedienteApi({ despachar() {}, estadoRef: { current: {} } }), estado: { agora: Date.parse('2026-10-26T12:00:00-03:00'), lojaAberta: null } }
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(FormularioHorariosDaSemana)
  assert.equal(andar(raiz.arvore).some((n) => n.type === 'Modal'), false, 'solto, sem o modal')
  assert.ok(botao(raiz, 'Salvar horários'), 'o próprio botão Salvar')
  mudar(raiz, 'Abre, Segunda', '09:00')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  await enviar(raiz)
  const put = chamadasDe('PUT', '/api/atendimento/expediente')
  assert.equal(put.length, 1)
  assert.deepEqual(put[0].corpo.horarios.find((h) => h.diaDaSemana === 1), { diaDaSemana: 1, abre: '09:00', fecha: '18:00' })
  assert.equal(put[0].corpo.mensagemForaDoHorario, undefined, 'a semana não mexe nas mensagens')
  assert.equal(aoSalvo.chamadas.length, 1)
  assert.equal(aoMudarPendencia.chamadas.at(-1), false)
  assert.match(texto(raiz.arvore), /Horários salvos no EasyStok/)

  let fechou = 0
  const modal = new Raiz({ type: ModalHorariosDaSemana, props: { aoFechar: () => { fechou++ } } })
  await modal.atualizar()
  const casca = andar(modal.arvore).find((n) => n.type === 'Modal')
  assert.equal(casca.props.titulo, 'Horários da semana')
  assert.ok(campo(modal, 'Abre, Segunda'), 'o corpo é o do formulário')
  await clicar(modal, 'Fechar')
  assert.equal(fechou, 1)
  assert.doesNotMatch(fonte('features/automacoes/ModalHorariosDaSemana.jsx'), /useState|useEffect|salvarHorariosExpediente/, 'o modal é só casca')
})

// Só o CSS: 360, 768 e 1280 px na tela real ficam para o laboratório.
await provar('C  CSS dos formulários: colunas que encolhem e nenhuma largura fixa acima de 328 px', async () => {
  const estilo = fonte('features/gestao/formularioDeAjuste.module.css')
  assert.match(estilo, /grid-template-columns: repeat\(auto-fit, minmax\(min\(100%, \d+px\), 1fr\)\)/)
  for (const [, largura] of estilo.matchAll(/(?:^|[^-])width:\s*(\d+)px/g)) assert.ok(Number(largura) <= 328, `largura fixa ${largura}px`)
  for (const caminho of ['features/gestao/loja/AbaDadosDaLoja.jsx', 'features/gestao/vitrine/AbaSiteEImpressos.jsx', 'features/gestao/estoque/AbaAlertasDeEstoque.jsx', 'features/gestao/caderno/AbaCadernoDoAgente.jsx', 'features/gestao/avisos/AbaAvisos.jsx']) {
    assert.match(fonte(caminho), /formularioDeAjuste\.module\.css/, caminho)
  }
})

console.log(`\n${passou} verificações passaram${falhas.length ? `, ${falhas.length} falharam` : ''}.`)
if (falhas.length) process.exit(1)
