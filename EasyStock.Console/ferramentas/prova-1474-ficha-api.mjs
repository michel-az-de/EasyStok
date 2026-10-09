/* eslint-disable no-console */
// Prova da issue #1474, segunda rodada (bancas de UX e front-end sobre a Ficha no modo API):
//   R1  esteira da Ficha ligada ao PATCH do KDS; cancelar, estornar e voltar etapa somem;
//   R2  botão de ação não ligada some no modo API; bloquear cliente ligado ao EasyStok;
//   R3  pré-requisito da cobrança antes do clique, na ordem certa;
//   R4  um botão só cria o pedido no modo API ("Gerar cobrança e enviar");
//   R5  antes do pedido o total é só dos itens;
//   R6  número do entregador é o mesmo da Cozinha e das Entregas;
//   R7  recusar pedido pede confirmação com o que acontece;
//   R8  baixa à mão em pedido que espera aprovação: motivo e "Aprovar pedido";
//   R9  dica de CEP fora da área some no modo API;
//   R11 nota de sistema não vira prévia do Balcão.
//
// Arquivo à parte da prova-1474-revisao-visual.mjs porque outra sessão edita aquela ao mesmo tempo.
//
//   node ferramentas/prova-1474-ficha-api.mjs

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

globalThis.sessionStorage = {
  getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' } }),
  setItem: () => {}, removeItem: () => {},
}
globalThis.window = globalThis.window ?? { dispatchEvent: () => {} }

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

let respostaFetch = () => ({ data: null })
const chamadas = []
globalThis.fetch = async (url, opcoes = {}) => {
  const chamada = { url: String(url), metodo: opcoes.method ?? 'GET', corpo: opcoes.body ? JSON.parse(opcoes.body) : undefined }
  chamadas.push(chamada)
  const r = await respostaFetch(chamada)
  const status = r?.status ?? 200
  return new Response(JSON.stringify(r?.corpo ?? r), { status, headers: { 'Content-Type': 'application/json' } })
}

const acao = await import('../src/aplicacao/acoes.js')
const { reducer } = await import('../src/aplicacao/reducer.js')
const naoLigadas = await import('../src/aplicacao/api/naoLigadas.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const { criarAcoesClienteApi } = await import('../src/aplicacao/api/cliente.js')
const comandaApi = await import('../src/infra/api/comandaApi.js')
const { clienteDoDossie } = await import('../src/infra/api/traducaoCliente.js')
const cobranca = await import('../src/dominio/cobranca.js')
const resumoPedido = await import('../src/dominio/resumoPedido.js')
const pedidoDom = await import('../src/dominio/pedido.js')
const { numeroParaEntregador } = await import('../src/dominio/despacho.js')
const entregasApi = await import('../src/dominio/entregasApi.js')
const clienteDom = await import('../src/dominio/cliente.js')
const { previaDaConversa } = await import('../src/dominio/conversa.js')

const PEDIDO_ID = '3f2a9b1c-1111-2222-3333-444444444444'
const PEDIDO_PAGO = {
  pedidoId: PEDIDO_ID, status: 'aguardando', total: 30, frete: 5, itens: [], totalPago: 30, pagamentos: [],
  cobranca: { cobrancaId: 'cob', provedor: 'mercado_pago', status: 'Paga', valor: 30, linkPagamento: 'https://mp/x', criadaEm: '2026-10-08T15:00:00Z', expiraEm: '2099-01-01T00:00:00Z', pagaEm: '2026-10-08T15:05:00Z', valorPago: 30, tentativa: 1 },
}
const montarComanda = (pedidoApi) => {
  const despachos = []
  const locais = []
  const pedido = comandaApi.pedidoDaApi(pedidoApi, { meio: 'pix' })
  const estadoRef = { current: { conversas: [{ id: 'c1', nome: 'Ana Souza', pedido }] } }
  const acoesLocais = new Proxy({}, { get: (_, nome) => (...args) => { locais.push(nome); return args } })
  const api = criarAcoesComandaApi(acoesLocais, { despachar: (a) => despachos.push(a), estadoRef })
  return { api, despachos, locais, pedido }
}

// ── R1 ─────────────────────────────────────────────────────────────────────
await confere('R1 etapa do console vira o status do backend (PedidoStateMachine)', () => {
  assert.deepEqual(comandaApi.STATUS_DO_PASSO, {
    preparo: 'preparando', embalado: 'pronto', entrega: 'saiu_para_entrega', entregue: 'entregue',
  })
})
await confere('R1 "Iniciar preparo" faz PATCH /api/kds/pedidos/{id}/status e relê o pedido', async () => {
  chamadas.length = 0
  respostaFetch = (c) => ({ data: c.metodo === 'PATCH' ? {} : { ...PEDIDO_PAGO, status: 'preparando' } })
  const { api, despachos, locais } = montarComanda(PEDIDO_PAGO)
  const r = await api.avancarEsteira('c1', 'preparo')
  assert.equal(r, undefined)
  const patch = chamadas.find((c) => c.metodo === 'PATCH')
  assert.ok(patch, chamadas.map((c) => `${c.metodo} ${c.url}`).join(', '))
  assert.equal(patch.url, `/api/kds/pedidos/${PEDIDO_ID}/status`)
  assert.deepEqual(patch.corpo, { status: 'preparando' })
  const sinc = despachos.find((d) => d.tipo === acao.SINCRONIZAR_PEDIDO)
  assert.equal(sinc?.pedido.estado, 'preparo')
  assert.deepEqual(locais, [])
  respostaFetch = () => ({ data: null })
})
await confere('R1 despachar vai direto a saiu_para_entrega (o aviso ao cliente é do EasyStok)', async () => {
  chamadas.length = 0
  respostaFetch = () => ({ data: {} })
  const { api } = montarComanda({ ...PEDIDO_PAGO, status: 'pronto' })
  await api.avancarEsteira('c1', 'entrega', { tipo: 'motoboy', nome: 'Zé' })
  assert.deepEqual(chamadas.find((c) => c.metodo === 'PATCH')?.corpo, { status: 'saiu_para_entrega' })
  respostaFetch = () => ({ data: null })
})
await confere('R1 erro do backend volta para a barra ({ erro }) sem mexer no estado local', async () => {
  respostaFetch = (c) => (c.metodo === 'PATCH'
    ? { status: 400, corpo: { error: { code: 'VALIDATION_ERROR', message: 'Transição inválida: aguardando_aprovacao_baba → preparando.' } } }
    : { data: null })
  const { api, despachos, locais } = montarComanda(PEDIDO_PAGO)
  const r = await api.avancarEsteira('c1', 'preparo')
  assert.equal(r?.erro, 'Transição inválida: aguardando_aprovacao_baba → preparando.')
  assert.ok(!despachos.some((d) => d.tipo === acao.AVANCAR_ESTEIRA))
  assert.deepEqual(locais, [])
  respostaFetch = () => ({ data: null })
})
await confere('R1 sem pedido no EasyStok a esteira avisa e não chama a API', async () => {
  chamadas.length = 0
  const despachos = []
  const estadoRef = { current: { conversas: [{ id: 'c1', pedido: { pedidoId: null, itens: [] } }] } }
  const api = criarAcoesComandaApi({}, { despachar: (a) => despachos.push(a), estadoRef })
  await api.avancarEsteira('c1', 'preparo')
  assert.equal(chamadas.length, 0)
  assert.equal(despachos[0]?.tipo, acao.AVISO_API)
})
await confere('R1 avançar a esteira está ligado; cancelar, estornar e voltar etapa somem no modo API', () => {
  assert.equal(naoLigadas.acaoDisponivel('avancarEsteira', { fonteApi: true }), true)
  for (const nome of ['cancelarPedido', 'marcarEstorno', 'corrigirPasso', 'desfazerEsteira']) {
    assert.equal(naoLigadas.acaoDisponivel(nome, { fonteApi: true }), false, nome)
    assert.equal(naoLigadas.acaoDisponivel(nome, { fonteApi: false }), true, nome)
  }
  assert.equal('avancarEsteira' in naoLigadas.SO_NO_EASYSTOK, false)
})

// ── R2 ─────────────────────────────────────────────────────────────────────
await confere('R2 botões de ação sem endpoint somem no modo API', () => {
  for (const nome of ['reabrir', 'aplicarCupom', 'marcarRecebidoEntrega', 'refazerCobranca', 'marcarComprovante',
    'aceitarDivergencia', 'mudarEnderecoDoPedido', 'resgatarRecompensa']) {
    assert.equal(naoLigadas.acaoDisponivel(nome, { fonteApi: true }), false, nome)
  }
  assert.equal(naoLigadas.acaoDisponivel('enviar', { fonteApi: true }), true)
  // #1241 ligou o cardápio do dia no modo API: a aba "Gerir o dia" volta a aparecer.
  assert.equal(naoLigadas.acaoDisponivel('alternarDisponibilidade', { fonteApi: true }), true)
})
await confere('R2 bloquear cliente está ligado: POST /api/clientes/{id}/bloquear e relê o dossiê', async () => {
  assert.equal(naoLigadas.acaoDisponivel('bloquearCliente', { fonteApi: true }), true)
  assert.equal(naoLigadas.acaoDisponivel('desbloquearCliente', { fonteApi: true }), true)
  chamadas.length = 0
  respostaFetch = (c) => ({ data: c.url.endsWith('/dossie')
    ? { cliente: { id: 'cl1', nome: 'Ana Souza', telefone: '11999990000', motivoBloqueio: 'Golpe' }, bloqueado: true, enderecos: [], tags: [], notas: [] }
    : {} })
  const despachos = []
  const api = criarAcoesClienteApi({ despachar: (a) => despachos.push(a), estadoRef: { current: { conversas: [{ id: 'c1', nome: 'Ana Souza', clienteId: 'cl1', cliente: {} }] } } })
  await api.bloquearCliente('Ana Souza', 'Golpe', 'c1')
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.equal(post?.url, '/api/clientes/cl1/bloquear')
  assert.deepEqual(post.corpo, { motivo: 'Golpe' })
  const doDossie = despachos.find((d) => d.tipo === acao.CLIENTE_DA_API)
  assert.equal(doDossie?.bloqueio?.motivo, 'Golpe')
  respostaFetch = () => ({ data: null })
})
await confere('R2 clientes homônimos: bloqueio e desbloqueio usam a conversa escolhida', async () => {
  chamadas.length = 0
  respostaFetch = () => ({ data: null })
  const api = criarAcoesClienteApi({ despachar: () => {}, estadoRef: { current: { conversas: [
    { id: 'c1', nome: 'Ana Souza', clienteId: 'cl1' },
    { id: 'c2', nome: 'Ana Souza', clienteId: 'cl2' },
  ] } } })
  await api.bloquearCliente('Ana Souza', 'Motivo', 'c2')
  await api.desbloquearCliente('Ana Souza', 'c2')
  assert.deepEqual(chamadas.filter((c) => c.metodo === 'POST').map((c) => c.url), [
    '/api/clientes/cl2/bloquear', '/api/clientes/cl2/desbloquear',
  ])
  chamadas.length = 0
  await api.bloquearCliente('Ana Souza', 'Motivo')
  assert.equal(chamadas.length, 0, 'sem id não escolhe cliente pelo nome')
})
await confere('R2 bloqueio do dossiê chega à conversa e some no desbloqueio', () => {
  const lido = clienteDoDossie({ cliente: { id: 'cl1', nome: 'Ana', motivoBloqueio: 'Golpe' }, bloqueado: true })
  let estado = reducer({ conversas: [{ id: 'c1', nome: 'Ana', cliente: {}, mensagens: [] }] }, { tipo: acao.CLIENTE_DA_API, id: 'c1', ...lido })
  assert.equal(estado.conversas[0].bloqueio?.motivo, 'Golpe')
  const livre = clienteDoDossie({ cliente: { id: 'cl1', nome: 'Ana' }, bloqueado: false })
  estado = reducer(estado, { tipo: acao.CLIENTE_DA_API, id: 'c1', ...livre })
  assert.equal(estado.conversas[0].bloqueio, null)
})
await confere('R2 bloquear sem cadastro salvo avisa e não chama a API', async () => {
  chamadas.length = 0
  const despachos = []
  const api = criarAcoesClienteApi({ despachar: (a) => despachos.push(a), estadoRef: { current: { conversas: [{ id: 'c1', nome: 'Lead', clienteId: null, cliente: {} }] } } })
  await api.bloquearCliente('Lead', 'Golpe')
  assert.equal(chamadas.length, 0)
  assert.match(despachos[0]?.mensagem ?? '', /Salve o cadastro/)
})

// ── R3 ─────────────────────────────────────────────────────────────────────
await confere('R3 motivo antes de cobrar, na ordem: vazia, cadastro, endereço, janela', () => {
  const m = (pedido, extra = {}) => resumoPedido.motivoAntesDeCobrar(
    { pedido, clienteId: 'cl', endereco: 'Rua X, 1', ...extra }, { fonteApi: true })
  const cheio = { itens: [{ sku: 's', qtd: 1 }], janela: 'j|2026-10-09' }
  assert.equal(m({ itens: [], janela: null }, { clienteId: null, endereco: null }), 'A comanda está vazia.')
  assert.equal(m({ ...cheio, janela: null }, { clienteId: null, endereco: null }), 'Salve o cadastro do cliente (Contato e endereço).')
  assert.equal(m({ ...cheio, janela: null }, { endereco: null }), 'Falta o endereço de entrega (Contato e endereço).')
  assert.equal(m({ ...cheio, janela: null }), 'Escolha a janela de entrega, logo abaixo da comanda.')
  assert.equal(m(cheio), null)
  // Na demonstração só a comanda vazia trava: cadastro e janela são do EasyStok.
  assert.equal(resumoPedido.motivoAntesDeCobrar({ pedido: { ...cheio, janela: null }, clienteId: null, endereco: null }, { fonteApi: false }), null)
})

// ── R4 e R5 ────────────────────────────────────────────────────────────────
await confere('R4 rótulo único do botão que cria o pedido no modo API', () => {
  assert.equal(resumoPedido.ROTULO_GERAR_E_ENVIAR, 'Gerar cobrança e enviar')
  assert.equal(resumoPedido.LINHA_COMANDA_VAI_JUNTO, 'A comanda vai ao cliente junto com a cobrança.')
})
await confere('R5 antes do pedido o total é só dos itens', () => {
  assert.equal(resumoPedido.rotuloDoTotal({ pedidoId: null }, { fonteApi: true }), 'Total dos itens (o frete entra ao gerar a cobrança)')
  assert.equal(resumoPedido.rotuloDoTotal({ pedidoId: 'p' }, { fonteApi: true }), 'Total')
  assert.equal(resumoPedido.rotuloDoTotal({ pedidoId: null }, { fonteApi: false }), 'Total')
})

// ── R6 ─────────────────────────────────────────────────────────────────────
await confere('R6 pedido do EasyStok: entregador lê o mesmo número da Cozinha e das Entregas', () => {
  const pedido = comandaApi.pedidoDaApi(PEDIDO_PAGO)
  assert.equal(pedido.numero, '3F2A9B1C')
  assert.equal(numeroParaEntregador(pedido), '3F2A9B1C')
  assert.match(numeroParaEntregador({ numero: '2026-0186' }), /^#\d{4}$/)
})
await confere('R6 comanda sem pedido no EasyStok não mostra número inventado', () => {
  assert.equal(pedidoDom.numeroDaComanda({ pedidoId: null, numero: '2026-0001' }, { fonteApi: true }), null)
  assert.equal(pedidoDom.numeroDaComanda({ pedidoId: PEDIDO_ID, numero: '3F2A9B1C' }, { fonteApi: true }), '3F2A9B1C')
  assert.equal(pedidoDom.numeroDaComanda({ pedidoId: null, numero: '2026-0001' }, { fonteApi: false }), '0001')
})

// ── R7 ─────────────────────────────────────────────────────────────────────
await confere('R7 recusar pede confirmação com o que acontece', () => {
  assert.equal(entregasApi.confirmacaoDaRecusa('3F2A9B1C', 'Ana Souza'),
    'Recusar o pedido 3F2A9B1C de Ana Souza? O pedido é cancelado, o pagamento volta ao cliente e ele recebe aviso.')
})

// ── R8 ─────────────────────────────────────────────────────────────────────
await confere('R8 pedido esperando aprovação bloqueia a baixa com motivo', () => {
  const esperando = comandaApi.pedidoDaApi({ ...PEDIDO_PAGO, status: 'aguardando_aprovacao_baba' })
  assert.equal(cobranca.aguardaAprovacao(esperando), true)
  assert.equal(cobranca.aguardaAprovacao(comandaApi.pedidoDaApi(PEDIDO_PAGO)), false)
  assert.equal(cobranca.MOTIVO_BAIXA_SEM_APROVACAO, 'Aprove o pedido antes de registrar o pagamento.')
})
await confere('R8 registrar pagamento com pedido esperando aprovação não chama a API', async () => {
  chamadas.length = 0
  const { api } = montarComanda({ ...PEDIDO_PAGO, status: 'aguardando_aprovacao_baba' })
  const r = await api.confirmarPagamento('c1', 30, 'pix')
  assert.equal(chamadas.length, 0)
  assert.equal(r?.erro, cobranca.MOTIVO_BAIXA_SEM_APROVACAO)
})
await confere('R8 "Aprovar pedido" chama /api/storefront/pedidos/{id}/aprovar e relê o pedido', async () => {
  chamadas.length = 0
  respostaFetch = (c) => ({ data: c.metodo === 'POST' ? {} : { ...PEDIDO_PAGO, status: 'aprovado_baba' } })
  const { api, despachos } = montarComanda({ ...PEDIDO_PAGO, status: 'aguardando_aprovacao_baba' })
  await api.aprovarPedido('c1')
  assert.equal(chamadas[0]?.url, `/api/storefront/pedidos/${PEDIDO_ID}/aprovar`)
  assert.equal(despachos.find((d) => d.tipo === acao.SINCRONIZAR_PEDIDO)?.pedido.statusApi, 'aprovado_baba')
  respostaFetch = () => ({ data: null })
})
await confere('R8 link cancelado e pedido passou a esperar aprovação: o aviso diz o que fazer', async () => {
  const pendente = { ...PEDIDO_PAGO, status: 'aguardando_pagamento', totalPago: 0, cobranca: { ...PEDIDO_PAGO.cobranca, status: 'Pendente', pagaEm: null, valorPago: null } }
  respostaFetch = (c) => {
    if (c.url.endsWith('/pagamentos')) return { status: 400, corpo: { error: { message: 'Não é possível registrar pagamento: o pedido ainda não foi confirmado/aprovado.' } } }
    if (c.metodo === 'POST') return { data: {} }
    return { data: { ...pendente, status: 'aguardando_aprovacao_baba', cobranca: { ...pendente.cobranca, provedor: 'na_entrega', status: 'Pendente', linkPagamento: null } } }
  }
  const { api, despachos } = montarComanda(pendente)
  await api.confirmarPagamento('c1', 30, 'pix')
  const aviso = despachos.filter((d) => d.tipo === acao.AVISO_API).at(-1)?.mensagem ?? ''
  assert.match(aviso, /espera aprovação/)
  assert.match(aviso, /Aprovar pedido/)
  respostaFetch = () => ({ data: null })
})

// ── R9 ─────────────────────────────────────────────────────────────────────
await confere('R9 dica "Fora da área de entrega" some no modo API', () => {
  assert.equal(clienteDom.dicaDoCep(true, { fonteApi: false }), 'Fora da área de entrega')
  assert.equal(clienteDom.dicaDoCep(true, { fonteApi: true }), undefined)
  assert.equal(clienteDom.dicaDoCep(false, { fonteApi: false }), undefined)
})

// ── R11 ────────────────────────────────────────────────────────────────────
await confere('R11 prévia é a última mensagem do cliente ou da loja, nunca a nota de sistema', () => {
  const conversa = { mensagens: [
    { dir: 'in', texto: 'Quero uma lasanha' },
    { dir: 'sistema', texto: 'Escalado para a dona: agente desligado' },
  ] }
  assert.deepEqual(previaDaConversa(conversa), { prefixo: '', corpo: 'Quero uma lasanha' })
  const so = previaDaConversa({ mensagens: [{ dir: 'sistema', texto: 'x' }], ultimaMensagemTexto: null })
  assert.equal(so.prefixo, '')
  assert.equal(so.corpo, 'Sem mensagens')
})

if (falhas.length > 0) {
  console.error(`\n${passou} ok, ${falhas.length} falha(s)`)
  process.exit(1)
}
console.log(`\n${passou} ok, 0 falha(s)`)
