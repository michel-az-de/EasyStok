/* eslint-disable no-console */
// Prova da issue #1430: o visitante do chat do site informa nome, telefone e e-mail antes de
// conversar. No modo API, abrir a conversa do lead lê o dossiê e guarda o contato informado
// (separado do cadastro); ele sobrevive à sincronização de 5 s; "Confirmar cadastro" faz um
// POST .../cliente com nome, telefone e e-mail; e as edições sem e-mail mandam o corpo de antes.
//
// Telefone do formulário não é telefone verificado: se já é de um cadastro, a API responde 409
// TELEFONE_JA_CADASTRADO e o cartão pergunta antes de ligar. O cartão (JSX real, hooks
// controlados, no molde da prova-1586) espera a resposta, trava o segundo toque e mostra a
// recusa junto do botão. Lead restaurado na carga também lê o dossiê. Não prova pixels.
//
//   node ferramentas/prova-1430-contato-informado.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import { transformWithOxc } from 'vite'

const src = new URL('../src/', import.meta.url)
const CARTAO = new URL('features/ficha-cliente/ContatoInformado.jsx', src)
const cartaoCompilado = (await transformWithOxc(readFileSync(CARTAO, 'utf8'), CARTAO.pathname, { jsx: { runtime: 'automatic' } })).code

// Hooks do cartão, controlados: o estado fica na `Tela` que está renderizando.
let ativo
globalThis.__prova1430 = {
  useState(inicial) {
    const host = ativo
    const i = host.indice++
    if (!(i in host.valores)) host.valores[i] = inicial
    return [host.valores[i], (valor) => { host.valores[i] = typeof valor === 'function' ? valor(host.valores[i]) : valor }]
  },
  useRef(inicial) { const host = ativo; const i = host.indice++; return host.valores[i] ??= { current: inicial } },
}
const codigo = (texto) => 'data:text/javascript,' + encodeURIComponent(texto)

registerHooks({
  resolve(especificador, contexto, proximo) {
    // Só o cartão enxerga estas fronteiras; o resto da prova importa os módulos reais.
    if (contexto.parentURL === CARTAO.href) {
      if (especificador === 'react') return { url: codigo('const h=globalThis.__prova1430; export const useState=h.useState,useRef=h.useRef'), shortCircuit: true }
      if (especificador === 'react/jsx-runtime') return { url: codigo('export const Fragment="fragment"; export const jsx=(type,props,key)=>({type,props,key}); export const jsxs=jsx'), shortCircuit: true }
      if (especificador.endsWith('.css')) return { url: codigo('export default new Proxy({},{get:(_,chave)=>String(chave)})'), shortCircuit: true }
      if (especificador.endsWith('/componentes/Botao')) return { url: codigo('export function Botao(){return null}'), shortCircuit: true }
    }
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url === CARTAO.href) return { format: 'module', shortCircuit: true, source: cartaoCompilado }
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    return proximo(url, contexto)
  },
})

const acao = await import('../src/aplicacao/acoes.js')
const { reducer } = await import('../src/aplicacao/reducer.js')
const { criarAcoes } = await import('../src/aplicacao/criarAcoes.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { telefoneDoE164 } = await import('../src/dominio/formato.js')
const { contatoInformadoDoDossie } = await import('../src/infra/api/traducaoCliente.js')
const modulo = await import('../src/aplicacao/api/cliente.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}
const esperar = () => new Promise((r) => setTimeout(r, 20))

const CLIENTE = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
const INFORMADO = { nome: 'Maria Souza', telefone: '+5511987654321', email: 'maria@exemplo.com', informadoEm: '2026-10-07T12:00:00Z' }
const dossieDoLead = {
  cliente: { id: null, nome: 'Maria Souza', telefone: null },
  enderecos: [], totalPedidos: 0, contatoInformado: INFORMADO,
}

const DONA_DO_TELEFONE = 'Maria Cadastrada'
const PERGUNTA = `Este telefone já é de ${DONA_DO_TELEFONE}. Confira com o cliente antes de ligar esta conversa ao cadastro dele.`

// `telefoneDe`: o telefone informado já é do cadastro dessa pessoa (a API só liga com a confirmação).
function rede({ dossie = dossieDoLead, telefoneDe = null } = {}) {
  const chamadas = []
  let atual = dossie
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null })
    if (method === 'POST' && url.endsWith('/cliente') && telefoneDe) {
      if (!JSON.parse(body).confirmarVinculoExistente) {
        return new Response(JSON.stringify({ error: { code: 'TELEFONE_JA_CADASTRADO', message: PERGUNTA, details: { clienteId: CLIENTE, nome: telefoneDe } } }), { status: 409 })
      }
      atual = { cliente: { id: CLIENTE, nome: telefoneDe, telefone: '+5511987654321' }, enderecos: [] }
      return new Response(JSON.stringify({ data: { clienteId: CLIENTE, nome: telefoneDe, telefone: '+5511987654321', email: null, novo: false, dentroDaArea: null } }), { status: 200 })
    }
    if (method === 'POST' && url.endsWith('/cliente')) {
      // Depois de confirmado, o dossiê é o do cliente (sem o contato informado).
      atual = { cliente: { id: CLIENTE, nome: 'Maria Souza', telefone: '+5511987654321', email: 'maria@exemplo.com' }, enderecos: [] }
      return new Response(JSON.stringify({ data: { clienteId: CLIENTE, nome: 'Maria Souza', telefone: '+5511987654321', email: 'maria@exemplo.com', novo: true, dentroDaArea: null } }), { status: 200 })
    }
    if (url.endsWith('/dossie')) return new Response(JSON.stringify({ data: atual }), { status: 200 })
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  return chamadas
}

const lead = (extra = {}) => ({
  id: 'conv-1', canal: 'Chat do site', nome: 'Maria Souza', clienteId: null, conta: 'lead', estado: 'Em atendimento',
  cliente: { telefone: null, endereco: null, enderecoCapturado: null, tags: [], notas: [] }, mensagens: [], pedido: null, ...extra,
})

function montar(conversa) {
  const despachados = []
  const estadoRef = { current: { conversas: [conversa], sincronizacao: { estado: 'ok', aviso: null } } }
  const acoes = modulo.criarAcoesClienteApi({ despachar: (a) => despachados.push(a), estadoRef })
  return { acoes, despachados }
}

await confere('#1936: endereço de lead do chat do site vai com o telefone e o nome informados', async () => {
  const chamadas = rede()
  const { acoes } = montar(lead({ contatoInformado: INFORMADO }))
  acoes.editarDadoCliente('conv-1', 'endereco', 'Rua dos Pinheiros, 200, Pinheiros, São Paulo, 05415-000', 0)
  await esperar()
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.ok(post, 'sem POST')
  assert.equal(post.corpo.telefone, '+5511987654321', 'cadastro sem o telefone do visitante')
  assert.equal(post.corpo.nome, 'Maria Souza')
  assert.deepEqual(post.corpo.endereco, { cep: '05415000', logradouro: 'Rua dos Pinheiros', numero: '200', complemento: null, bairro: 'Pinheiros' })
})

await confere('#1936: cidade e UF no fim do endereço não viram bairro', async () => {
  const { partesDoEndereco } = await import('../src/dominio/formato.js')
  for (const cidade of ['São Paulo', 'Sao Paulo - SP', 'São Paulo/SP', 'SP']) {
    const p = partesDoEndereco(`Rua A, 10, apto 2, Pinheiros, ${cidade}, 05415-000`)
    assert.equal(p.bairro, 'Pinheiros', cidade)
    assert.equal(p.complemento, 'apto 2', cidade)
  }
  assert.equal(partesDoEndereco('Rua A, 10, Pinheiros, 05415-000').bairro, 'Pinheiros', 'formato antigo')
})

await confere('telefone E.164 da API aparece na máscara da tela', () => {
  assert.equal(telefoneDoE164('+5511987654321'), '(11) 98765-4321')
  assert.equal(telefoneDoE164('+551132654321'), '(11) 3265-4321')
  assert.equal(telefoneDoE164(null), '')
})

await confere('dossiê de lead traz o contato informado; dossiê de cliente não', () => {
  assert.deepEqual(contatoInformadoDoDossie(dossieDoLead), INFORMADO)
  assert.equal(contatoInformadoDoDossie({ ...dossieDoLead, cliente: { id: CLIENTE } }), null)
  assert.equal(contatoInformadoDoDossie({ cliente: { id: null } }), null)
})

await confere('abrir a conversa lê a Ficha: lead do chat do site sem contato, ou cliente ainda não lido', () => {
  assert.equal(modulo.precisaLerFicha(lead()), true)
  assert.equal(modulo.precisaLerFicha(lead({ contatoInformado: INFORMADO })), false)
  assert.equal(modulo.precisaLerFicha(lead({ canal: 'WhatsApp' })), false)
  assert.equal(modulo.precisaLerFicha(lead({ clienteId: CLIENTE })), true)
  assert.equal(modulo.precisaLerFicha(lead({ clienteId: CLIENTE, cliente: { daApi: true } })), false)
  assert.equal(modulo.precisaLerFicha(undefined), false)
})

await confere('selecionar a conversa do lead no modo API busca o dossiê e guarda o contato', async () => {
  const chamadas = rede()
  const despachados = []
  const estadoRef = {
    current: {
      conversas: [lead()], catalogo: { cardapio: [], janelas: [], canais: [] },
      funcionamento: {}, lojaAberta: true, sincronizacao: { estado: 'ok', aviso: null },
    },
  }
  const despachar = (a) => despachados.push(a)
  const agoraRef = { current: Date.parse('2026-10-07T12:00:00Z') }
  const locais = criarAcoes({
    despachar, agoraRef, estadoRef, pendentes: { current: [] },
    consultarAgente: async () => {}, perguntarAssistente: async () => '', pedirNotificacaoDoNavegador: async () => {},
  })
  comApi(locais, { despachar, agoraRef, estadoRef }).selecionar('conv-1')
  await esperar()
  assert.ok(chamadas.some((c) => c.url.endsWith('/conversas/conv-1/dossie')), 'não buscou o dossiê')
  const gravado = despachados.find((a) => a.tipo === acao.CONTATO_INFORMADO_API)
  assert.deepEqual(gravado?.contato, INFORMADO)
  assert.ok(!despachados.some((a) => a.tipo === acao.CLIENTE_DA_API), 'tratou o lead como cliente')
})

await confere('contato informado sobrevive à sincronização de 5 s', () => {
  const base = { conversas: [lead()], selecionadaId: 'conv-1', sincronizacao: { estado: 'ok', aviso: null } }
  const comContato = reducer(base, { tipo: acao.CONTATO_INFORMADO_API, id: 'conv-1', contato: INFORMADO })
  const sincronizado = reducer(comContato, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [lead()] })
  assert.deepEqual(sincronizado.conversas[0].contatoInformado, INFORMADO)
})

await confere('Confirmar cadastro manda nome, telefone e e-mail informados, sem o nome escrito pela dona', async () => {
  const chamadas = rede()
  const { acoes, despachados } = montar(lead({ contatoInformado: INFORMADO }))
  acoes.salvarCadastroRapido('conv-1', { nome: INFORMADO.nome, telefone: INFORMADO.telefone, email: INFORMADO.email, endereco: null }, 0)
  await esperar()
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.ok(post, 'sem POST')
  assert.match(post.url, /\/api\/atendimento\/conversas\/conv-1\/cliente$/)
  assert.deepEqual(post.corpo, { nome: 'Maria Souza', telefone: '+5511987654321', endereco: null, email: 'maria@exemplo.com' })
  assert.ok(despachados.some((a) => a.tipo === acao.CLIENTE_DA_API), 'não releu o cliente')
  assert.ok(!despachados.some((a) => a.tipo === acao.AVISO_API), 'avisou sem motivo')
})

await confere('cadastro sem e-mail manda o corpo de antes (sem a chave email)', async () => {
  const chamadas = rede({ dossie: { cliente: { id: CLIENTE, nome: 'Maria Souza', telefone: '+5511987654321' }, enderecos: [] } })
  const { acoes } = montar(lead({ clienteId: CLIENTE, conta: 'cliente' }))
  acoes.editarDadoCliente('conv-1', 'telefone', '(11) 91111-2222', 0)
  await esperar()
  assert.deepEqual(chamadas.find((c) => c.metodo === 'POST')?.corpo, { nome: null, telefone: '(11) 91111-2222', endereco: null })
})

await confere('chat do site sem formulário continua pedindo o nome da dona', async () => {
  const chamadas = rede()
  const { acoes, despachados } = montar(lead({ nome: 'Visitante do site' }))
  acoes.salvarCadastroRapido('conv-1', { nome: 'Visitante do site', telefone: '11987654321', endereco: null }, 0)
  await esperar()
  assert.ok(!chamadas.some((c) => c.metodo === 'POST'), 'cadastrou com o nome genérico')
  assert.ok(despachados.some((a) => a.tipo === acao.AVISO_API && /nome/.test(a.mensagem)), 'não pediu o nome')
})

// ── Telefone do formulário que já é de um cadastro: a API não liga sem a dona confirmar ──
const CONFIRMAR = { nome: INFORMADO.nome, telefone: INFORMADO.telefone, email: INFORMADO.email, endereco: null }

await confere('telefone já é de um cadastro: a ação devolve de quem é, sem ligar a conversa nem acender a faixa', async () => {
  const chamadas = rede({ telefoneDe: DONA_DO_TELEFONE })
  const { acoes, despachados } = montar(lead({ contatoInformado: INFORMADO }))
  const resultado = await acoes.salvarCadastroRapido('conv-1', CONFIRMAR, 0)
  assert.deepEqual(resultado, { erro: PERGUNTA, jaCadastrado: { nome: DONA_DO_TELEFONE } })
  assert.deepEqual(chamadas.filter((c) => c.metodo === 'POST').map((c) => c.corpo), [{ nome: 'Maria Souza', telefone: '+5511987654321', endereco: null, email: 'maria@exemplo.com' }])
  assert.ok(!despachados.some((a) => a.tipo === acao.CLIENTE_DA_API), 'ligou a conversa sem a dona confirmar')
  assert.ok(!despachados.some((a) => a.tipo === acao.AVISO_API), 'a pergunta é do cartão, não da faixa')
})

await confere('"Ligar ao cadastro" repete o POST com confirmarVinculoExistente e relê a Ficha, sem aviso a mais', async () => {
  const chamadas = rede({ telefoneDe: DONA_DO_TELEFONE })
  const { acoes, despachados } = montar(lead({ contatoInformado: INFORMADO }))
  const resultado = await acoes.salvarCadastroRapido('conv-1', { ...CONFIRMAR, confirmarVinculoExistente: true }, 0)
  assert.equal(resultado, undefined)
  assert.deepEqual(chamadas.find((c) => c.metodo === 'POST').corpo,
    { nome: 'Maria Souza', telefone: '+5511987654321', endereco: null, email: 'maria@exemplo.com', confirmarVinculoExistente: true })
  assert.equal(despachados.find((a) => a.tipo === acao.CLIENTE_DA_API)?.nome, DONA_DO_TELEFONE)
  assert.ok(!despachados.some((a) => a.tipo === acao.AVISO_API), 'avisou do que a dona acabou de mandar fazer')
})

// ── O cartão da Ficha: JSX real, hooks controlados ──
const { ContatoInformado } = await import('../src/features/ficha-cliente/ContatoInformado.jsx')

class Tela {
  constructor(props) { this.props = props; this.valores = []; this.renderizar() }
  renderizar() { ativo = this; this.indice = 0; this.arvore = ContatoInformado(this.props); return this.arvore }
}
const nos = (arvore) => (Array.isArray(arvore) ? arvore.flatMap(nos)
  : !arvore || typeof arvore !== 'object' ? [] : [arvore, ...nos(arvore.props?.children)])
const texto = (arvore) => (Array.isArray(arvore) ? arvore.map(texto).join('')
  : arvore == null || typeof arvore === 'boolean' ? '' : typeof arvore !== 'object' ? String(arvore) : texto(arvore.props?.children))
const botoes = (tela) => nos(tela.arvore).filter((n) => n.type?.name === 'Botao')
const botao = (tela, rotulo) => botoes(tela).find((n) => texto(n) === rotulo)
const alertas = (tela) => nos(tela.arvore).filter((n) => n.props?.role === 'alert').map(texto)
const pendente = () => { let resolver; const promise = new Promise((r) => { resolver = r }); return { promise, resolver } }

await confere('"Confirmar cadastro" espera a resposta, trava o segundo toque e mostra a recusa junto do botão', async () => {
  const espera = pendente()
  let idas = 0
  const tela = new Tela({ contato: INFORMADO, aoConfirmar: () => { idas += 1; return espera.promise } })
  assert.equal(botao(tela, 'Confirmar cadastro').props.disabled, false)
  const ida = botao(tela, 'Confirmar cadastro').props.onClick()
  botao(tela, 'Confirmar cadastro').props.onClick()
  tela.renderizar()
  assert.equal(idas, 1, 'o segundo toque mandou outro POST')
  assert.equal(botao(tela, 'Salvando…').props.disabled, true)
  espera.resolver({ erro: 'Sem conexão com o EasyStok.' }); await ida; tela.renderizar()
  assert.deepEqual(alertas(tela), ['Sem conexão com o EasyStok.'])
  assert.equal(botao(tela, 'Confirmar cadastro').props.disabled, false)
})

await confere('telefone de outro cadastro: o cartão pergunta, "Não ligar" desiste e "Ligar ao cadastro de Fulano" confirma', async () => {
  const idas = []
  const jaCadastrado = { erro: PERGUNTA, jaCadastrado: { nome: DONA_DO_TELEFONE } }
  const tela = new Tela({ contato: INFORMADO, aoConfirmar: async (opcoes) => { idas.push(opcoes); return opcoes?.confirmarVinculoExistente ? undefined : jaCadastrado } })
  await botao(tela, 'Confirmar cadastro').props.onClick(); tela.renderizar()
  assert.deepEqual(alertas(tela), [PERGUNTA])
  assert.deepEqual(botoes(tela).map(texto), [`Ligar ao cadastro de ${DONA_DO_TELEFONE}`, 'Não ligar'])

  botao(tela, 'Não ligar').props.onClick(); tela.renderizar()
  assert.deepEqual(alertas(tela), [])
  assert.deepEqual(botoes(tela).map(texto), ['Confirmar cadastro'])
  assert.equal(idas.length, 1, '"Não ligar" não pode chamar a API')

  await botao(tela, 'Confirmar cadastro').props.onClick(); tela.renderizar()
  await botao(tela, `Ligar ao cadastro de ${DONA_DO_TELEFONE}`).props.onClick(); tela.renderizar()
  assert.deepEqual(idas, [undefined, undefined, { confirmarVinculoExistente: true }])
  assert.deepEqual(alertas(tela), [PERGUNTA], 'a resposta que desmonta o cartão não muda o que ele mostrava')
})

await confere('a Ficha entrega ao cartão a escolha da dona e o reinicia a cada conversa', () => {
  const fonte = readFileSync(new URL('features/ficha-cliente/BlocoCliente.jsx', src), 'utf8')
  assert.match(fonte, /<ContatoInformado\s+key=\{conversa\.id\}/)
  assert.match(fonte, /aoConfirmar=\{\(opcoes\) => salvarCadastroRapido\(conversa\.id, \{[^}]*\.\.\.opcoes,?\s*\}, agora\)\}/)
})

// ── Lead que já abre selecionado (restaurado na carga) não passa por `selecionar` ──
await confere('conversa restaurada na carga: o efeito do Provider lê a Ficha pelo mesmo critério do clique', () => {
  const fonte = readFileSync(new URL('aplicacao/AtendimentoProvider.jsx', src), 'utf8')
  assert.match(fonte, /const precisaDossie = FONTE_API\s+&& precisaLerFicha\(estado\.conversas\.find\(\(c\) => c\.id === estado\.selecionadaId\)\)/)
  // Lead sem formulário segue precisando para sempre: quem evita reler a cada volta é o efeito
  // depender do booleano, não da conversa.
  assert.match(fonte, /if \(precisaDossie\) acoesAtivas\.carregarClienteDaConversa\(estado\.selecionadaId\)\s+\}, \[precisaDossie, estado\.selecionadaId, acoesAtivas\]\)/)
  assert.ok(!/deveCarregarDossie/.test(fonte), 'o Provider ainda decide só pelo cliente vinculado')
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length > 0) process.exit(1)
