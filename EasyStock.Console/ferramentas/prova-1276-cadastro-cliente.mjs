/* eslint-disable no-console */
// Prova da issue #1276: no modo API o cadastro do cliente da conversa vai ao EasyStok.
// Salvar cadastro, cadastrar endereço e editar nome, telefone ou endereço fazem
// POST .../conversas/{id}/cliente e releem o dossiê; o cliente salvo sobrevive à
// sincronização de 5 s; conversa com cliente carrega a Ficha pelo dossiê ao abrir.
//
//   node ferramentas/prova-1276-cadastro-cliente.mjs

import { registerHooks } from 'node:module'
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
    return proximo(url, contexto)
  },
})

const acao = await import('../src/aplicacao/acoes.js')
const { reducer } = await import('../src/aplicacao/reducer.js')
const { NAO_LIGADAS } = await import('../src/aplicacao/api/naoLigadas.js')
const modulo = await import('../src/aplicacao/api/cliente.js').catch(() => ({}))

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
const dossie = {
  cliente: { id: CLIENTE, nome: 'Maria Souza', telefone: '+5511987654321' },
  enderecos: [{ logradouro: 'Rua Alvarenga', numero: '120', complemento: 'apto 12', bairro: 'Butantã', cep: '05500000', padrao: true }],
  totalPedidos: 0,
}

// Rede falsa: guarda as chamadas e responde o POST do cadastro e o GET do dossiê.
function rede({ statusPost = 200, dentroDaArea = true } = {}) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null })
    if (method === 'POST' && url.endsWith('/cliente')) {
      if (statusPost >= 400) {
        return new Response(JSON.stringify({ error: { code: 'VALIDATION_ERROR', message: 'Informe o telefone do cliente para cadastrar.' } }), { status: statusPost })
      }
      return new Response(JSON.stringify({ data: { clienteId: CLIENTE, nome: 'Maria Souza', telefone: '+5511987654321', novo: true, dentroDaArea, mensagemForaArea: dentroDaArea ? null : 'Ainda não entregamos aí.' } }), { status: 200 })
    }
    if (url.endsWith('/dossie')) return new Response(JSON.stringify({ data: dossie }), { status: 200 })
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  return chamadas
}

function montar(conversa) {
  const despachados = []
  const estadoRef = { current: { conversas: [conversa], sincronizacao: { estado: 'ok', aviso: null } } }
  const despachar = (a) => despachados.push(a)
  const acoes = modulo.criarAcoesClienteApi({ despachar, estadoRef })
  return { acoes, despachados }
}

const lead = (extra = {}) => ({
  id: 'conv-1', canal: 'Chat do site', nome: 'Visitante do site', clienteId: null, conta: 'lead',
  cliente: { telefone: null, endereco: null, enderecoCapturado: null, tags: [], notas: [] }, mensagens: [], ...extra,
})

await confere('as três ações saíram da lista de "não ligadas"', () => {
  for (const nome of ['salvarCadastroRapido', 'cadastrarEndereco', 'editarDadoCliente']) {
    assert.ok(!(nome in NAO_LIGADAS), `${nome} ainda está em NAO_LIGADAS`)
  }
})

await confere('Salvar cadastro faz POST com nome, telefone e endereço em partes, e relê o dossiê', async () => {
  const chamadas = rede()
  const { acoes, despachados } = montar(lead({ nome: 'Maria Souza', nomeDaDona: true }))
  acoes.salvarCadastroRapido('conv-1', { nome: 'Maria Souza', telefone: '(11) 98765-4321', endereco: 'Rua Alvarenga, 120, apto 12, Butantã, 05500-000' }, 0)
  await esperar()
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.ok(post, 'sem POST')
  assert.match(post.url, /\/api\/atendimento\/conversas\/conv-1\/cliente$/)
  assert.deepEqual(post.corpo, {
    nome: 'Maria Souza', telefone: '(11) 98765-4321',
    endereco: { cep: '05500000', logradouro: 'Rua Alvarenga', numero: '120', complemento: 'apto 12', bairro: 'Butantã' },
  })
  assert.ok(chamadas.some((c) => c.url.endsWith('/conversas/conv-1/dossie')), 'não releu o dossiê')
  const gravado = despachados.find((a) => a.tipo === acao.CLIENTE_DA_API)
  assert.ok(gravado, 'não gravou o cliente da API')
  assert.equal(gravado.clienteId, CLIENTE)
  assert.equal(gravado.nome, 'Maria Souza')
  assert.equal(gravado.cliente.telefone, '+5511987654321')
  assert.equal(gravado.cliente.endereco, 'Rua Alvarenga, 120, apto 12, Butantã, 05500-000')
})

await confere('Salvar cadastro recusado avisa e não grava nada local', async () => {
  rede({ statusPost: 400 })
  const { acoes, despachados } = montar(lead({ nome: 'Maria', nomeDaDona: true }))
  acoes.salvarCadastroRapido('conv-1', { nome: 'Maria', telefone: '', endereco: null }, 0)
  await esperar()
  assert.ok(despachados.some((a) => a.tipo === acao.AVISO_API && /telefone/.test(a.mensagem)), 'não avisou o motivo')
  assert.ok(!despachados.some((a) => a.tipo === acao.CLIENTE_DA_API || a.tipo === acao.SALVAR_CADASTRO_RAPIDO), 'gravou local')
})

await confere('Endereço fora da área grava e avisa', async () => {
  rede({ dentroDaArea: false })
  const { acoes, despachados } = montar(lead({ nome: 'Maria', nomeDaDona: true }))
  acoes.salvarCadastroRapido('conv-1', { nome: 'Maria', telefone: '11987654321', endereco: 'Rua X, 1, Centro, 01001-000' }, 0)
  await esperar()
  assert.ok(despachados.some((a) => a.tipo === acao.CLIENTE_DA_API), 'não gravou')
  assert.ok(despachados.some((a) => a.tipo === acao.AVISO_API && /área/.test(a.mensagem)), 'não avisou fora da área')
})

await confere('Cadastrar endereço manda o endereço capturado e o telefone do canal', async () => {
  const chamadas = rede()
  const { acoes } = montar(lead({ canal: 'WhatsApp', cliente: { telefoneCanal: '5511987654321', enderecoCapturado: 'Rua Alvarenga, 120, Butantã, 05500-000', tags: [], notas: [] } }))
  acoes.cadastrarEndereco('conv-1')
  await esperar()
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.ok(post, 'sem POST')
  assert.equal(post.corpo.telefone, '5511987654321')
  assert.equal(post.corpo.endereco.cep, '05500000')
})

await confere('Editar telefone manda só o telefone; editar avisos ainda avisa sem POST', async () => {
  const chamadas = rede()
  const { acoes, despachados } = montar(lead({ clienteId: CLIENTE, conta: 'cliente' }))
  acoes.editarDadoCliente('conv-1', 'telefone', '(11) 91111-2222', 0)
  await esperar()
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.deepEqual(post?.corpo, { nome: null, telefone: '(11) 91111-2222', endereco: null })
  chamadas.length = 0
  acoes.editarDadoCliente('conv-1', 'avisos', 'alergia a camarão', 0)
  await esperar()
  assert.equal(chamadas.length, 0, 'avisos chamou a API')
  assert.ok(despachados.some((a) => a.tipo === acao.AVISO_API && /ainda não ligado/.test(a.mensagem)), 'avisos não avisou')
})

await confere('Abrir conversa com cliente e Ficha vazia carrega pelo dossiê', async () => {
  const chamadas = rede()
  const { acoes, despachados } = montar(lead({ clienteId: CLIENTE, conta: 'cliente' }))
  acoes.carregarClienteDaConversa('conv-1')
  await esperar()
  assert.ok(chamadas.some((c) => c.url.endsWith('/conversas/conv-1/dossie')), 'não buscou o dossiê')
  assert.ok(despachados.some((a) => a.tipo === acao.CLIENTE_DA_API), 'não gravou')
})

await confere('Chat do site sem nome escrito pela dona não cadastra "Visitante do site"', async () => {
  const chamadas = rede()
  const { acoes, despachados } = montar(lead())
  acoes.salvarCadastroRapido('conv-1', { nome: 'Visitante do site', telefone: '11987654321', endereco: null }, 0)
  await esperar()
  assert.ok(!chamadas.some((c) => c.metodo === 'POST'), 'cadastrou com o nome genérico')
  assert.ok(despachados.some((a) => a.tipo === acao.AVISO_API && /nome/.test(a.mensagem)), 'não pediu o nome')
})

await confere('Renomear lead sem cadastro é rascunho: sobrevive à sincronização e vai no cadastro', async () => {
  const chamadas = rede()
  const { acoes, despachados } = montar(lead())
  acoes.editarDadoCliente('conv-1', 'nome', 'Joana Lima', 0)
  await esperar()
  assert.equal(chamadas.length, 0, 'renomear lead chamou a API')
  const renomear = despachados.find((a) => a.tipo === acao.RENOMEAR_LEAD_API)
  assert.ok(renomear, 'não guardou o rascunho do nome')
  const base = { conversas: [lead()], selecionadaId: 'conv-1', sincronizacao: { estado: 'ok', aviso: null } }
  const renomeado = reducer(base, renomear)
  const sincronizado = reducer(renomeado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [{ ...lead(), pedido: null }] })
  assert.equal(sincronizado.conversas[0].nome, 'Joana Lima')
  const depois = montar(sincronizado.conversas[0])
  depois.acoes.salvarCadastroRapido('conv-1', { nome: 'Visitante do site', telefone: '11987654321', endereco: null }, 0)
  await esperar()
  assert.equal(chamadas.find((c) => c.metodo === 'POST')?.corpo.nome, 'Joana Lima')
})

await confere('Cliente gravado sobrevive à sincronização de 5 s com o mesmo clienteId', () => {
  const base = { conversas: [lead()], selecionadaId: 'conv-1', sincronizacao: { estado: 'ok', aviso: null } }
  const comCliente = reducer(base, {
    tipo: acao.CLIENTE_DA_API, id: 'conv-1', clienteId: CLIENTE, nome: 'Maria Souza',
    cliente: { telefone: '+5511987654321', endereco: 'Rua Alvarenga, 120, Butantã, 05500-000' },
  })
  const doServidor = { ...lead({ clienteId: CLIENTE, conta: 'cliente' }), pedido: null }
  const sincronizado = reducer(comCliente, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [doServidor] })
  const c = sincronizado.conversas[0]
  assert.equal(c.cliente.telefone, '+5511987654321')
  assert.equal(c.cliente.endereco, 'Rua Alvarenga, 120, Butantã, 05500-000')
  assert.equal(c.nome, 'Maria Souza')
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length > 0) process.exit(1)
