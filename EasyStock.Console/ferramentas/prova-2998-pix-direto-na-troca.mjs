/* eslint-disable no-console */
// Prova da #2998: no modo API, "Alterar forma de pagamento" oferece o Pix na chave da loja num pedido que já
// tem cobrança por link (vencido ou pendente), quando o Pix direto está disponível (flag `pagamento.pix_direto`
// e loja com chave, lidos de GET /api/pix-direto por `usePixDireto`). Na prova da E3 (#2962), em 20/11, a
// tela só oferecia "receber na entrega" e a troca saiu chamando a rota direto. A recusa da API aparece na tela.
//
//   node ferramentas/prova-2998-pix-direto-na-troca.mjs

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
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})

const cob = await import('../src/dominio/cobranca.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8').replaceAll(String.fromCharCode(13), '')
const trecho = (texto, inicio, fim) => {
  const de = texto.indexOf(inicio)
  assert.ok(de >= 0, `trecho não encontrado: ${inicio}`)
  const ate = texto.indexOf(fim, de + inicio.length)
  assert.ok(ate > de, `fim do trecho não encontrado: ${fim}`)
  return texto.slice(de, ate)
}

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

const LINK = { meio: 'cartao-link', link: 'https://mp/x', manual: false, expiraEm: Date.parse('2026-11-20T15:30:00Z') }
const LINK_VENCIDO = { ...LINK, expiraEm: Date.parse('2026-11-20T12:00:00Z') }
const NA_ENTREGA = { meio: 'maquininha', link: null, manual: true }
const PIX_DIRETO = { meio: cob.MEIO_PIX_DIRETO, link: null, manual: true, copiaECola: '000201' }
const meios = (opcoes) => opcoes.map((o) => o.meio)

await confere('1  com link (pendente ou vencido) e Pix direto disponível, a troca oferece na entrega e Pix na chave da loja', () => {
  for (const cobranca of [LINK, LINK_VENCIDO]) {
    const opcoes = cob.trocasDeFormaNoEasyStok(cobranca, 'Ana', { pixDireto: true })
    assert.deepEqual(meios(opcoes), ['maquininha', cob.MEIO_PIX_DIRETO])
    const pix = opcoes.find((o) => o.meio === cob.MEIO_PIX_DIRETO)
    assert.equal(pix.rotulo, 'Pix na chave da loja')
    assert.match(pix.texto, /O link de pagamento é cancelado/)
    assert.match(pix.texto, /Ana recebe o Pix na chave da loja/)
    // A API confere o link aberto no Mercado Pago e recusa com pagamento em andamento (MensagemLinkPendente).
    assert.match(pix.texto, /confere o Mercado Pago/)
  }
})

await confere('2  sem Pix direto disponível, nada muda: a troca de antes, uma opção só', () => {
  for (const cobranca of [LINK, NA_ENTREGA, PIX_DIRETO]) {
    const opcoes = cob.trocasDeFormaNoEasyStok(cobranca, 'Ana', { pixDireto: false })
    assert.equal(opcoes.length, 1)
    assert.deepEqual(opcoes[0], { ...cob.trocaDeFormaNoEasyStok(cobranca, 'Ana'), rotulo: opcoes[0].rotulo })
  }
  assert.deepEqual(meios(cob.trocasDeFormaNoEasyStok(LINK, 'Ana')), ['maquininha'], 'sem opções vale indisponível')
})

await confere('3  na entrega também troca para o Pix direto; quem já está no Pix direto não o vê de novo', () => {
  const daEntrega = cob.trocasDeFormaNoEasyStok(NA_ENTREGA, 'Ana', { pixDireto: true })
  assert.deepEqual(meios(daEntrega), ['cartao-link', cob.MEIO_PIX_DIRETO])
  assert.doesNotMatch(daEntrega[1].texto, /link de pagamento é cancelado|Mercado Pago/)
  assert.deepEqual(meios(cob.trocasDeFormaNoEasyStok(PIX_DIRETO, 'Ana', { pixDireto: true })), ['cartao-link'])
})

await confere('4  toda opção tem rótulo para o chip e texto sem travessão', () => {
  for (const cobranca of [LINK, NA_ENTREGA, PIX_DIRETO]) {
    for (const opcao of cob.trocasDeFormaNoEasyStok(cobranca, 'Ana', { pixDireto: true })) {
      assert.ok(opcao.rotulo?.trim(), `${opcao.meio} sem rótulo`)
      assert.doesNotMatch(opcao.texto, /—|recebe uma nova/)
    }
  }
})

// ------------------------------------------------------------------ ações reais, HTTP na fronteira

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
const PEDIDO_ID = '22222222-2222-2222-2222-222222222222'
const doServidor = (extra = {}) => ({
  pedidoId: PEDIDO_ID, status: 'aguardando_pagamento', total: 50, frete: 5, itens: [], totalPago: 0, pagamentos: [],
  cobranca: { cobrancaId: 'cb', provedor: 'mercadopago', status: 'Pendente', valor: 50, tentativa: 2,
    linkPagamento: 'https://mp/x', criadaEm: '2026-11-20T11:00:00Z', expiraEm: '2026-11-20T11:30:00Z' },
  ...extra,
})
function montar(pedido, responder) {
  const chamadas = []
  const despachados = []
  globalThis.fetch = async (url, opcoes = {}) => {
    chamadas.push({ url: String(url), metodo: opcoes.method ?? 'GET', corpo: opcoes.body ? JSON.parse(opcoes.body) : null })
    return responder(String(url), opcoes.method ?? 'GET')
  }
  const estadoRef = { current: { conversas: [{ id: 'conversa', pedido }] } }
  const acoes = criarAcoesComandaApi({}, { despachar: (a) => despachados.push(a), estadoRef })
  const avisos = () => despachados.filter((d) => d.mensagem).map((d) => d.mensagem)
  const posts = () => chamadas.filter((c) => c.metodo === 'POST')
  return { acoes, avisos, posts }
}
const ok = (data) => new Response(JSON.stringify({ data }))
const recusa = (message, status = 400) => new Response(JSON.stringify({ error: { code: 'VALIDATION_ERROR', message } }), { status })

await confere('5  do link vencido para o Pix direto vai ao EasyStok com a forma pix_direto', async () => {
  const t = montar(pedidoDaApi(doServidor()), (_url, metodo) => (metodo === 'POST' ? ok({}) : ok(doServidor())))
  await t.acoes.alterarMeioPagamento('conversa', cob.MEIO_PIX_DIRETO)
  assert.deepEqual(t.posts().map((c) => [c.url, c.corpo]), [[`/api/pedidos/${PEDIDO_ID}/cobranca/forma`, { forma: 'pix_direto' }]])
  assert.deepEqual(t.avisos(), [])
})

await confere('6  a recusa da API (link pendente, sem chave) aparece com a frase dela', async () => {
  for (const frase of [
    'Há uma cobrança online pendente. Confira o pagamento antes de receber manualmente.',
    'A loja do pedido não tem chave Pix cadastrada.',
  ]) {
    const t = montar(pedidoDaApi(doServidor()), (_url, metodo) => (metodo === 'POST' ? recusa(frase) : ok(doServidor())))
    await t.acoes.alterarMeioPagamento('conversa', cob.MEIO_PIX_DIRETO)
    assert.deepEqual(t.avisos(), [`Forma de pagamento: ${frase}`])
  }
})

await confere('7  a tela lê o Pix direto disponível e pergunta a forma quando há mais de uma', () => {
  const bloco = fonte('features/ficha-cliente/BlocoCobranca.jsx')
  const alterar = trecho(bloco, 'function AlterarForma(', '// Bloco Cobrança da ficha.')
  assert.match(alterar, /const pixDireto = usePixDireto\(\)/)
  assert.match(alterar, /trocasDeFormaNoEasyStok\(cobranca, nomeCliente, \{ pixDireto \}\)/)
  assert.match(alterar, /role="radiogroup"/)
})

console.log(`\n${passou} verificações ok`)
if (falhas.length) {
  console.error(`${falhas.length} falharam`)
  process.exit(1)
}
