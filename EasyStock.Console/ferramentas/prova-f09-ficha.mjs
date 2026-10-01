/* eslint-disable no-console */
// Prova da F09 (issue #1239): no modo API a Ficha do cliente vem do dossiê do EasyStok (S25),
// tag, nota e bloqueio gravam na API (S24) e a ocorrência é a do EasyStok (S27).
// Exercita a tradução do dossiê e da ocorrência para o formato do console, o merge da polling
// no reducer de verdade (o cadastro carregado do dossiê não some no ciclo de 5 s), o erro do
// estorno recusado (502) em português e o aviso do lead sem cadastro.
//
//   node ferramentas/prova-f09-ficha.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  // `fonteDados.js` lê `import.meta.env` do Vite; no node a fonte é fixa.
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    // A massa (`repositorioConversas.js`, via `acoesApi.js`) é JSON importado sem atributo, como o Vite aceita.
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: `export default ${readFileSync(fileURLToPath(url), 'utf8')}` }
    }
    return proximo(url, contexto)
  },
})

const { bloquear, fichaDoDossie } = await import('../src/infra/api/fichaClienteApi.js')
const { ocorrenciaDaApi, resolverOcorrencia } = await import('../src/infra/api/ocorrenciasApi.js')
const { conversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { ESTADOS_OCORRENCIA, ocorrenciaDoPedido } = await import('../src/dominio/ocorrencia.js')

let passou = 0
const confere = async (descricao, fn) => { await fn(); passou += 1; console.log('ok  ' + descricao) }

const CLIENTE = '9a9a9a9a-0000-0000-0000-000000000001'
const CONVERSA = 'c0c0c0c0-0000-0000-0000-000000000001'
const OUTRA = 'c0c0c0c0-0000-0000-0000-000000000002'
const PEDIDO = 'abcdef12-3333-3333-3333-333333333333'

const dossie = (extra = {}) => ({
  cliente: { id: CLIENTE, nome: 'Ana Souza', telefone: '11987654321', email: 'ana@exemplo.test', observacoes: null, motivoBloqueio: null },
  enderecos: [{
    id: 'e1', clienteId: CLIENTE, tipo: null, logradouro: 'Rua das Flores', numero: '10', complemento: 'ap 2',
    bairro: 'Centro', cidade: 'São Paulo', estado: 'SP', cep: '01001000', pais: 'BR', referencia: null, padrao: true, criadoEm: '2026-09-01T10:00:00',
  }],
  tags: [{ tag: 'sem lactose', origem: 'console', criadoEm: '2026-09-20T10:00:00' }],
  notas: [{ id: 'n1', texto: 'Prefere al dente', autor: 'Thatiane', pedidoId: null, mensagemId: null, criadoEm: '2026-09-29T18:30:00' }],
  ultimosPedidos: [
    { id: PEDIDO, status: 'entregue', criadoEm: '2026-09-28T12:00:00', total: 93, itens: [{ nome: 'Lasanha', quantidade: 1 }, { nome: 'Parmesão', quantidade: 2 }] },
    { id: 'p2', status: 'cancelado', criadoEm: '2026-08-10T12:00:00', total: 85, itens: [{ nome: 'Lasanha', quantidade: 1 }] },
  ],
  itemFavorito: { nome: 'Lasanha', pedidos: 2, quantidade: 2 },
  ultimaCompraEm: '2026-09-28T12:00:00',
  totalPedidos: 2,
  bloqueado: false,
  preferencias: { avisosStatusAtivos: true, consentiuMarketing: false, consentimentoEm: null },
  domicilio: [],
  conversasRecentes: [
    { id: CONVERSA, canal: 'WhatsApp', situacao: 'Assumida', iniciadaEm: '2026-09-30T14:00:00', ultimaMensagemEm: '2026-09-30T14:20:00' },
    { id: OUTRA, canal: 'ChatSite', situacao: 'Encerrada', iniciadaEm: '2026-09-28T11:00:00', ultimaMensagemEm: '2026-09-28T11:45:00' },
  ],
  pedidoEmAndamento: null,
  ...extra,
})

const resumo = (id, clienteId) => ({
  id, canal: 'WhatsApp', situacao: 'Assumida', clienteId, contatoNome: 'Ana', contatoIdExterno: '5511987654321',
  assumidaPorUsuarioId: null, ultimaMensagemEm: '2026-09-30T14:20:00', dentroDaJanela: true, naoLidas: 0,
})

// --- Tradução do dossiê -----------------------------------------------------

await confere('dossiê vira o cliente da Ficha: tags, notas, telefone, endereço e contagem', () => {
  const { clienteId, cliente, bloqueio } = fichaDoDossie(dossie())
  assert.equal(clienteId, CLIENTE)
  assert.deepEqual(cliente.tags, ['sem lactose'])
  assert.equal(cliente.notas.length, 1)
  assert.equal(cliente.notas[0].texto, 'Prefere al dente')
  assert.equal(cliente.notas[0].autor, 'Thatiane')
  assert.match(cliente.notas[0].em, /^\d{2}\/\d{2}/, 'nota com data legível, como a Ficha lê (em.slice(0, 5))')
  assert.equal(cliente.telefone, '11987654321')
  assert.match(cliente.endereco, /Rua das Flores, 10/)
  assert.match(cliente.endereco, /01001-000/)
  assert.equal(cliente.pedidos, 2)
  assert.equal(cliente.dossie, true, 'marca de que veio do dossiê, para a polling preservar')
  assert.equal(bloqueio, null)
})

await confere('Histórico: pedidos do dossiê no formato da modal (itens, estado, total)', () => {
  const { cliente } = fichaDoDossie(dossie())
  assert.equal(cliente.historico.length, 2)
  assert.deepEqual(cliente.historico[0].itens, ['1× Lasanha', '2× Parmesão'])
  assert.equal(cliente.historico[0].estado, 'entregue')
  assert.equal(cliente.historico[0].total, 93)
  assert.equal(cliente.historico[1].estado, 'cancelado')
  assert.ok(cliente.desde, 'todos os pedidos vieram: dá para dizer desde quando é cliente')
})

await confere('Histórico: atendimentos do dossiê (conversas recentes) com canal e situação', () => {
  const { cliente } = fichaDoDossie(dossie())
  assert.equal(cliente.atendimentos.length, 2)
  assert.equal(cliente.atendimentos[0].daApi, true)
  assert.match(cliente.atendimentos[1].resumoApi, /Chat do site/)
  assert.match(cliente.atendimentos[1].resumoApi, /Encerrad/)
})

await confere('bloqueio do dossiê vira o bloqueio do cadastro, com o motivo', () => {
  const { bloqueio } = fichaDoDossie(dossie({
    bloqueado: true,
    cliente: { ...dossie().cliente, motivoBloqueio: 'Golpe do Pix' },
  }))
  assert.equal(bloqueio.motivo, 'Golpe do Pix')
  assert.equal(bloqueio.alcance, 'todos os canais')
})

await confere('dossiê mínimo de lead (sem cliente): sem clienteId, sem histórico', () => {
  const { clienteId, cliente } = fichaDoDossie(dossie({
    cliente: { id: null, nome: 'Visitante', telefone: null, email: null, observacoes: null, motivoBloqueio: null },
    enderecos: [], tags: [], notas: [], ultimosPedidos: [], totalPedidos: 0, conversasRecentes: [],
  }))
  assert.equal(clienteId, null)
  assert.equal(cliente.pedidos, 0)
  assert.deepEqual(cliente.tags, [])
})

// --- Merge da polling -------------------------------------------------------

const comConversas = (conversas) => ({ ...estadoInicial, conversas, selecionadaId: conversas[0]?.id ?? null })
const sincronizar = (estado, resumos) => reducer(estado, {
  tipo: acao.SINCRONIZAR_CONVERSAS, conversas: resumos.map((r) => conversaDaApi(r, [], null)),
})

await confere('tag e nota carregadas do dossiê não somem no ciclo de 5 s', () => {
  let estado = sincronizar(comConversas([]), [resumo(CONVERSA, CLIENTE)])
  estado = reducer(estado, { tipo: acao.APLICAR_FICHA_API, id: CONVERSA, ficha: fichaDoDossie(dossie()) })
  estado = sincronizar(estado, [resumo(CONVERSA, CLIENTE)])
  const c = estado.conversas.find((x) => x.id === CONVERSA)
  assert.deepEqual(c.cliente.tags, ['sem lactose'])
  assert.equal(c.cliente.notas[0].texto, 'Prefere al dente')
  assert.equal(c.cliente.historico.length, 2)
})

await confere('cadastro do dossiê sai quando o vínculo da conversa muda de cliente', () => {
  let estado = sincronizar(comConversas([]), [resumo(CONVERSA, CLIENTE)])
  estado = reducer(estado, { tipo: acao.APLICAR_FICHA_API, id: CONVERSA, ficha: fichaDoDossie(dossie()) })
  estado = sincronizar(estado, [resumo(CONVERSA, '9a9a9a9a-0000-0000-0000-00000000000f')])
  const c = estado.conversas.find((x) => x.id === CONVERSA)
  assert.deepEqual(c.cliente.tags, [])
})

await confere('bloqueio do dossiê vale para todas as conversas do mesmo cliente e sobrevive à polling', () => {
  let estado = sincronizar(comConversas([]), [resumo(CONVERSA, CLIENTE), resumo(OUTRA, CLIENTE)])
  const bloqueado = dossie({ bloqueado: true, cliente: { ...dossie().cliente, motivoBloqueio: 'Golpe' } })
  estado = reducer(estado, { tipo: acao.APLICAR_FICHA_API, id: CONVERSA, ficha: fichaDoDossie(bloqueado) })
  estado = sincronizar(estado, [resumo(CONVERSA, CLIENTE), resumo(OUTRA, CLIENTE)])
  for (const c of estado.conversas) assert.equal(c.bloqueio?.motivo, 'Golpe', `conversa ${c.id}`)
  estado = reducer(estado, { tipo: acao.APLICAR_FICHA_API, id: CONVERSA, ficha: fichaDoDossie(dossie()) })
  for (const c of estado.conversas) assert.equal(c.bloqueio, null, `desbloqueio na conversa ${c.id}`)
})

// --- Ocorrência -------------------------------------------------------------

const ocorrenciaApi = (extra = {}) => ({
  id: 'o1', pedidoId: PEDIDO, clienteId: CLIENTE, conversaId: CONVERSA, origem: 'dona', categoria: 'produto_improprio',
  relato: 'Lasanha chegou fria', status: 'aberta', resolucao: null, reembolsoValor: null, reembolsoIdSolicitacao: null,
  reembolsoEm: null, criadaEm: '2026-09-30T14:00:00', resolvidaEm: null, resolvidaPorUsuarioId: null, ...extra,
})

await confere('ocorrência aberta na API aparece pronta para resolver (em apuração), com o relato', () => {
  const o = ocorrenciaDaApi(ocorrenciaApi())
  assert.equal(o.apiId, 'o1')
  assert.equal(o.pedidoId, PEDIDO)
  assert.equal(o.estado, ESTADOS_OCORRENCIA.EM_APURACAO)
  assert.equal(o.historico[0].texto, 'Lasanha chegou fria')
  assert.equal(o.historico[0].autor, 'dona')
})

await confere('ocorrência resolvida com reembolso e sem reembolso', () => {
  const com = ocorrenciaDaApi(ocorrenciaApi({
    status: 'resolvida', resolucao: 'Devolvido', reembolsoValor: 93, resolvidaEm: '2026-09-30T15:00:00',
  }))
  assert.equal(com.estado, ESTADOS_OCORRENCIA.ENCERRADA_COM_ESTORNO)
  assert.equal(com.estorno.valor, 93)
  assert.equal(com.estorno.motivo, 'Devolvido')
  const sem = ocorrenciaDaApi(ocorrenciaApi({ status: 'resolvida', resolucao: 'Gosto pessoal', resolvidaEm: '2026-09-30T15:00:00' }))
  assert.equal(sem.estado, ESTADOS_OCORRENCIA.ENCERRADA_SEM_ESTORNO)
  assert.equal(sem.notaInterna.texto, 'Gosto pessoal')
})

await confere('ocorrência do pedido: a aberta antes da resolvida', () => {
  const lista = [
    ocorrenciaDaApi(ocorrenciaApi({ id: 'velha', status: 'resolvida', resolucao: 'ok', resolvidaEm: '2026-09-30T15:00:00' })),
    ocorrenciaDaApi(ocorrenciaApi({ id: 'nova' })),
    ocorrenciaDaApi(ocorrenciaApi({ id: 'outro-pedido', pedidoId: 'p2' })),
  ]
  assert.equal(ocorrenciaDoPedido(lista, PEDIDO).apiId, 'nova')
  assert.equal(ocorrenciaDoPedido(lista, 'nenhum'), null)
})

await confere('estorno recusado (502) vira erro em português e diz que a ocorrência continua aberta', async () => {
  globalThis.sessionStorage = { getItem: () => null, setItem() {}, removeItem() {} }
  globalThis.fetch = async () => new Response(JSON.stringify({
    data: { ocorrencia: ocorrenciaApi(), reembolso: { situacao: 'Falhou', codigo: 'cc_rejected_other_reason', valor: 93, idSolicitacao: null } },
    error: 'cc_rejected_other_reason',
  }), { status: 502, headers: { 'Content-Type': 'application/json' } })
  await assert.rejects(
    () => resolverOcorrencia('o1', { resolucao: 'Chegou fria', reembolsar: true, valor: 93 }),
    (erro) => {
      assert.equal(erro.status, 502)
      assert.equal(erro.codigo, 'cc_rejected_other_reason')
      assert.match(erro.message, /estorno/i)
      assert.match(erro.message, /continua aberta/)
      assert.doesNotMatch(erro.message, /canal não respondeu/)
      return true
    },
  )
})

await confere('bloqueio sem permissão (403, policy Gerente) diz o motivo em vez de quebrar', async () => {
  globalThis.fetch = async () => new Response('', { status: 403 })
  await assert.rejects(() => bloquear(CLIENTE, 'Golpe'), (erro) => {
    assert.equal(erro.status, 403)
    assert.match(erro.message, /gerente/)
    return true
  })
})

// --- Lead sem cadastro ------------------------------------------------------

await confere('lead sem clienteId: tag, nota e bloqueio avisam para cadastrar e não mudam nada', () => {
  const despachados = []
  const lead = { ...conversaDaApi(resumo(CONVERSA, null), [], null) }
  const estadoRef = { current: { conversas: [lead] } }
  const acoes = comApi({}, { despachar: (a) => despachados.push(a), agoraRef: { current: Date.now() }, estadoRef })
  acoes.adicionarTag(CONVERSA, 'vegano')
  acoes.salvarNota(CONVERSA, 'Ligar antes')
  acoes.bloquearCliente(lead.nome, 'Golpe')
  assert.equal(despachados.length, 3)
  for (const a of despachados) {
    assert.equal(a.tipo, acao.AVISO_API)
    assert.match(a.mensagem, /Cadastre o cliente/)
  }
})

console.log(`\n${passou} verificações da F09 passaram.`)
