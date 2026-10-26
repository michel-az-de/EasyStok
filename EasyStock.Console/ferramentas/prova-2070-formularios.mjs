// Prova da #2070, formulários novos do console: Dados da loja, Site e impressos, Alertas de
// estoque, Caderno do agente, Avisos, Entregadores (extraído) e Horários (extraído).
//
//   A  domínio puro: normalização e validação de cada formulário;
//   B  montagem real dos componentes com a API simulada no fetch (o `chamarApi` é o de verdade):
//      editar sem salvar chama aoMudarPendencia(true), salvar chama a rota certa com o corpo certo,
//      o erro da API aparece em português e o site não mostra os campos proibidos.
//
// Pixels, foco e 360 px continuam no navegador.
//
//   node ferramentas/prova-2070-formularios.mjs

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
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
g.push = {}
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
    if (caminho.endsWith('/aplicacao/acessoModulos.jsx')) return inline('export const useAcessoModulos=()=>({acoes:{}})')
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
  rede.chamadas = []; rede.rotas = []
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
    subtituloPublico: null, logoUrl: '/uploads/logo-antigo.png', corPrimaria: '#8b1e3f', whatsappPedidos: '11999990000',
    mensagemForaArea: 'Ainda não chegamos aí', pedidoMinimoEntrega: 25.5, freteGratisAcima: 150,
  })
  assert.equal(d.corpoDoSite(config, { ...form, corPrimaria: '' }).corPrimaria, null, 'cor apagada volta à cor padrão do site')
  assert.equal(d.corpoDoSite(config, { ...form, whatsappPedidos: '11987654321' }).whatsappPedidos, '(11) 98765-4321')
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
    subtituloPublico: null, logoUrl: '/uploads/novo.png', corPrimaria: '#8b1e3f', whatsappPedidos: '11999990000', mensagemForaArea: 'Ainda não chegamos aí',
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

await provar('A5 avisos: cada aviso diz por qual canal chega, sem canal inventado', async () => {
  const d = await carregar('dominio/avisosDaDona.js')
  const canais = new Set(Object.values(d.CANAIS_DE_AVISO))
  assert.ok(d.AVISOS_DA_DONA.length >= 8)
  assert.equal(new Set(d.AVISOS_DA_DONA.map((a) => a.titulo)).size, d.AVISOS_DA_DONA.length, 'títulos únicos')
  for (const aviso of d.AVISOS_DA_DONA) {
    assert.ok(aviso.canais.length > 0, aviso.titulo)
    for (const canal of aviso.canais) assert.ok(canais.has(canal), `${aviso.titulo}: ${canal}`)
  }
  const contato = d.AVISOS_DA_DONA.find((a) => a.titulo === 'Contato novo')
  assert.deepEqual(contato.canais, [d.CANAIS_DE_AVISO.TELA, d.CANAIS_DE_AVISO.APARELHO, d.CANAIS_DE_AVISO.SINO])
  assert.deepEqual(d.AVISOS_DA_DONA.find((a) => a.titulo === 'Mensagem nova de cliente').canais, [d.CANAIS_DE_AVISO.TELA])
})

// --- B. Montagem com a API simulada -------------------------------------------------------------------

const LOJA = { id: 'l1', empresaId: 'emp-1', nome: 'Casa da Baba', ativa: true, logoUrl: null, descricao: 'Massas', documento: null, endereco: 'Rua A, 10', telefone: '(11) 3456-7890' }
const LOJAS = '/api/lojas?empresaId=emp-1'

await provar('B1 dados da loja: um cartão por loja ativa; editar avisa pendência; salvar reenvia os 5 campos no PUT', async () => {
  const { AbaDadosDaLoja } = await carregar('features/gestao/loja/AbaDadosDaLoja.jsx')
  responder('GET', LOJAS, { dados: [LOJA, { ...LOJA, id: 'l2', nome: 'Fechada', ativa: false }] })
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
  responder('GET', LOJAS, { dados: [LOJA] })
  responder('PUT', '/api/lojas/l1', { status: 400, erro: { code: 'VALIDACAO', message: 'Requisição inválida', detail: 'Nome da loja é obrigatório.' } })
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(AbaDadosDaLoja)
  mudar(raiz, 'Endereço', 'Rua B, 20')
  await enviar(raiz)
  assert.match(texto(raiz.arvore), /Nome da loja é obrigatório\./)
  assert.equal(campo(raiz, 'Endereço').props.value, 'Rua B, 20')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  assert.equal(aoSalvo.chamadas.length, 0)

  rede.rotas = []; rede.chamadas = []
  responder('GET', LOJAS, { dados: [{ id: 'l1', empresaId: 'emp-1', nome: 'Casa da Baba', ativa: true, logoUrl: null }] })
  const curta = await montar(AbaDadosDaLoja)
  mudar(curta.raiz, 'Nome da loja', 'Casa da Baba Massas')
  assert.equal(botao(curta.raiz, 'Salvar dados da loja').props.disabled, true)
  assert.match(texto(curta.raiz.arvore), /ainda não devolve/)
  await enviar(curta.raiz)
  assert.equal(escritas().length, 0, 'sem a leitura completa, salvar apagaria descrição, documento, endereço e telefone')
})

const CONFIG_SITE = {
  id: 's1', empresaId: 'emp-1', slug: 'casa-da-baba', tituloPublico: 'Casa da Baba', subtituloPublico: 'Massas', logoUrl: '/uploads/antigo.png',
  corPrimaria: '#8b1e3f', whatsappPedidos: '(11) 99999-0000', pedidoMinimoEntrega: 30, freteGratisAcima: null, mensagemForaArea: 'Ainda não chegamos aí',
  dominioCustom: 'casadababa.com', modeloFiscal: 'manual', nfeAutomaticaHabilitada: false, lojaPadraoId: 'l1', ativo: true,
}
const PROIBIDOS = /ativar|desativar|publicar|domínio|dominio|fora da área|loja padrão|fiscal|nota fiscal|nf-?e/i

await provar('B2 site: sem site oferece criar com nome e endereço; criar faz POST e mostra a configuração', async () => {
  const { AbaSiteEImpressos } = await carregar('features/gestao/vitrine/AbaSiteEImpressos.jsx')
  let criado = false
  responder('GET', '/api/minha-vitrine', () => (criado ? { dados: { storefrontId: 's1', slug: 'casa-da-baba', tituloPublico: 'Casa da Baba', ativo: false } } : { status: 404, erro: { code: 'NAO_ENCONTRADO', message: 'Sua vitrine ainda não foi criada.' } }))
  responder('GET', '/api/minha-vitrine/configuracao', { dados: { ...CONFIG_SITE, ativo: false } })
  responder('POST', '/api/minha-vitrine', () => { criado = true; return { status: 201, dados: { storefrontId: 's1', slug: 'casa-da-baba' } } })
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(AbaSiteEImpressos)
  assert.deepEqual(rotulos(raiz), ['Nome do site', 'Endereço do site'])
  mudar(raiz, 'Nome do site', 'Casa da Babá')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  assert.equal(campo(raiz, 'Endereço do site').props.value, 'casa-da-baba', 'o endereço nasce do nome enquanto ela não mexe nele')
  await enviar(raiz)
  assert.deepEqual(chamadasDe('POST', '/api/minha-vitrine').map((c) => c.corpo), [{ tituloPublico: 'Casa da Babá', slug: 'casa-da-baba' }])
  assert.equal(aoSalvo.chamadas.length, 1)
  assert.equal(aoMudarPendencia.chamadas.at(-1), false)
  assert.ok(campo(raiz, 'Subtítulo'), 'depois de criar, a configuração aparece')
})

await provar('B2 site: mostra só os campos do passo e o PUT reenvia o grupo de marca inteiro', async () => {
  const { AbaSiteEImpressos } = await carregar('features/gestao/vitrine/AbaSiteEImpressos.jsx')
  responder('GET', '/api/minha-vitrine', { dados: { storefrontId: 's1', slug: 'casa-da-baba', tituloPublico: 'Casa da Baba', ativo: true } })
  responder('GET', '/api/minha-vitrine/configuracao', { dados: CONFIG_SITE })
  responder('PUT', '/api/minha-vitrine/configuracao', ({ corpo }) => ({ dados: { ...CONFIG_SITE, ...corpo } }))
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(AbaSiteEImpressos)
  assert.deepEqual(rotulos(raiz), ['Subtítulo', 'Cor principal', 'WhatsApp de pedidos', 'Pedido mínimo para entrega', 'Frete grátis acima de'])
  assert.doesNotMatch(todoTexto(raiz), PROIBIDOS, 'nada de ativar o site, domínio, mensagem de fora da área, loja padrão ou fiscal')
  assert.doesNotMatch(todoTexto(raiz), /Ainda não chegamos aí|casadababa\.com/, 'o valor dos campos fora do passo não aparece')
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
  responder('PUT', '/api/minha-vitrine/configuracao', ({ corpo }) => ({ dados: { ...CONFIG_SITE, ...corpo } }))
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

  const gif = { target: { files: [new File([new Uint8Array(10)], 'logo.gif', { type: 'image/gif' })], value: 'x' } }
  await andar(raiz.arvore).find((n) => n.type === 'input' && n.props?.type === 'file').props.onChange(gif)
  await raiz.atualizar()
  assert.match(texto(raiz.arvore), /Escolha uma imagem PNG ou JPG\./)
  assert.equal(chamadasDe('POST', /uploads/).length, 1, 'arquivo recusado não sobe')
})

await provar('B3 alertas de estoque: a loja da empresa, sem moeda, fuso nem KDS, e PATCH só do que mudou', async () => {
  const { AbaAlertasDeEstoque } = await carregar('features/gestao/estoque/AbaAlertasDeEstoque.jsx')
  const config = { lojaId: 'l1', diasAlertaValidade: 7, diasAlertaParado: 30, quantidadeMinimaPadrao: 10, quantidadeCriticaPadrao: 3, notificarEstoqueCritico: true, notificarValidade: true, notificarParado: true, notificarReposicao: true, fifoAtivo: true, moeda: 'BRL', timezone: 'America/Sao_Paulo', kdsHabilitado: true }
  responder('GET', LOJAS, { dados: [LOJA] })
  responder('GET', '/api/configuracoes?empresaId=emp-1&lojaId=l1', { dados: config })
  responder('PATCH', '/api/configuracoes', ({ corpo }) => ({ dados: { ...config, ...corpo } }))
  const { raiz, aoMudarPendencia, aoSalvo } = await montar(AbaAlertasDeEstoque)
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
  await clicar(raiz, 'Novo trecho')
  mudar(raiz, 'Título', 'Alergias')
  mudar(raiz, 'Texto', 'Não temos opção sem glúten.')
  mudar(raiz, 'Palavras-chave', 'Glúten; alergia')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  await enviar(raiz)
  assert.deepEqual(chamadasDe('POST', '/api/atendimento/caderno').map((c) => c.corpo), [{ titulo: 'Alergias', texto: 'Não temos opção sem glúten.', palavrasChave: 'glúten, alergia', nucleo: false }])
  assert.equal(aoSalvo.chamadas.length, 1)
  assert.equal(aoMudarPendencia.chamadas.at(-1), false)
  assert.match(texto(raiz.arvore), /Alergias/)

  await clicar(raiz, 'Editar: Entrega')
  assert.equal(campo(raiz, 'Texto').props.value, 'Entregamos até 5 km.')
  mudar(raiz, 'Texto', 'Entregamos até 7 km.')
  assert.equal(aoMudarPendencia.chamadas.at(-1), true)
  await enviar(raiz)
  assert.deepEqual(chamadasDe('PUT', '/api/atendimento/caderno/t1').map((c) => c.corpo), [{ titulo: 'Entrega', texto: 'Entregamos até 7 km.', palavrasChave: 'entrega, frete', nucleo: true }])
  assert.equal(aoSalvo.chamadas.length, 2)

  await clicar(raiz, 'Remover: Alergias')
  assert.equal(chamadasDe('POST', /arquivar/).length, 0, 'remover pede confirmação antes')
  await clicar(raiz, 'Remover do caderno')
  assert.equal(chamadasDe('POST', '/api/atendimento/caderno/t2/arquivar?arquivado=true').length, 1)
  assert.doesNotMatch(texto(raiz.arvore), /Alergias/)
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

  g.push = {
    suportado: () => true, permissao: () => 'default', pedirPermissao: async () => 'granted', inscricaoAtual: async () => null,
    inscrever: async () => ({ endpoint: 'https://push.exemplo.test/1', p256dh: 'p', auth: 'a' }), associar() {}, desinscrever: async () => {},
  }
  responder('GET', '/api/pwa/push/vapid-public', { dados: { publicKey: 'BAA' } })
  responder('POST', '/api/pwa/push/subscribe', { dados: { ok: true } })
  const proprio = await montar(AbaAvisos)
  assert.match(texto(proprio.raiz.arvore), /Receba avisos desta conta mesmo com o Console fechado\./)
  await clicar(proprio.raiz, 'Ativar avisos neste aparelho')
  assert.deepEqual(chamadasDe('POST', '/api/pwa/push/subscribe').map((c) => c.corpo.endpoint), ['https://push.exemplo.test/1'])
  assert.match(texto(proprio.raiz.arvore), /Avisos desta conta ativados neste aparelho\./)
  assert.ok(botao(proprio.raiz, 'Desativar avisos neste aparelho'))
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
  assert.match(texto(raiz.arvore), /O entregador não foi cadastrado: Seu usuário não tem permissão para isso\./)
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

await provar('C  os formulários cabem em 360 px: uma coluna, sem largura fixa maior que a tela', async () => {
  const estilo = fonte('features/gestao/formularioDeAjuste.module.css')
  assert.match(estilo, /grid-template-columns: repeat\(auto-fit, minmax\(min\(100%, \d+px\), 1fr\)\)/)
  for (const [, largura] of estilo.matchAll(/(?:^|[^-])width:\s*(\d+)px/g)) assert.ok(Number(largura) <= 328, `largura fixa ${largura}px`)
  for (const caminho of ['features/gestao/loja/AbaDadosDaLoja.jsx', 'features/gestao/vitrine/AbaSiteEImpressos.jsx', 'features/gestao/estoque/AbaAlertasDeEstoque.jsx', 'features/gestao/caderno/AbaCadernoDoAgente.jsx', 'features/gestao/avisos/AbaAvisos.jsx']) {
    assert.match(fonte(caminho), /formularioDeAjuste\.module\.css/, caminho)
  }
})

console.log(`\n${passou} verificações passaram${falhas.length ? `, ${falhas.length} falharam` : ''}.`)
if (falhas.length) process.exit(1)
