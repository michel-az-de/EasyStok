import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

// Exercita o hook e o ciclo reais; somente React, relógio, SSE e HTTP são fronteiras controladas.
globalThis.provaPedidoAoVivo = { efeitos: [], conexoes: [] }
registerHooks({
  resolve(nome, contexto, proximo) {
    if (nome === 'react') return { url: 'prova:react', shortCircuit: true }
    if (nome.startsWith('.') && !/\.[a-z]+$/i.test(nome)) {
      try { return proximo(nome + '.js', contexto) } catch { /* resolução original */ }
    }
    return proximo(nome, contexto)
  },
  load(url, contexto, proximo) {
    const source = url === 'prova:react'
      ? 'export const useRef = current => ({current}); export const useEffect = f => globalThis.provaPedidoAoVivo.efeitos.push(f)'
      : url.endsWith('/infra/fonteDados.js') ? "export const FONTE_API = true; export const API_BASE = ''"
        : url.endsWith('/aplicacao/api/cardapio.js') ? 'export const lerCardapio = async () => []; export const lerAlertasDeEstoque = async () => []'
          : url.endsWith('/infra/api/eventosOperacao.js')
            ? 'export function conectarEventosOperacao(c) { globalThis.provaPedidoAoVivo.conexoes.push(c); return () => {c.fechada = true} }' : null
    return source == null ? proximo(url, contexto) : { format: 'module', shortCircuit: true, source }
  },
})

const { useSincronizacaoApi } = await import('../src/aplicacao/useSincronizacaoApi.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const { criarMemoriaDoLaco } = await import('../src/aplicacao/cicloDeSincronizacao.js')
const { faltaPagar } = await import('../src/dominio/pagamento.js')
const antigos = { fetch: globalThis.fetch, setTimeout, clearTimeout }
const respirar = async () => { for (let i = 0; i < 12; i += 1) await new Promise(setImmediate) }
let passou = 0
const falhas = []

async function montar({ selecionada = null, memoria = criarMemoriaDoLaco(), alocadoInicial = 15 } = {}) {
  const ambiente = globalThis.provaPedidoAoVivo = { efeitos: [], conexoes: [] }
  let agora = 0
  let seguinte = 0
  const tempos = new Map()
  globalThis.setTimeout = (fn, ms) => { const id = ++seguinte; tempos.set(id, { fn, em: agora + ms }); return id }
  globalThis.clearTimeout = id => tempos.delete(id)
  const pedido = (id, alocado) => ({ pedidoId: id, status: 'aguardando', itens: [], total: 25, alocado, frete: 5 })
  const s = {
    pedidos: { a: pedido('pedido-a', alocadoInicial), b: pedido('pedido-b', 25) }, chamadas: [], falha: false,
    emVoo: 0, pico: 0, retida: null,
    conversas: ['a', 'b'].map(id => ({ id, canal: 'WhatsApp', contatoNome: id, situacao: 'Aberta',
      ultimaMensagemEm: '2026-10-12T04:00:00Z', pedidoEmAndamentoId: `pedido-${id}`, pedidoStatus: 'aguardando' })),
  }
  globalThis.fetch = async url => {
    s.chamadas.push(url)
    if (url.includes('/conversas?')) return Response.json({ data: s.conversas })
    if (url.endsWith('/mensagens')) return Response.json({ data: [] })
    const id = /\/conversas\/([^/]+)\/pedido$/.exec(url)?.[1]
    if (!id) throw new Error(`HTTP inesperado: ${url}`)
    s.emVoo += 1; s.pico = Math.max(s.pico, s.emVoo)
    const dados = structuredClone(s.pedidos[id])
    try {
      if (s.retida?.id === id) {
        const retida = s.retida; s.retida = null; retida.entrou = true; await retida.espera
      }
      if (s.falha) throw new TypeError('Failed to fetch')
      return Response.json({ data: dados })
    } finally { s.emVoo -= 1 }
  }
  let estado = estadoInicial({ conversas: [], catalogo: { janelas: [] }, regras: [] })
  const acoes = []
  useSincronizacaoApi({ ativo: true, usuario: { id: 'atendente' }, selecionadaId: selecionada, memoria,
    despachar: a => { acoes.push(a); estado = reducer(estado, a) } })
  const desligar = ambiente.efeitos.map(f => f()).filter(f => typeof f === 'function')
  await respirar()
  const m = {
    s, memoria, acoes,
    pedido: id => estado.conversas.find(c => c.id === id)?.pedido,
    evento: (pedidoId = 'pedido-a') => ambiente.conexoes.at(-1)?.aoEvento({ evento: 'pedido.pagamento_atualizado', dados: { pedidoId } }),
    eventoComanda: (conversaId = 'a') => ambiente.conexoes.at(-1)?.aoEvento({ evento: 'conversa.comanda_atualizada', dados: { conversaId } }),
    ready: () => ambiente.conexoes.at(-1)?.aoEvento({ evento: 'ready', dados: {} }),
    cair: () => ambiente.conexoes.at(-1)?.aoCair(),
    conexoes: () => ambiente.conexoes.length,
    fechar: () => desligar.forEach(f => f()),
    reter(id) {
      let liberar
      const retida = { id, entrou: false, espera: new Promise(r => { liberar = r }) }
      s.retida = retida
      return { retida, liberar }
    },
    async avancar(ms) {
      const ate = agora + ms
      for (;;) {
        const proximo = [...tempos].filter(([, t]) => t.em <= ate).sort((a, b) => a[1].em - b[1].em)[0]
        if (!proximo) break
        const [id, t] = proximo; agora = t.em; tempos.delete(id); t.fn(); await respirar()
      }
      agora = ate; await respirar()
    },
  }
  s.chamadas.length = 0
  s.pico = 0
  return m
}

async function confere(nome, fn, opcoes) {
  let m
  try { m = await montar(opcoes); await fn(m); passou += 1; console.log(`ok ${nome}`) }
  catch (erro) { falhas.push(nome); console.log(`FALHA ${nome}: ${erro.message.split('\n').slice(0, 2).join(' ')}`) }
  finally { m?.fechar() }
}

await confere('SSE atualiza financeiro de pedido não selecionado sem mudar status nem reler inbox/outros pedidos', async m => {
  assert.equal(m.pedido('a').alocadoApi, 15)
  m.s.pedidos.a.alocado = 25; m.evento(); await m.avancar(300)
  assert.equal(m.pedido('a').alocadoApi, 25, 'o indicador manteve o saldo anterior ao pagamento')
  assert.equal(m.pedido('a').statusApi, 'aguardando')
  assert.deepEqual(m.s.chamadas, ['/api/atendimento/conversas/a/pedido'])
  assert.equal(m.memoria.pedidos.get('b').dados.alocado, 25)
})

await confere('evento real de alteração da comanda atualiza total/saldo antes do próximo pagamento', async m => {
  m.eventoComanda('fora-da-inbox'); m.eventoComanda(null); await m.avancar(300)
  assert.equal(m.s.chamadas.length, 0, 'conversa desconhecida não pode invalidar todos os pedidos')
  m.s.pedidos.a.alocado = 25; m.evento(); await m.avancar(300); m.s.chamadas.length = 0
  m.s.pedidos.a.total = 35
  for (let i = 0; i < 8; i += 1) m.eventoComanda()
  await m.avancar(300)
  assert.equal(m.pedido('a').totalApi, 35, 'o indicador conservou o total anterior à alteração da comanda')
  assert.equal(faltaPagar(m.pedido('a'), []), 10)
  assert.equal(m.pedido('a').statusApi, 'aguardando')
  assert.deepEqual(m.s.chamadas, ['/api/atendimento/conversas/a/pedido'])
  m.s.chamadas.length = 0
  const { retida, liberar } = m.reter('a')
  m.eventoComanda(); await m.avancar(300); assert.equal(retida.entrou, true)
  m.s.pedidos.a.total = 45; m.eventoComanda(); liberar(); await respirar(); await m.avancar(300)
  assert.equal(m.pedido('a').totalApi, 45, 'evento da conversa em voo se perdeu no cache antigo')
  assert.equal(m.s.chamadas.length, 2)
  assert.equal(m.s.pico, 1)
})

await confere('rajada do mesmo pedido agrupa uma única leitura e ignora pedido desconhecido', async m => {
  m.evento('fora-da-inbox'); await m.avancar(300); assert.equal(m.s.chamadas.length, 0)
  m.s.pedidos.a.alocado = 25
  for (let i = 0; i < 8; i += 1) m.evento()
  await m.avancar(300)
  assert.equal(m.pedido('a').alocadoApi, 25)
  assert.deepEqual(m.s.chamadas, ['/api/atendimento/conversas/a/pedido'])
})

await confere('evento durante GET antigo mantém a invalidação até leitura posterior, sem concorrência', async m => {
  const { retida, liberar } = m.reter('a')
  m.evento(); await m.avancar(300); assert.equal(retida.entrou, true)
  m.s.pedidos.a.alocado = 25; m.evento(); liberar(); await respirar(); await m.avancar(300)
  assert.equal(m.pedido('a').alocadoApi, 25)
  assert.equal(m.memoria.pedidos.get('a').dados.alocado, 25)
  assert.equal(m.s.chamadas.length, 2)
  assert.equal(m.s.pico, 1, 'o evento iniciou leitura concorrente com o GET antigo')
})

await confere('evento durante GET periódico não se perde na resposta antiga nem inicia outro ciclo em paralelo', async m => {
  const { retida, liberar } = m.reter('a')
  await m.avancar(5000); assert.equal(retida.entrou, true)
  m.s.pedidos.a.alocado = 25; m.evento(); liberar(); await respirar(); await m.avancar(300)
  assert.equal(m.pedido('a').alocadoApi, 25)
  assert.equal(m.s.chamadas.filter(c => c.endsWith('/a/pedido')).length, 2)
  assert.equal(m.s.chamadas.filter(c => c.endsWith('/b/pedido')).length, 0)
  assert.equal(m.s.pico, 1)
}, { selecionada: 'a' })

await confere('eventos frequentes não adiam a conferência periódica da inbox', async m => {
  for (let i = 0; i < 12; i += 1) { m.evento(); await m.avancar(500) }
  assert.equal(m.s.chamadas.filter(c => c.includes('/conversas?')).length, 1)
})

await confere('falha mantém cache anterior e a volta periódica repete somente o pedido invalidado', async m => {
  m.s.falha = true; m.evento(); await m.avancar(300)
  assert.equal(m.pedido('a').alocadoApi, 15)
  m.s.falha = false; m.s.pedidos.a.alocado = 25; await m.avancar(5000)
  assert.equal(m.pedido('a').alocadoApi, 25)
  assert.equal(m.s.chamadas.filter(c => c.endsWith('/b/pedido')).length, 0)
})

await confere('ready após reconexão confere pedidos conhecidos sem perder cache nem reler mensagens/inbox', async m => {
  m.cair(); await m.avancar(5000); assert.equal(m.conexoes(), 2)
  m.s.chamadas.length = 0; m.s.pedidos.a.alocado = 25; m.ready(); await m.avancar(300)
  assert.equal(m.pedido('a').alocadoApi, 25)
  assert.deepEqual(m.s.chamadas.sort(), ['/api/atendimento/conversas/a/pedido', '/api/atendimento/conversas/b/pedido'])
})

await confere('desmontagem fecha SSE e cancela releitura agendada', async m => {
  m.evento(); m.fechar(); await m.avancar(10000)
  assert.equal(m.s.chamadas.length, 0)
  assert.equal(globalThis.provaPedidoAoVivo.conexoes.every(c => c.fechada), true)
})

await confere('GET do hook desmontado não sobrescreve o cache compartilhado da montagem nova', async m => {
  const { retida, liberar } = m.reter('a')
  m.evento(); await m.avancar(300); assert.equal(retida.entrou, true)
  m.fechar()
  const nova = await montar({ memoria: m.memoria, alocadoInicial: 25 })
  try {
    assert.equal(nova.memoria.pedidos.get('a').dados.alocado, 25)
    liberar(); await respirar()
    assert.equal(nova.memoria.pedidos.get('a').dados.alocado, 25, 'resposta da montagem encerrada substituiu o cache mais recente')
    assert.equal(nova.pedido('a').alocadoApi, 25)
  } finally { liberar(); nova.fechar() }
})

Object.assign(globalThis, antigos)
console.log(`${passou}/${passou + falhas.length} provas do pedido ao vivo aprovadas`)
if (falhas.length) process.exitCode = 1
