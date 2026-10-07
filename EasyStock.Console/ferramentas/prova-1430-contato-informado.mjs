/* eslint-disable no-console */
// Prova da issue #1430: o visitante do chat do site informa nome, telefone e e-mail antes de
// conversar. No modo API, abrir a conversa do lead lê o dossiê e guarda o contato informado
// (separado do cadastro); ele sobrevive à sincronização de 5 s; "Confirmar cadastro" faz um
// POST .../cliente com nome, telefone e e-mail; e as edições sem e-mail mandam o corpo de antes.
//
//   node ferramentas/prova-1430-contato-informado.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
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

function rede({ dossie = dossieDoLead } = {}) {
  const chamadas = []
  let atual = dossie
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null })
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

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length > 0) process.exit(1)
