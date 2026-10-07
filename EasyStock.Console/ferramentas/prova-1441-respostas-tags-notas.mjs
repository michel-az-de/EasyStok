/* eslint-disable no-console */
// Prova da issue #1441 (homologação de 07/10): no modo API
//   - o seletor rápido de respostas ("/" no campo) só lista respostas prontas, com busca e setas;
//   - respostas prontas e mensagens automáticas leem e gravam no EasyStok;
//   - tags do cliente vão ao cadastro pela API;
//   - notas internas entram na linha do tempo da conversa como post-it;
//   - o que o automático (Sistema) mandou aparece marcado e listado no histórico.
//
//   node ferramentas/prova-1441-respostas-tags-notas.mjs

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
const { NAO_LIGADAS } = await import('../src/aplicacao/api/naoLigadas.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const respostas = await import('../src/dominio/respostas.js')
const automacao = await import('../src/dominio/automacao.js')
const notas = await import('../src/dominio/notas.js')
const traducao = await import('../src/infra/api/traducaoRespostas.js')
const { clienteDoDossie } = await import('../src/infra/api/traducaoCliente.js')
const { mensagemDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { criarAcoesRespostasApi } = await import('../src/aplicacao/api/respostas.js')
const { criarAcoesTagsApi } = await import('../src/aplicacao/api/tags.js')

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

const esvaziar = async () => { for (let i = 0; i < 8; i += 1) await new Promise((r) => setTimeout(r, 0)) }

// ── 1. Seletor rápido ────────────────────────────────────────────────────────
const PRONTAS = [
  { id: 'r1', titulo: 'Horário de entrega', atalho: '/entrega', texto: 'Entregamos das 11h às 20h.', arquivada: false },
  { id: 'r2', titulo: 'Cardápio do dia', atalho: '/cardapio', texto: 'Segue o cardápio, {nome}.', arquivada: false },
  { id: 'r3', titulo: 'Pix', atalho: '/pix', texto: 'A chave Pix da entrega é o CNPJ.', arquivada: false },
  { id: 'r4', titulo: 'Antiga', atalho: '/antiga', texto: 'Não usar.', arquivada: true },
]

await confere('seletor lista só respostas prontas ativas, sem automáticas', () => {
  const itens = respostas.respostasDoSeletor({ respostasProntas: PRONTAS, termo: '' })
  assert.deepEqual(itens.map((i) => i.id), ['r2', 'r1', 'r3'])
})

await confere('seletor põe atalho que começa com o termo antes de quem só contém', () => {
  const itens = respostas.respostasDoSeletor({ respostasProntas: PRONTAS, termo: 'entrega' })
  assert.equal(itens[0].id, 'r1')
  assert.ok(itens.every((i) => i.id !== 'r4'))
  const pix = respostas.respostasDoSeletor({ respostasProntas: PRONTAS, termo: 'pix' })
  assert.equal(pix[0].id, 'r3')
})

await confere('seletor acha por texto e ignora acento', () => {
  const itens = respostas.respostasDoSeletor({ respostasProntas: PRONTAS, termo: 'cardapio' })
  assert.equal(itens[0].id, 'r2')
})

await confere('termo da barra: só "/" no começo do campo, sem espaço', () => {
  assert.equal(respostas.termoDaBarra('/'), '')
  assert.equal(respostas.termoDaBarra('/ent'), 'ent')
  assert.equal(respostas.termoDaBarra('oi /ent'), null)
  assert.equal(respostas.termoDaBarra('/ent rega'), null)
  assert.equal(respostas.termoDaBarra(''), null)
})

await confere('setas andam em volta da lista', () => {
  assert.equal(respostas.moverDestaque(0, 1, 3), 1)
  assert.equal(respostas.moverDestaque(2, 1, 3), 0)
  assert.equal(respostas.moverDestaque(0, -1, 3), 2)
  assert.equal(respostas.moverDestaque(0, 1, 0), 0)
})

// ── 2. Tradução da API ───────────────────────────────────────────────────────
await confere('resposta pronta da API vira item do catálogo com atalho "/"', () => {
  const r = traducao.respostaDaApi({ id: 'g1', titulo: 'Pix', atalho: 'pix', texto: 'Chave', arquivada: false, alteradaEm: '2026-10-07T12:00:00' })
  assert.equal(r.id, 'g1')
  assert.equal(r.atalho, '/pix')
  assert.equal(r.titulo, 'Pix')
  assert.equal(r.arquivada, false)
  assert.equal(traducao.corpoDaResposta({ titulo: ' Pix ', atalho: '/pix', texto: ' Chave ' }).atalho, 'pix')
})

await confere('automática da API vira regra com id, gatilho e descrição do EasyStok', () => {
  const r = traducao.regraDaApi({ gatilho: 'PagamentoConfirmado', ligada: true, texto: 'Pago, {nome}!', alteradaEm: null })
  assert.equal(r.id, 'recibo')
  assert.equal(r.gatilho, automacao.GATILHOS.PAGAMENTO_CONFIRMADO)
  assert.equal(r.gatilhoApi, 'PagamentoConfirmado')
  assert.equal(r.ativa, true)
  assert.equal(r.texto, 'Pago, {nome}!')
  assert.ok(r.descricao.length > 10)
})

await confere('automática sem regra na API vem desligada, sem texto e com sugestão', () => {
  const r = traducao.regraDaApi({ gatilho: 'Encerramento', ligada: false, texto: null })
  assert.equal(r.ativa, false)
  assert.equal(r.texto, '')
  assert.ok(r.sugestao)
})

await confere('variável que o EasyStok não preenche é apontada', () => {
  assert.deepEqual(automacao.variaveisForaDaApi('Oi {nome}, voltamos {abre}. {linkCardapio}'), ['abre', 'linkCardapio'])
  assert.deepEqual(automacao.variaveisForaDaApi('Pedido {pedido} entre {faixa}'), [])
})

// ── 3. Ações de respostas e automáticas ──────────────────────────────────────
function montarFetch(rotas) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null })
    for (const [teste, resposta] of rotas) {
      if (teste(method, url)) {
        const { status = 200, data = null, error } = resposta
        return new Response(status === 204 ? null : JSON.stringify(error ? { error } : { data }), { status })
      }
    }
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  return chamadas
}

const LISTA_API = [{ id: 'g1', titulo: 'Pix', atalho: 'pix', texto: 'Chave', arquivada: false, alteradaEm: '2026-10-07T12:00:00' }]
const AUTOMACOES_API = [
  { gatilho: 'PrimeiroContato', ligada: true, texto: 'Oi {nome}!', alteradaEm: null },
  { gatilho: 'PagamentoConfirmado', ligada: false, texto: 'Pago!', alteradaEm: null },
  { gatilho: 'Encerramento', ligada: false, texto: null, alteradaEm: null },
]
const rotasRespostas = (extra = []) => [
  ...extra,
  [(m, u) => m === 'GET' && u.includes('/api/atendimento/respostas-prontas'), { data: LISTA_API }],
  [(m, u) => m === 'GET' && u.includes('/api/atendimento/automacoes'), { data: AUTOMACOES_API }],
]

function montarRespostas(rotas, estado = {}) {
  const chamadas = montarFetch(rotas)
  const despachados = []
  const estadoRef = {
    current: {
      catalogo: { respostasProntas: traducao.respostaDaApi(LISTA_API[0]) ? [traducao.respostaDaApi(LISTA_API[0])] : [] },
      regras: AUTOMACOES_API.map(traducao.regraDaApi),
      ...estado,
    },
  }
  const acoes = criarAcoesRespostasApi({ despachar: (a) => despachados.push(a), estadoRef })
  return { acoes, chamadas, despachados }
}

await confere('recarregarRespostas lê as duas listas e despacha para o estado', async () => {
  const { acoes, chamadas, despachados } = montarRespostas(rotasRespostas())
  await acoes.recarregarRespostas()
  assert.ok(chamadas.some((c) => c.url.includes('/api/atendimento/respostas-prontas?arquivadas=true')))
  assert.ok(chamadas.some((c) => c.url.endsWith('/api/atendimento/automacoes')))
  const r = despachados.find((a) => a.tipo === acao.RESPOSTAS_DA_API)
  const g = despachados.find((a) => a.tipo === acao.REGRAS_DA_API)
  assert.ok(r && g, 'não despachou as duas listas')
  assert.equal(r.respostas[0].atalho, '/pix')
  assert.equal(g.regras.find((x) => x.gatilhoApi === 'PrimeiroContato').id, 'boas-vindas')
})

await confere('incluir resposta faz POST com título, atalho e texto e relê', async () => {
  const { acoes, chamadas } = montarRespostas(rotasRespostas([[(m) => m === 'POST', { data: LISTA_API[0] }]]))
  await acoes.incluirRespostaPronta({ titulo: 'Pix', atalho: '/pix', texto: 'Chave' })
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.match(post.url, /\/api\/atendimento\/respostas-prontas$/)
  assert.deepEqual(post.corpo, { titulo: 'Pix', atalho: 'pix', texto: 'Chave' })
  assert.ok(chamadas.some((c) => c.metodo === 'GET'), 'não releu a lista')
})

await confere('atalho repetido (409) vira aviso, sem quebrar', async () => {
  const { acoes, despachados } = montarRespostas(rotasRespostas([[(m) => m === 'POST', { status: 409, error: { code: 'CONFLICT', message: 'Atalho já usado.' } }]]))
  const ok = await acoes.incluirRespostaPronta({ titulo: 'Pix', atalho: '/pix', texto: 'Chave' })
  assert.equal(ok, false)
  const aviso = despachados.find((a) => a.tipo === acao.AVISO_API)
  assert.ok(aviso)
  assert.match(aviso.mensagem, /Atalho já usado/)
})

await confere('editar resposta faz PUT no id', async () => {
  const { acoes, chamadas } = montarRespostas(rotasRespostas([[(m) => m === 'PUT', { data: LISTA_API[0] }]]))
  await acoes.editarRespostaPronta('g1', { titulo: 'Pix', atalho: '/pix', texto: 'Nova chave' })
  const put = chamadas.find((c) => c.metodo === 'PUT')
  assert.match(put.url, /\/respostas-prontas\/g1$/)
  assert.equal(put.corpo.texto, 'Nova chave')
})

await confere('arquivar alterna pelo estado atual', async () => {
  const { acoes, chamadas } = montarRespostas(rotasRespostas([[(m, u) => m === 'POST' && u.includes('/arquivar'), { data: LISTA_API[0] }]]))
  await acoes.alternarArquivamentoRespostaPronta('g1')
  assert.ok(chamadas.some((c) => c.metodo === 'POST' && c.url.endsWith('/respostas-prontas/g1/arquivar?arquivada=true')))
})

await confere('ligar automática faz PUT no gatilho com o texto atual', async () => {
  const { acoes, chamadas } = montarRespostas(rotasRespostas([[(m) => m === 'PUT', { data: AUTOMACOES_API[1] }]]))
  await acoes.alternarRegra('recibo')
  const put = chamadas.find((c) => c.metodo === 'PUT')
  assert.match(put.url, /\/api\/atendimento\/automacoes\/PagamentoConfirmado$/)
  assert.deepEqual(put.corpo, { texto: 'Pago!', ligada: true })
})

await confere('ligar automática sem texto avisa e não chama a API', async () => {
  const { acoes, chamadas, despachados } = montarRespostas(rotasRespostas())
  await acoes.alternarRegra('encerramento')
  assert.equal(chamadas.filter((c) => c.metodo === 'PUT').length, 0)
  assert.ok(despachados.some((a) => a.tipo === acao.AVISO_API && /texto/i.test(a.mensagem)))
})

await confere('editar automática mantém ligada/desligada', async () => {
  const { acoes, chamadas } = montarRespostas(rotasRespostas([[(m) => m === 'PUT', { data: AUTOMACOES_API[0] }]]))
  await acoes.editarRegra('boas-vindas', { texto: 'Olá {nome}!' })
  const put = chamadas.find((c) => c.metodo === 'PUT')
  assert.match(put.url, /\/automacoes\/PrimeiroContato$/)
  assert.deepEqual(put.corpo, { texto: 'Olá {nome}!', ligada: true })
})

// ── 4. Tags ──────────────────────────────────────────────────────────────────
const CLIENTE = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
const dossie = {
  cliente: { id: CLIENTE, nome: 'Thatiane' },
  tags: [{ tag: 'sem gluten', origem: 'Manual', criadoEm: '2026-10-07T12:00:00Z' }],
  notas: [{ id: 'n1', texto: 'Prefere al dente', autor: 'Felipe', criadoEm: '2026-10-07T14:30:00Z' }],
}

await confere('dossiê traz as tags e a hora ISO da nota', () => {
  const c = clienteDoDossie(dossie)
  assert.deepEqual(c.cliente.tags, ['sem gluten'])
  assert.equal(c.cliente.notas[0].criadoEm, '2026-10-07T14:30:00Z')
})

function montarTags(conversa) {
  const chamadas = montarFetch([
    [(m, u) => u.endsWith('/dossie'), { data: dossie }],
    [(m) => m === 'POST', { status: 201, data: { tag: 'vegano' } }],
    [(m) => m === 'DELETE', { status: 204 }],
  ])
  const despachados = []
  const estadoRef = { current: { conversas: [conversa] } }
  const acoes = criarAcoesTagsApi({ despachar: (a) => despachados.push(a), estadoRef })
  return { acoes, chamadas, despachados }
}

await confere('adicionar tag faz POST no cliente e relê o dossiê', async () => {
  const { acoes, chamadas, despachados } = montarTags({ id: 'conv-1', clienteId: CLIENTE, cliente: { tags: [] } })
  await acoes.adicionarTag('conv-1', 'Vegano')
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.match(post.url, new RegExp(`/api/clientes/${CLIENTE}/tags$`))
  assert.deepEqual(post.corpo, { tag: 'Vegano' })
  assert.ok(chamadas.some((c) => c.url.endsWith('/conv-1/dossie')))
  assert.ok(despachados.some((a) => a.tipo === acao.CLIENTE_DA_API))
})

await confere('tirar tag faz DELETE com a tag codificada', async () => {
  const { acoes, chamadas } = montarTags({ id: 'conv-1', clienteId: CLIENTE, cliente: { tags: ['sem gluten'] } })
  await acoes.removerTag('conv-1', 'sem gluten')
  assert.ok(chamadas.some((c) => c.metodo === 'DELETE' && c.url.endsWith(`/api/clientes/${CLIENTE}/tags/sem%20gluten`)))
})

await confere('editar tag tira a antiga e põe a nova', async () => {
  const { acoes, chamadas } = montarTags({ id: 'conv-1', clienteId: CLIENTE, cliente: { tags: ['vegan'] } })
  await acoes.editarTag('conv-1', 'vegan', 'vegano')
  const metodos = chamadas.filter((c) => c.metodo !== 'GET').map((c) => c.metodo)
  assert.deepEqual(metodos, ['DELETE', 'POST'])
})

await confere('lead sem cadastro: tag avisa e não chama a API', async () => {
  const { acoes, chamadas, despachados } = montarTags({ id: 'conv-2', clienteId: null, cliente: { tags: [] } })
  await acoes.adicionarTag('conv-2', 'vegano')
  assert.equal(chamadas.length, 0)
  assert.ok(despachados.some((a) => a.tipo === acao.AVISO_API && /cadastr/i.test(a.mensagem)))
})

await confere('tags, respostas e automáticas saíram da lista de não ligadas', () => {
  for (const nome of ['adicionarTag', 'removerTag', 'editarTag', 'alternarRegra', 'editarRegra',
    'incluirRespostaPronta', 'editarRespostaPronta', 'alternarArquivamentoRespostaPronta']) {
    assert.equal(NAO_LIGADAS[nome], undefined, `${nome} ainda avisa`)
  }
})

// ── 5. Notas como post-it na conversa ────────────────────────────────────────
await confere('nota entra na linha do tempo pela hora, entre as mensagens', () => {
  const mensagens = [
    { id: 'm1', em: '2026-10-07T14:00:00Z' },
    { id: 'm2', em: '2026-10-07T15:00:00Z' },
  ]
  const linha = notas.intercalarNotas(mensagens, [
    { id: 'n1', texto: 'a', criadoEm: '2026-10-07T14:30:00Z' },
    { id: 'n0', texto: 'sem hora', em: '07/10 14:30' },
  ])
  assert.deepEqual(linha.map((l) => `${l.tipo}:${(l.mensagem ?? l.nota).id}`), ['mensagem:m1', 'nota:n1', 'mensagem:m2'])
})

await confere('hora da nota sem fuso vem da API em UTC (ganha o Z)', () => {
  const c = clienteDoDossie({ cliente: { id: CLIENTE }, notas: [{ id: 'n2', texto: 'x', autor: 'A', criadoEm: '2026-10-07T14:30:00' }] })
  assert.equal(c.cliente.notas[0].criadoEm, '2026-10-07T14:30:00Z')
})

await confere('nota mais nova que todas as mensagens fica no fim', () => {
  const linha = notas.intercalarNotas([{ id: 'm1', em: '2026-10-07T14:00:00Z' }], [{ id: 'n9', texto: 'b', criadoEm: '2026-10-08T09:00:00Z' }])
  assert.equal(linha.at(-1).tipo, 'nota')
})

// ── 6. O que o automático mandou ─────────────────────────────────────────────
const base = { direcao: 'Saida', status: 'Enviada', enviadaEm: '2026-10-07T14:00:00Z', tipoConteudo: 'Texto' }

await confere('mensagem do Sistema chega marcada como automática do sistema', () => {
  const m = mensagemDaApi({ ...base, id: 's1', autor: 'Sistema', texto: 'Pagamento confirmado!' })
  assert.equal(m.automatica, true)
  assert.equal(m.origemAutomatica, 'sistema')
  const a = mensagemDaApi({ ...base, id: 'a1', autor: 'Agente', texto: 'Oi' })
  assert.equal(a.origemAutomatica, 'agente')
  const d = mensagemDaApi({ ...base, id: 'd1', autor: 'Dona', texto: 'Oi' })
  assert.equal(d.automatica, undefined)
})

await confere('histórico lista só os envios automáticos, do mais novo ao mais antigo', () => {
  const lista = automacao.enviosAutomaticos([
    { id: 'm1', dir: 'in', em: '2026-10-07T13:00:00Z' },
    { id: 's1', dir: 'out', automatica: true, origemAutomatica: 'sistema', em: '2026-10-07T14:00:00Z' },
    { id: 'd1', dir: 'out', em: '2026-10-07T14:10:00Z' },
    { id: 'a1', dir: 'out', automatica: true, origemAutomatica: 'agente', em: '2026-10-07T14:20:00Z' },
  ])
  assert.deepEqual(lista.map((m) => m.id), ['a1', 's1'])
})

// ── 7. Reducer ───────────────────────────────────────────────────────────────
await confere('reducer guarda respostas e regras lidas da API', () => {
  const inicial = estadoInicial({ conversas: [], catalogo: { respostasProntas: [] }, regras: [] })
  const comRespostas = reducer(inicial, { tipo: acao.RESPOSTAS_DA_API, respostas: [{ id: 'g1' }] })
  assert.deepEqual(comRespostas.catalogo.respostasProntas, [{ id: 'g1' }])
  const comRegras = reducer(comRespostas, { tipo: acao.REGRAS_DA_API, regras: [{ id: 'recibo' }] })
  assert.deepEqual(comRegras.regras, [{ id: 'recibo' }])
})

await esvaziar()
console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
