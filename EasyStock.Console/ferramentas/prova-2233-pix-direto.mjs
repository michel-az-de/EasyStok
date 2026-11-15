/* eslint-disable no-console */
// Prova da #2233 (Pix direto pela chave da loja, sem Mercado Pago), na parte do console:
// o chip só existe com a API dizendo que o Pix direto está disponível (flag e chave) e com o
// pedido já criado; fora do catálogo fixo, então demonstração e cardápio público não mudam.
// A cobrança "pix_direto" da API vira o meio "pix-direto", com baixa à mão e o copia e cola.
//
//   node ferramentas/prova-2233-pix-direto.mjs

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
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})

const cob = await import('../src/dominio/cobranca.js')
const { MEIOS_DE_PAGAMENTO } = await import('../src/infra/catalogo.js')
const { formaDoMeio, pedidoDaApi } = await import('../src/infra/api/comandaApi.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const COPIA_E_COLA = '00020126330014BR.GOV.BCB.PIX0111loja@exemplo.test5204000053039865405' // fictício, só o formato

confere('o catálogo fixo não tem Pix direto: demonstração e cardápio público seguem iguais', () => {
  assert.ok(!MEIOS_DE_PAGAMENTO.some((m) => m.id === cob.MEIO_PIX_DIRETO))
})

confere('chip só com Pix direto disponível e pedido criado', () => {
  const ids = (opcoes) => cob.meiosDaEscolha(MEIOS_DE_PAGAMENTO, opcoes).map((m) => m.id)
  assert.ok(!ids({ pixDireto: false, pedidoCriado: true }).includes(cob.MEIO_PIX_DIRETO), 'flag ou chave faltando')
  assert.ok(!ids({ pixDireto: true, pedidoCriado: false }).includes(cob.MEIO_PIX_DIRETO), 'pedido ainda não criado')
  const com = cob.meiosDaEscolha(MEIOS_DE_PAGAMENTO, { pixDireto: true, pedidoCriado: true })
  const pix = com.find((m) => m.id === cob.MEIO_PIX_DIRETO)
  assert.ok(pix?.destaque, 'Pix direto ao lado do link, fora da linha "Na entrega"')
  assert.equal(com.length, MEIOS_DE_PAGAMENTO.length + 1)
})

confere('o meio vira a forma pix_direto da API; os outros não mudam', () => {
  assert.equal(formaDoMeio(cob.MEIO_PIX_DIRETO), 'pix_direto')
  assert.equal(formaDoMeio('cartao-link'), 'online')
  assert.equal(formaDoMeio('maquininha'), 'na_entrega')
  assert.equal(cob.nomeDoMeio(cob.MEIO_PIX_DIRETO), 'Pix na chave da loja')
})

confere('cobrança pix_direto da API: meio pix-direto, baixa à mão, copia e cola, sem link', () => {
  const pedido = pedidoDaApi({
    pedidoId: 'p1', status: 'aguardando_pagamento', itens: [], total: 57.9,
    cobranca: { cobrancaId: 'c1', provedor: 'pix_direto', valor: 57.9, status: 'Pendente', pixCopiaCola: COPIA_E_COLA },
  }, { meio: 'maquininha' })
  assert.equal(pedido.cobranca.meio, cob.MEIO_PIX_DIRETO)
  assert.equal(pedido.cobranca.manual, true)
  assert.equal(pedido.cobranca.copiaECola, COPIA_E_COLA)
  assert.equal(pedido.cobranca.link, null)
  assert.equal(pedido.meio, cob.MEIO_PIX_DIRETO)
  assert.equal(cob.baixaCancelaCobrancaOnline(pedido), false, '"Marcar pago" não troca para na entrega antes')
})

confere('voltar do Pix direto: cobrança na entrega e online seguem como antes', () => {
  const naEntrega = pedidoDaApi({
    pedidoId: 'p1', status: 'em_fila', itens: [], total: 10,
    cobranca: { cobrancaId: 'c2', provedor: 'na_entrega', valor: 10, status: 'Pendente' },
  }, { meio: cob.MEIO_PIX_DIRETO })
  assert.equal(naEntrega.cobranca.meio, 'maquininha')
  assert.equal(naEntrega.cobranca.copiaECola, null)
  const online = pedidoDaApi({
    pedidoId: 'p1', status: 'aguardando_pagamento', itens: [], total: 10,
    cobranca: { cobrancaId: 'c3', provedor: 'mercadopago', valor: 10, status: 'Pendente', linkPagamento: 'https://mp.exemplo/1' },
  }, { meio: cob.MEIO_PIX_DIRETO })
  assert.equal(online.cobranca.meio, 'cartao-link')
})

console.log(`\n${passou} verificações ok`)
