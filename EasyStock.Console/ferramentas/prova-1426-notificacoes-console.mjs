// Prova da #1426: no modo API o sininho fala com o EasyStok e o aparelho se inscreve no Web Push.
//   1. cliente HTTP: rotas, método, corpo e empresa de notificações, lembretes e push;
//   2. tradução: horário UTC sem fuso, só o lembrete manual do servidor entra;
//   3. reducer: cada fonte da API troca por inteiro, a falha de uma não apaga a outra,
//      a sincronização dos automáticos locais não apaga o que veio da API;
//   4. ações: programar grava antes de mostrar, concluir chama a rota da fonte, falha avisa;
//   5. avisos no aparelho: permissão só no clique e antes de qualquer espera, sem VAPID,
//      bloqueado, sem suporte, erro, e a reinscrição ao abrir.
//
//   node ferramentas/prova-1426-notificacoes-console.mjs
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

const EMPRESA = '11111111-1111-1111-1111-111111111111'
const CONVERSA = '22222222-2222-2222-2222-222222222222'
globalThis.sessionStorage = {
  getItem: () => JSON.stringify({ token: 'tk', expiraEm: Date.now() + 60000, empresa: { id: EMPRESA } }),
}

const chamadas = []
let rotas = {}
globalThis.fetch = async (url, opcoes = {}) => {
  const metodo = opcoes.method ?? 'GET'
  chamadas.push({ url, metodo, corpo: opcoes.body ? JSON.parse(opcoes.body) : undefined, auth: opcoes.headers?.Authorization })
  const chave = `${metodo} ${url.split('?')[0]}`
  const [status, corpo] = rotas[chave] ?? [200, { data: null }]
  return new Response(corpo === null ? null : JSON.stringify(corpo), { status })
}
const responder = (novas) => { rotas = novas; chamadas.length = 0 }

const acao = await import('../src/aplicacao/acoes.js')
const api = await import('../src/infra/api/notificacoesApi.js')
const { lembreteDaApi, avisoDaApi, lembreteEhManual } = await import('../src/infra/api/traducaoNotificacoes.js')
const { FONTES, ORIGENS, novoLembrete } = await import('../src/dominio/lembrete.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const { criarAcoes } = await import('../src/aplicacao/criarAcoes.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { lerLembretesDaApi } = await import('../src/aplicacao/api/lembretes.js')
const { NAO_LIGADAS } = await import('../src/aplicacao/api/naoLigadas.js')
const { ESTADOS_AVISO, ativarAvisosNoAparelho, conferirAvisosNoAparelho } = await import('../src/aplicacao/avisosNoAparelho.js')
const { chaveDeBase64Url } = await import('../src/infra/pushNavegador.js')

const esvaziar = async () => { for (let i = 0; i < 5; i += 1) await new Promise((r) => setTimeout(r, 0)) }

// 1. Cliente HTTP.
{
  responder({})
  await api.listarNotificacoesNaoLidas()
  await api.marcarNotificacaoLida('n1')
  await api.listarLembretes()
  await api.criarLembreteNaApi({ texto: 'Ligar pro motoboy', venceEm: '2026-10-07T18:00:00.000Z', conversaId: CONVERSA })
  await api.concluirLembreteNaApi('l1')
  await api.inscreverPush({ endpoint: 'https://push/x', p256dh: 'p', auth: 'a', userAgent: 'ua' })
  assert.deepEqual(chamadas.map((c) => `${c.metodo} ${c.url}`), [
    `GET /api/notificacoes?empresaId=${EMPRESA}&lida=false&pageSize=20`,
    `PATCH /api/notificacoes/n1/lida?empresaId=${EMPRESA}`,
    'GET /api/atendimento/lembretes',
    'POST /api/atendimento/lembretes',
    'POST /api/atendimento/lembretes/l1/concluir',
    'POST /api/pwa/push/subscribe',
  ])
  assert.ok(chamadas.every((c) => c.auth === 'Bearer tk'), 'todas autenticadas')
  assert.deepEqual(chamadas[3].corpo, { texto: 'Ligar pro motoboy', venceEm: '2026-10-07T18:00:00.000Z', conversaId: CONVERSA })
  assert.deepEqual(chamadas[5].corpo, { endpoint: 'https://push/x', p256dh: 'p', auth: 'a', userAgent: 'ua' })

  responder({ 'GET /api/pwa/push/vapid-public': [200, { data: { publicKey: 'BAA', subject: 'mailto:x' } }] })
  assert.equal((await api.obterChavePush()).publicKey, 'BAA')
  assert.equal(chamadas[0].auth, undefined, 'a chave pública é anônima')
}

// 2. Tradução.
{
  const lembrete = lembreteDaApi({ id: 'l1', tipo: 'Manual', texto: 'Ligar', venceEm: '2026-10-07T18:00:00', conversaId: null })
  assert.equal(lembrete.quando, Date.parse('2026-10-07T18:00:00Z'), 'DateTime sem fuso é UTC')
  assert.equal(lembrete.fonte, FONTES.LEMBRETE)
  assert.equal(lembrete.servidorId, 'l1')
  assert.equal(lembrete.origem, ORIGENS.MANUAL)
  assert.equal(lembreteDaApi({ id: 'l2', tipo: 'Manual', texto: 'x', venceEm: '2026-10-07T18:00:00-03:00' }).quando,
    Date.parse('2026-10-07T21:00:00Z'), 'com fuso respeita o fuso')
  assert.equal(lembreteEhManual({ tipo: 'PagamentoSemBaixa' }), false)
  const aviso = avisoDaApi({ id: 'n1', titulo: 'Estoque baixo', mensagem: 'Farinha acabando', createdAt: '2026-10-07T12:00:00+00:00' })
  assert.deepEqual([aviso.titulo, aviso.detalhe, aviso.origem, aviso.fonte], ['Estoque baixo', 'Farinha acabando', ORIGENS.AVISO, FONTES.AVISO])
  assert.equal(avisoDaApi({ id: 'n2', titulo: null, mensagem: 'Só a mensagem', createdAt: '2026-10-07T12:00:00Z' }).titulo, 'Só a mensagem')
}

// 3. Reducer.
const vazio = () => estadoInicial({ conversas: [], catalogo: { cardapio: [], janelas: [], canais: [] }, regras: [] })
{
  const doServidor = lembreteDaApi({ id: 'l1', tipo: 'Manual', texto: 'Ligar', venceEm: '2026-10-07T18:00:00Z' })
  const aviso = avisoDaApi({ id: 'n1', titulo: 'Aviso', mensagem: 'm', createdAt: '2026-10-07T12:00:00Z' })
  const automatico = novoLembrete({ id: 'auto-pagamento-c1', titulo: 'Confirmar', quando: 1, conversaId: 'c1', origem: ORIGENS.PAGAMENTO })

  let estado = reducer(vazio(), { tipo: acao.SINCRONIZAR_LEMBRETES_API, lembretes: [doServidor], avisos: [aviso] })
  assert.deepEqual(estado.lembretes.map((l) => l.id), ['api-lembrete-l1', 'api-aviso-n1'])

  estado = reducer(estado, { tipo: acao.SINCRONIZAR_LEMBRETES, automaticos: [automatico] })
  assert.deepEqual(estado.lembretes.map((l) => l.id).sort(), ['api-aviso-n1', 'api-lembrete-l1', 'auto-pagamento-c1'],
    'os automáticos locais não apagam o que veio da API')

  const antes = estado
  estado = reducer(estado, { tipo: acao.SINCRONIZAR_LEMBRETES_API, lembretes: [{ ...doServidor }], avisos: [{ ...aviso }] })
  assert.equal(estado, antes, 'a mesma leitura não troca o estado')

  estado = reducer(estado, { tipo: acao.SINCRONIZAR_LEMBRETES_API, lembretes: undefined, avisos: [] })
  assert.deepEqual(estado.lembretes.map((l) => l.id).sort(), ['api-lembrete-l1', 'auto-pagamento-c1'],
    'aviso lido sai; a fonte que falhou (lembretes) fica como estava')

  estado = reducer(estado, { tipo: acao.CONCLUIR_LEMBRETE, lembreteId: 'api-lembrete-l1', titulo: 'Ligar', conversaId: null })
  estado = reducer(estado, { tipo: acao.SINCRONIZAR_LEMBRETES_API, lembretes: [doServidor] })
  assert.ok(!estado.lembretes.some((l) => l.id === 'api-lembrete-l1'), 'concluído não volta na leitura seguinte')
}

// 4. Ações do modo API.
const despachos = []
const despachar = (a) => despachos.push(a)
const locais = criarAcoes({
  despachar, agoraRef: { current: Date.now() }, estadoRef: { current: { conversas: [] } }, pendentes: { current: [] },
  consultarAgente: async () => {}, perguntarAssistente: async () => '', pedirNotificacaoDoNavegador: async () => {},
})
const acoes = comApi(locais, { despachar, agoraRef: { current: Date.now() }, estadoRef: { current: { conversas: [] } } })
{
  assert.equal('criarLembrete' in NAO_LIGADAS, false)
  assert.equal('concluirLembrete' in NAO_LIGADAS, false)
  assert.notEqual(acoes.criarLembrete, locais.criarLembrete)

  responder({
    'GET /api/atendimento/lembretes': [200, { data: [
      { id: 'l1', tipo: 'Manual', texto: 'Ligar', venceEm: '2026-10-07T18:00:00Z' },
      { id: 'l2', tipo: 'PagamentoSemBaixa', texto: 'Pagamento', venceEm: '2026-10-07T18:00:00Z' },
    ] }],
    'GET /api/notificacoes': [200, { data: [{ id: 'n1', titulo: 'Aviso', mensagem: 'm', createdAt: '2026-10-07T12:00:00Z' }], meta: {} }],
  })
  despachos.length = 0
  await lerLembretesDaApi(despachar)
  assert.equal(despachos[0].tipo, acao.SINCRONIZAR_LEMBRETES_API)
  assert.deepEqual(despachos[0].lembretes.map((l) => l.servidorId), ['l1'], 'automático do servidor não duplica o local')
  assert.deepEqual(despachos[0].avisos.map((l) => l.servidorId), ['n1'])

  responder({ 'GET /api/atendimento/lembretes': [500, { error: { code: 'X', message: 'caiu' } }] })
  despachos.length = 0
  await lerLembretesDaApi(despachar)
  assert.equal(despachos[0].lembretes, undefined, 'falha numa fonte não esvazia a lista')
  assert.ok(Array.isArray(despachos[0].avisos))

  const venceEm = Date.parse('2026-10-07T18:00:00Z')
  responder({ 'POST /api/atendimento/lembretes': [200, { data: { id: 'l9', tipo: 'Manual', texto: 'Ligar', venceEm: '2026-10-07T18:00:00Z', conversaId: CONVERSA } }] })
  despachos.length = 0
  await acoes.criarLembrete(novoLembrete({ titulo: 'Ligar', quando: venceEm, conversaId: CONVERSA }))
  assert.deepEqual(chamadas[0].corpo, { texto: 'Ligar', venceEm: '2026-10-07T18:00:00.000Z', conversaId: CONVERSA })
  assert.deepEqual(despachos.map((d) => d.tipo), [acao.CRIAR_LEMBRETE])
  assert.equal(despachos[0].lembrete.servidorId, 'l9', 'o item da tela é o que a API gravou')

  responder({ 'POST /api/atendimento/lembretes': [200, { data: { id: 'l10', tipo: 'Manual', texto: 'x', venceEm: '2026-10-07T18:00:00Z' } }] })
  await acoes.criarLembrete(novoLembrete({ titulo: 'x', quando: venceEm, conversaId: 'c1' }))
  assert.equal(chamadas[0].corpo.conversaId, null, 'id que não é do servidor não vai à API')

  responder({ 'POST /api/atendimento/lembretes': [400, { error: { code: 'BAD_REQUEST', message: 'Texto obrigatório.' } }] })
  despachos.length = 0
  await acoes.criarLembrete(novoLembrete({ titulo: '', quando: venceEm }))
  assert.deepEqual(despachos.map((d) => d.tipo), [acao.AVISO_API], 'falha não cria item falso')
  assert.match(despachos[0].mensagem, /Lembrete não gravado: Texto obrigatório/)

  const aviso = avisoDaApi({ id: 'n1', titulo: 'Aviso', mensagem: 'm', createdAt: '2026-10-07T12:00:00Z' })
  responder({ 'PATCH /api/notificacoes/n1/lida': [204, null] })
  despachos.length = 0
  await acoes.concluirLembrete(aviso)
  assert.deepEqual(chamadas.map((c) => `${c.metodo} ${c.url.split('?')[0]}`), ['PATCH /api/notificacoes/n1/lida'])
  assert.deepEqual(despachos.map((d) => [d.tipo, d.lembreteId, d.conversaId]), [[acao.CONCLUIR_LEMBRETE, 'api-aviso-n1', null]])

  const lembrete = lembreteDaApi({ id: 'l1', tipo: 'Manual', texto: 'Ligar', venceEm: '2026-10-07T18:00:00Z', conversaId: CONVERSA })
  responder({})
  despachos.length = 0
  await acoes.concluirLembrete(lembrete)
  assert.deepEqual(chamadas.map((c) => `${c.metodo} ${c.url}`), ['POST /api/atendimento/lembretes/l1/concluir'])
  assert.equal(despachos[0].conversaId, null, 'sem mensagem de sistema só do navegador')

  responder({ 'POST /api/atendimento/lembretes/l1/concluir': [404, { error: { code: 'NOT_FOUND', message: 'Lembrete não encontrado.' } }] })
  despachos.length = 0
  await acoes.concluirLembrete(lembrete)
  assert.deepEqual(despachos.map((d) => d.tipo), [acao.AVISO_API], 'falha não tira o item')

  responder({})
  despachos.length = 0
  await acoes.concluirLembrete(novoLembrete({ id: 'auto-passagem-c1', titulo: 'Responder', quando: 1, conversaId: 'c1', origem: ORIGENS.PASSAGEM }))
  assert.equal(chamadas.length, 0, 'automático da tela não chama a API')
  assert.deepEqual(despachos.map((d) => d.tipo), [acao.CONCLUIR_LEMBRETE])
  await esvaziar()
}

// 5. Avisos no aparelho.
function falsos({ suportado = true, permissao = 'default', resposta = 'granted', inscricao = null, chave = 'BAA', erroChave = null, erroInscrever = null } = {}) {
  const passos = []
  const navegador = {
    suportado: () => suportado,
    permissao: () => permissao,
    pedirPermissao: () => { passos.push('permissao'); return Promise.resolve(resposta) },
    inscricaoAtual: async () => { passos.push('atual'); return inscricao },
    inscrever: async (k) => {
      passos.push(`inscrever:${k}`)
      if (erroInscrever) throw new Error(erroInscrever)
      return { endpoint: 'https://push/x', p256dh: 'p', auth: 'a', userAgent: 'ua' }
    },
  }
  const apiFalsa = {
    obterChavePush: async () => { passos.push('chave'); if (erroChave) throw erroChave; return chave ? { publicKey: chave } : null },
    inscreverPush: async (i) => { passos.push(`gravar:${i.endpoint}`) },
  }
  return { navegador, api: apiFalsa, passos }
}
{
  const ok = falsos()
  const pendente = ativarAvisosNoAparelho(ok)
  assert.deepEqual(ok.passos, ['permissao'], 'a permissão sai no mesmo gesto, antes de qualquer espera')
  assert.deepEqual(await pendente, { estado: ESTADOS_AVISO.ATIVO, mensagem: null })
  assert.deepEqual(ok.passos, ['permissao', 'chave', 'inscrever:BAA', 'gravar:https://push/x'])

  const negado = falsos({ resposta: 'denied' })
  assert.equal((await ativarAvisosNoAparelho(negado)).estado, ESTADOS_AVISO.BLOQUEADO)
  assert.deepEqual(negado.passos, ['permissao'], 'negado não chama a API')

  assert.equal((await ativarAvisosNoAparelho(falsos({ resposta: 'default' }))).estado, ESTADOS_AVISO.DESLIGADO)

  const semSuporte = falsos({ suportado: false })
  assert.equal((await ativarAvisosNoAparelho(semSuporte)).estado, ESTADOS_AVISO.INDISPONIVEL)
  assert.deepEqual(semSuporte.passos, [])

  const { ErroApi } = await import('../src/infra/api/cliente.js')
  const semVapid = falsos({ erroChave: new ErroApi(404, 'NOT_FOUND', 'WebPush nao configurado neste ambiente.') })
  assert.equal((await ativarAvisosNoAparelho(semVapid)).estado, ESTADOS_AVISO.SEM_CHAVE)
  assert.ok(!semVapid.passos.some((p) => p.startsWith('inscrever')), 'sem VAPID não assina')

  const falhou = await ativarAvisosNoAparelho(falsos({ erroInscrever: 'Registration failed - push service error' }))
  assert.deepEqual(falhou, { estado: ESTADOS_AVISO.ERRO, mensagem: 'Registration failed - push service error' })

  const reabriu = falsos({ permissao: 'granted', inscricao: { endpoint: 'https://push/antigo', p256dh: 'p', auth: 'a' } })
  assert.equal((await conferirAvisosNoAparelho(reabriu)).estado, ESTADOS_AVISO.ATIVO)
  assert.deepEqual(reabriu.passos, ['atual', 'gravar:https://push/antigo'], 'reabrir regrava a inscrição para o usuário atual')

  const nunca = falsos({ permissao: 'default' })
  assert.equal((await conferirAvisosNoAparelho(nunca)).estado, ESTADOS_AVISO.DESLIGADO)
  assert.deepEqual(nunca.passos, [], 'abrir o console nunca pede permissão sozinho')

  assert.equal((await conferirAvisosNoAparelho(falsos({ permissao: 'granted' }))).estado, ESTADOS_AVISO.DESLIGADO)
  assert.equal((await conferirAvisosNoAparelho(falsos({ permissao: 'denied' }))).estado, ESTADOS_AVISO.BLOQUEADO)

  assert.deepEqual([...chaveDeBase64Url('AQID_-8')], [1, 2, 3, 255, 239], 'chave VAPID base64url vira bytes')
}

console.log('Sininho no modo API (#1426): cliente, tradução, reducer, ações e avisos no aparelho verificados.')
