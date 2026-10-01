/* eslint-disable no-console */
// Prova da F07 (issue #1237): sincronização correta do console no modo API.
// Cobre as partes puras dos itens 1, 2, 3 e 5 da spec (11-console-fechamento.md):
//   1. relógio que relê o real a cada tique (aba suspensa não atrasa a janela de 24 h);
//   2. mensagens da conversa selecionada relidas mesmo sem mudar `ultimaMensagemEm`;
//   3. três textos da pausa: você assumiu, outro atendente, o automático passou para você;
//   5. cobrança online sem `meioAnterior` é link (não Pix) e a troca que falha recarrega.
//
//   node ferramentas/prova-f07-sincronizacao.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

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
    return proximo(url, contexto)
  },
})

const { proximoInstante } = await import('../src/hooks/relogio.js')
const { deveRelerMensagens } = await import('../src/aplicacao/planoDeSincronizacao.js')
const { conversaDaApi, mensagemDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { modoDoAtendimento, motivoDePrecisar, MODOS } = await import('../src/dominio/automatico.js')
const { MOTIVO_PADRAO } = await import('../src/dominio/passagem.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')

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

// --- Item 1: relógio --------------------------------------------------------
const T0 = Date.parse('2026-09-30T12:00:00Z')
const PASSO = 30000

await confere('1. modo API: tique depois de 10 min suspenso lê o relógio real', () => {
  const real = T0 + 10 * 60000
  assert.equal(proximoInstante(T0, PASSO, true, real), real)
})

await confere('1. demonstração: o relógio do protótipo segue instante fixo + passo', () => {
  assert.equal(proximoInstante(T0, PASSO, false, T0 + 10 * 60000), T0 + PASSO)
})

// --- Item 2: status da mensagem ---------------------------------------------
const resumo = (extra = {}) => ({
  id: 'conv-1', canal: 'WhatsApp', contatoIdExterno: '5511999990000', contatoNome: 'Maria',
  clienteId: null, situacao: 'Automatica', naoLidas: 0, ultimaMensagemEm: '2026-09-30T11:58:00',
  ultimaMensagemTexto: 'oi', pedidoEmAndamentoId: null, assumidaPorUsuarioId: null, dentroDaJanela: true,
  ...extra,
})
const guardado = { ultima: '2026-09-30T11:58:00', mensagens: [] }

await confere('2. conversa selecionada relê as mensagens mesmo com `ultimaMensagemEm` igual', () => {
  assert.equal(deveRelerMensagens(resumo(), guardado, 'conv-1'), true)
})

await confere('2. conversa não selecionada e sem mudança fica no cache', () => {
  assert.equal(deveRelerMensagens(resumo(), guardado, 'outra'), false)
  assert.equal(deveRelerMensagens(resumo({ ultimaMensagemEm: '2026-09-30T11:59:00' }), guardado, 'outra'), true)
  assert.equal(deveRelerMensagens(resumo(), undefined, 'outra'), true)
})

await confere('2. status novo do servidor (Falhou) substitui o da tela na mescla', () => {
  const saida = (status) => mensagemDaApi({
    id: 'm1', direcao: 'Saida', autor: 'Atendente', tipoConteudo: 'Texto', texto: 'segue o link',
    status, erro: status === 'Falhou' ? 'número inválido' : null, enviadaEm: '2026-09-30T11:58:00',
  })
  const antes = conversaDaApi(resumo(), [], null)
  const base = reducer({ ...estadoInicial, conversas: [], selecionadaId: 'conv-1' },
    { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [{ ...antes, mensagens: [saida('Enviada')] }] })
  const depois = reducer(base, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [{ ...antes, mensagens: [saida('Falhou')] }] })
  assert.equal(depois.conversas[0].mensagens[0].status, 'falhou')
  assert.equal(depois.conversas[0].mensagens[0].erro, 'número inválido')
})

// --- Item 3: três textos da pausa -------------------------------------------
const EU = { id: 'u-eu', nome: 'Thatiane' }
const sincronizada = (r) => {
  const conversa = conversaDaApi(r, [], EU)
  const estado = reducer({ ...estadoInicial, conversas: [], selecionadaId: r.id },
    { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversa] })
  const local = estado.conversas[0]
  return { conversa: local, pausa: estado.automaticoPausado[r.id] }
}

await confere('3. você assumiu: "Pausado porque você assumiu"', () => {
  const { conversa, pausa } = sincronizada(resumo({ situacao: 'Assumida', assumidaPorUsuarioId: 'u-eu' }))
  const modo = modoDoAtendimento(conversa, pausa)
  assert.equal(modo.chave, MODOS.PAUSADO)
  assert.equal(modo.rotulo, 'Pausado porque você assumiu')
})

await confere('3. outro atendente assumiu: o texto não diz que foi você', () => {
  const { conversa, pausa } = sincronizada(resumo({ situacao: 'Assumida', assumidaPorUsuarioId: 'u-outro' }))
  const modo = modoDoAtendimento(conversa, pausa)
  assert.equal(modo.chave, MODOS.PAUSADO)
  assert.equal(modo.rotulo, 'Pausado porque outro atendente assumiu')
})

await confere('3. o automático escalou com motivo: "Passou para você" e o selo com o motivo', () => {
  const { conversa, pausa } = sincronizada(resumo({
    situacao: 'Assumida', assumidaPorUsuarioId: null, motivoEscalada: 'Cliente perguntou sobre glúten',
  }))
  const modo = modoDoAtendimento(conversa, pausa)
  assert.equal(modo.chave, MODOS.COM_VOCE)
  assert.equal(modo.rotulo, 'Passou para você')
  assert.match(modo.detalhe, /cliente perguntou sobre glúten/)
  const selo = motivoDePrecisar(conversa, T0, pausa)
  assert.equal(selo.rotulo, 'Precisa de você')
  assert.equal(selo.texto, 'Cliente perguntou sobre glúten')
})

await confere('3. escalada sem `motivoEscalada` (API anterior à F08) usa o motivo padrão', () => {
  const { conversa, pausa } = sincronizada(resumo({ situacao: 'Assumida', assumidaPorUsuarioId: null }))
  assert.equal(modoDoAtendimento(conversa, pausa).chave, MODOS.COM_VOCE)
  assert.equal(motivoDePrecisar(conversa, T0, pausa).texto, MOTIVO_PADRAO)
})

await confere('3. conversa automática segue com o automático ligado', () => {
  const { conversa, pausa } = sincronizada(resumo())
  assert.equal(modoDoAtendimento(conversa, pausa).chave, MODOS.LIGADO)
})

// --- Item 5: meio da cobrança -----------------------------------------------
const PEDIDO = 'abcdef12-3333-3333-3333-333333333333'
const pedidoDoServidor = (provedor) => ({
  pedidoId: PEDIDO, status: 'aguardando_pagamento', total: 93, frete: 8,
  itens: [{ cardapioItemId: 'x1', nome: 'Lasanha', quantidade: 1, precoUnitario: 85, observacao: null }],
  cobranca: {
    cobrancaId: 'c1', provedor, status: 'Pendente', linkPagamento: provedor === 'na_entrega' ? null : 'https://mp.test/1',
    valor: 93, expiraEm: null, pagaEm: null, valorPago: null, tentativa: 1, criadaEm: '2026-09-30T11:50:00',
  },
})

await confere('5. cobrança online sem meio anterior é link de pagamento, não Pix', () => {
  const pedido = pedidoDaApi(pedidoDoServidor('mercadopago'))
  assert.equal(pedido.cobranca.meio, 'cartao-link')
  assert.equal(pedido.meio, 'cartao-link')
})

await confere('5. meio anterior da mesma forma é mantido; de outra forma, cai no padrão da forma', () => {
  assert.equal(pedidoDaApi(pedidoDoServidor('mercadopago'), { meio: 'pix' }).meio, 'pix')
  assert.equal(pedidoDaApi(pedidoDoServidor('mercadopago'), { meio: 'maquininha' }).meio, 'cartao-link')
  assert.equal(pedidoDaApi(pedidoDoServidor('na_entrega')).meio, 'maquininha')
})

await confere('5. troca de forma que falha na API recarrega o pedido do servidor', async () => {
  const chamadas = []
  globalThis.fetch = async (url, { method }) => {
    chamadas.push(`${method} ${url}`)
    if (method === 'GET') return new Response(JSON.stringify({ data: pedidoDoServidor('mercadopago') }), { status: 200 })
    return new Response(JSON.stringify({ error: { code: 'X', message: 'não trocou' } }), { status: 500 })
  }
  const despachados = []
  const estadoRef = { current: { conversas: [{ id: 'conv-1', pedido: { pedidoId: PEDIDO, meio: 'cartao-link' } }] } }
  const despachar = (a) => {
    despachados.push(a)
    if (a.tipo === acao.ESCOLHER_MEIO_PAGAMENTO) {
      estadoRef.current = { conversas: [{ id: 'conv-1', pedido: { pedidoId: PEDIDO, meio: a.meio } }] }
    }
  }
  const acoes = criarAcoesComandaApi({}, { despachar, estadoRef })
  await acoes.alterarMeioPagamento('conv-1', 'maquininha')
  assert.ok(chamadas.some((c) => c.startsWith('GET ')), `sem GET do pedido depois da falha: ${chamadas.join(', ')}`)
  const sincronizado = despachados.find((a) => a.tipo === acao.SINCRONIZAR_PEDIDO)
  assert.ok(sincronizado, 'o pedido do servidor não voltou para a tela')
  assert.equal(sincronizado.pedido.meio, 'cartao-link')
  assert.ok(despachados.some((a) => a.tipo === acao.AVISO_API), 'a falha não avisou')
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length > 0) process.exit(1)
