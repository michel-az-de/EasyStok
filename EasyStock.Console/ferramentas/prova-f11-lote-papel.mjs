/* eslint-disable no-console */
// Prova da #1241 (F11, S46): no modo API o lote de papel vai ao EasyStok.
//   - cada passo marcado vira linhas do POST api/atendimento/esteira/lote, com os passos
//     intermediários (a máquina de estados do pedido não pula de pago para entregue);
//   - nenhuma mensagem "lida" falsa entra na conversa: o EasyStok não avisa retroativo;
//   - linha rejeitada volta com o motivo; o pedido é relido do EasyStok;
//   - queda de conexão vista pela sincronização liga a faixa do papel.
//
//   node ferramentas/prova-f11-lote-papel.mjs

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
const { NAO_LIGADAS, SO_DA_TELA } = await import('../src/aplicacao/api/naoLigadas.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const lote = await import('../src/dominio/loteDePapel.js')

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

const AGORA = Date.parse('2026-10-08T15:00:00Z')
const conversa = (id, numero, estado, pedidoId) => ({
  id, nome: `Cliente ${id}`, mensagens: [], pedido: { numero, estado, pedidoId, janela: null },
})

function montarLote(rotas, estado = {}) {
  const chamadas = montarFetch(rotas)
  const despachados = []
  const estadoRef = {
    current: {
      conversas: [conversa('c1', '#A1', 'pago', 'p-1'), conversa('c2', '#A2', 'preparo', 'p-2')],
      conexao: { online: true, offlineDesde: '2026-10-08T14:00:00Z', voltouEm: '2026-10-08T14:40:00Z', pedidosAbertos: ['#A1', '#A2'] },
      catalogo: { janelas: [] },
      ...estado,
    },
  }
  return { chamadas, despachados, estadoRef }
}

const ROTA_LOTE = (m, u) => m === 'POST' && u.endsWith('/api/atendimento/esteira/lote')
const RESULTADO_OK = {
  aplicados: 4, rejeitados: 0,
  linhas: [1, 2, 3, 4].map((linha) => ({ linha, sucesso: true, motivo: null })),
}

// ── 1. Domínio: passos do papel no vocabulário da API ────────────────────────
await confere('passosNaApi expande os intermediários e pula a entrega quando não marcada', () => {
  assert.deepEqual(lote.passosNaApi('pago', 'entregue'), ['preparando', 'pronto', 'entregue'])
  assert.deepEqual(lote.passosNaApi('pago', 'preparo'), ['preparando'])
  assert.deepEqual(lote.passosNaApi('preparo', 'entrega'), ['pronto', 'saiu_para_entrega'])
  assert.deepEqual(lote.passosNaApi('entrega', 'entregue'), ['entregue'])
  assert.deepEqual(lote.passosNaApi('embalado', 'embalado'), [])
})

// ── 2. Honestidade: lote ligado; queda e volta são só da tela ────────────────
await confere('lote de papel saiu da lista de não ligadas', () => {
  assert.equal(NAO_LIGADAS.lancarLotePapel, undefined)
  assert.ok(SO_DA_TELA.includes('conexaoCaiu') && SO_DA_TELA.includes('conexaoVoltou'))
})

// ── 3. Ação API ──────────────────────────────────────────────────────────────
const { criarAcoesLoteApi } = await import('../src/aplicacao/api/lote.js')
const criar = ({ despachados, estadoRef }) => criarAcoesLoteApi({ despachar: (a) => despachados.push(a), estadoRef })

await confere('lancarLotePapel manda as linhas com os intermediários e o horário da volta', async () => {
  const m = montarLote([[ROTA_LOTE, { data: RESULTADO_OK }]])
  await criar(m).lancarLotePapel({ c1: 'entregue', c2: 'embalado' }, AGORA)
  const post = m.chamadas.find((c) => c.metodo === 'POST')
  assert.ok(post, 'faz o POST do lote')
  assert.deepEqual(post.corpo.linhas.map((l) => [l.pedidoId, l.passo]), [
    ['p-1', 'preparando'], ['p-1', 'pronto'], ['p-1', 'entregue'], ['p-2', 'pronto'],
  ])
  assert.ok(post.corpo.linhas.every((l) => l.ocorreuEm === '2026-10-08T14:40:00.000Z'))
})

await confere('não cria mensagem lida falsa e fecha a pendência do papel', async () => {
  const m = montarLote([[ROTA_LOTE, { data: RESULTADO_OK }]])
  await criar(m).lancarLotePapel({ c1: 'preparo' }, AGORA)
  assert.ok(!m.despachados.some((a) => a.tipo === acao.LANCAR_LOTE_PAPEL), 'não usa o caso local que escreve mensagem')
  const fechou = m.despachados.find((a) => a.tipo === acao.LOTE_PAPEL_LANCADO_API)
  assert.ok(fechou, 'fecha a pendência')
  const inicial = estadoInicial({ conversas: [conversa('c1', '#A1', 'pago', 'p-1')] })
  const offline = { ...inicial, conexao: { online: true, offlineDesde: 'x', voltouEm: 'y', pedidosAbertos: ['#A1'] } }
  const depois = reducer(offline, fechou)
  assert.deepEqual(depois.conexao.pedidosAbertos, [])
  assert.equal(depois.conversas[0].mensagens.length, 0)
})

await confere('relê o pedido de cada conversa lançada', async () => {
  const m = montarLote([
    [ROTA_LOTE, { data: RESULTADO_OK }],
    [(mt, u) => mt === 'GET' && u.includes('/conversas/c1/pedido'), { data: { pedidoId: 'p-1', status: 'entregue', itens: [] } }],
  ])
  await criar(m).lancarLotePapel({ c1: 'entregue' }, AGORA)
  assert.ok(m.chamadas.some((c) => c.metodo === 'GET' && c.url.includes('/conversas/c1/pedido')))
  assert.ok(m.despachados.some((a) => a.tipo === acao.SINCRONIZAR_PEDIDO && a.id === 'c1'))
})

await confere('linha rejeitada volta com o motivo e avisa', async () => {
  const m = montarLote([[ROTA_LOTE, { data: {
    aplicados: 0, rejeitados: 1,
    linhas: [{ linha: 1, pedidoId: 'p-2', passo: 'pronto', sucesso: false, motivo: 'Transição inválida' }],
  } }]])
  const r = await criar(m).lancarLotePapel({ c2: 'embalado' }, AGORA)
  assert.deepEqual(r.rejeitados, [{ numero: '#A2', motivo: 'Transição inválida' }])
  assert.ok(m.despachados.some((a) => a.tipo === acao.AVISO_API && /#A2/.test(a.mensagem)))
})

await confere('pedido que ainda não está no EasyStok não vai e é apontado', async () => {
  const m = montarLote([], {
    conversas: [conversa('c3', '#A3', 'pago', null)],
    conexao: { online: true, voltouEm: null, pedidosAbertos: ['#A3'] },
  })
  const r = await criar(m).lancarLotePapel({ c3: 'preparo' }, AGORA)
  assert.ok(!m.chamadas.some((c) => c.metodo === 'POST'), 'sem linha válida, não chama a API')
  assert.equal(r.rejeitados[0].numero, '#A3')
})

await confere('falha de rede avisa e mantém a pendência', async () => {
  const m = montarLote([])
  globalThis.fetch = async () => { throw new Error('offline') }
  const r = await criar(m).lancarLotePapel({ c1: 'preparo' }, AGORA)
  assert.equal(r, null)
  assert.ok(m.despachados.some((a) => a.tipo === acao.AVISO_API))
  assert.ok(!m.despachados.some((a) => a.tipo === acao.LOTE_PAPEL_LANCADO_API))
})

// ── 4. Queda vista pela sincronização ────────────────────────────────────────
await confere('erro sem conexão na sincronização liga a faixa; voltar desliga', async () => {
  const { quedaDaSincronizacao } = await import('../src/aplicacao/planoDeSincronizacao.js')
  assert.equal(quedaDaSincronizacao({ codigo: 'SEM_CONEXAO' }), true)
  assert.equal(quedaDaSincronizacao({ codigo: 'HTTP_500' }), false)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
