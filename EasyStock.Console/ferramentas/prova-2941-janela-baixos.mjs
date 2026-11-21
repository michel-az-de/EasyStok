/* eslint-disable no-console */
// #2941: 4 achados Baixos da janela do Novo pedido (#2921). Um caso por comportamento, numerados.
//   1  avisos do cadastro de endereço dentro da janela; endereço exibido não fica velho depois de editar
//   2  comanda "de fora" (já existia ao abrir ou surgiu por fora) x montagem da própria janela
//   3  o aviso de "pedido em andamento" não pisca enquanto o Registrar está em curso
//   4  POST ok + recarga que falha: nova tentativa não cria segundo pedido
// Casos por regex do código (sem render de React no projeto): 1c, 2e, 2g, 3b, 4d. O resto roda a lógica de verdade.
//   node ferramentas/prova-2941-janela-baixos.mjs
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
  load(url, c, next) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return next(url, c)
  },
})
globalThis.sessionStorage = { getItem: () => null, setItem() {}, removeItem() {} }
const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const j = await import('../src/aplicacao/janelaDoPedido.js')
const acao = await import('../src/aplicacao/acoes.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')

let passou = 0
const falhas = []
const caso = async (nome, fn) => {
  try { await fn(); passou += 1; console.log(`ok    ${nome}`) } catch (e) { falhas.push(nome); console.log(`FALHA ${nome}\n      ${String(e.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`) }
}
const existe = (nome) => assert.equal(typeof j[nome], 'function', `${nome} ainda não existe em janelaDoPedido.js`)

// --- 1. Endereço -------------------------------------------------------------
await caso('1a. endereço salvo só aparece enquanto o campo ainda é o que foi salvo', () => {
  existe('enderecoVigente')
  assert.equal(j.enderecoVigente('Rua A, 1', 'Rua A, 1'), 'Rua A, 1')
  assert.equal(j.enderecoVigente('Rua A, 1', '  Rua A, 1 '), 'Rua A, 1')
})
await caso('1b. editou depois de salvar: o endereço velho não é mais mostrado como cadastrado', () => {
  existe('enderecoVigente')
  assert.equal(j.enderecoVigente('Rua A, 1', 'Rua B, 2'), null)
  assert.equal(j.enderecoVigente(null, 'Rua B, 2'), null)
})
await caso('1c. [regex] o aviso do cadastro aparece dentro da janela (AvisoDaAcaoApi no rodapé do Modal)', () => {
  const jsx = fonte('app/NovoPedidoJanela.jsx')
  assert.match(jsx, /import \{[^}]*AvisoDaAcaoApi[^}]*\} from '\.\/FaixaApi'/, 'AvisoDaAcaoApi não é importado')
  assert.match(jsx, /const rodape = <>\s*<AvisoDaAcaoApi \/>/, 'o rodapé da janela não renderiza <AvisoDaAcaoApi />')
  // #3076: a linha vale também para o endereço Padrão lido do dossiê; continua só quando o texto é o do cadastro.
  assert.match(jsx, /const enderecoDoPedido = enderecoVigente\(enderecoCadastrado \?\? conversa\.cliente\?\.endereco \?\? null, enderecoTexto\)/, 'o endereço do pedido não passa por enderecoVigente')
  assert.match(jsx, /\{enderecoValido && <p [^>]*>Entrega no endereço do cadastro: \{enderecoDoPedido\}<\/p>\}/, 'a linha do endereço do cadastro não sai de enderecoDoPedido')
})

// --- 2. Comanda de fora x montagem da própria janela ---------------------------
const comanda = (estado = 'rascunho', n = 1) => ({ pedido: { estado, itens: Array.from({ length: n }, (_, i) => ({ sku: `s${i}`, qtd: 1 })) } })
await caso('2a. comanda que já existia ao abrir (a janela não montou nada) é "de fora"', () => {
  existe('comandaDeFora'); assert.equal(j.comandaDeFora(comanda(), false), true)
})
await caso('2b. itens que a própria janela montou NÃO viram "comanda de fora" (bug já corrigido não volta)', () => {
  existe('comandaDeFora'); assert.equal(j.comandaDeFora(comanda(), true), false)
})
await caso('2c. conversa sem itens ou com pedido encerrado não é comanda de fora', () => {
  existe('comandaDeFora')
  assert.equal(j.comandaDeFora({ pedido: { itens: [] } }, false), false)
  assert.equal(j.comandaDeFora(comanda('entregue'), false), false)
})
await caso('2d. comanda que surge por fora com a janela aberta e vazia passa a ser "de fora"; ao sumir, deixa de ser', () => {
  existe('comandaDeFora')
  assert.equal(j.comandaDeFora({ pedido: { itens: [] } }, false), false)
  assert.equal(j.comandaDeFora(comanda(), false), true)
  assert.equal(j.comandaDeFora({ pedido: { itens: [] } }, false), false)
})
await caso('2e. [regex] a janela não congela a comanda no primeiro render e marca a montagem da própria janela', () => {
  const jsx = fonte('app/NovoPedidoJanela.jsx')
  assert.doesNotMatch(jsx, /useRef\(comandaEmAndamento\(/, 'ainda congela comandaEmAndamento num useRef no primeiro render')
  assert.doesNotMatch(jsx, /comandaDaConversa\.current/, 'ainda lê comandaDaConversa.current')
  assert.match(jsx, /comandaDeFora\(conversa, montouNaJanela\.current\)/, 'não deriva a comanda de fora a cada render')
  assert.match(jsx, /montouNaJanela\.current = true/, 'nunca marca a montagem da própria janela')
})

// --- 3. Aviso não pisca no Registrar -----------------------------------------
await caso('3a. enquanto registra, o aviso de pedido em andamento não aparece; fora disso aparece', () => {
  existe('mostrarAvisoDePedidoEmAndamento')
  assert.equal(j.mostrarAvisoDePedidoEmAndamento({ jaTemPedido: true, registrando: true }), false)
  assert.equal(j.mostrarAvisoDePedidoEmAndamento({ jaTemPedido: true, registrando: false }), true)
  assert.equal(j.mostrarAvisoDePedidoEmAndamento({ jaTemPedido: false, registrando: false }), false)
})
await caso('3b. [regex] a janela decide o aviso por mostrarAvisoDePedidoEmAndamento', () => {
  assert.match(fonte('app/NovoPedidoJanela.jsx'), /if \(mostrarAvisoDePedidoEmAndamento\(\{ jaTemPedido, registrando \}\)\)/)
})

// --- 4. POST ok, recarga falha: sem segundo pedido ----------------------------
const JANELA = '22222222-2222-2222-2222-222222222222'
const ITEM = '11111111-1111-1111-1111-111111111111'
const PEDIDO_ID = 'a1b2c3d4-e5f6-4711-8899-aabbccddeeff'
const IDENTIDADE = { clienteId: '33333333-3333-3333-3333-333333333333', vinculoRevisao: 2 }
const json = (data, status = 200) => new Response(JSON.stringify(status < 300 ? { data } : data), { status })
const pedidoApi = () => ({ pedidoId: PEDIDO_ID, status: 'aguardando_pagamento', itens: [{ cardapioItemId: ITEM, quantidade: 1 }], frete: 0, total: 50, cobranca: null })
function rede({ leituraOk }) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET' } = {}) => {
    chamadas.push(`${method} ${url}`)
    if (method === 'POST' && /\/conversas\/conv-1\/pedido$/.test(url)) return json({ pedidoId: PEDIDO_ID, cobranca: { cobrancaId: 'c' }, enviadoAoCliente: true })
    if (method === 'GET' && /\/conversas\/conv-1\/pedido$/.test(url) && leituraOk()) return json(pedidoApi())
    return json({ error: { code: 'X', message: 'fora do ar' } }, 500)
  }
  return () => chamadas.filter((c) => c.startsWith('POST')).length
}
function montar() {
  const despachados = []
  const rascunho = { numero: 1, pedidoId: null, identidadeProposta: { ...IDENTIDADE }, janela: `${JANELA}|2026-10-02`, meio: 'cartao-link', itens: [{ sku: ITEM, qtd: 1, obs: '' }] }
  const estadoRef = { current: { conversas: [{ id: 'conv-1', ...IDENTIDADE, pedido: rascunho }] } }
  const acoes = criarAcoesComandaApi({}, { despachar: (a) => { despachados.push(a) }, estadoRef })
  return { acoes, despachados, estadoRef }
}
await caso('4a. POST ok e recarga falha: a janela trata como registrado (sem erro que convide a reenviar)', async () => {
  rede({ leituraOk: () => false })
  const { acoes } = montar()
  const r = await j.registrarDaJanela({ acoes, janela: { conversaId: 'conv-1' }, meio: 'cartao-link' })
  assert.equal(r.erro, undefined, `a janela devolveu erro após o POST ok: ${r.erro}`)
  assert.equal(r.ok, true)
})
await caso('4b. POST ok, recarga falha, novo Registrar: continua UM POST só', async () => {
  const posts = rede({ leituraOk: () => false })
  const { acoes } = montar()
  await j.registrarDaJanela({ acoes, janela: { conversaId: 'conv-1' }, meio: 'cartao-link' })
  await j.registrarDaJanela({ acoes, janela: { conversaId: 'conv-1' }, meio: 'cartao-link' })
  assert.equal(posts(), 1, `POSTs do pedido: ${posts()} (duplicou)`)
})
await caso('4c. na nova tentativa a recarga que volta traz o pedido e a janela fecha ok, sem outro POST', async () => {
  let lendoOk = false
  const posts = rede({ leituraOk: () => lendoOk })
  const { acoes, despachados } = montar()
  await acoes.gerarPedido('conv-1', 'cartao-link')
  lendoOk = true
  const r = await j.registrarDaJanela({ acoes, janela: { conversaId: 'conv-1' }, meio: 'cartao-link' })
  assert.equal(r.ok, true)
  assert.equal(posts(), 1, `POSTs: ${posts()}`)
  assert.ok(despachados.some((d) => d.tipo === acao.SINCRONIZAR_PEDIDO), 'a tela não recebeu o pedido relido')
})
await caso('4d. [regex] criarPedido guarda o pedido criado sem recarga e o reconhece antes de postar', () => {
  assert.match(fonte('aplicacao/api/comanda.js'), /criadoSemRecarga/)
})
await caso('4e. a marca vale só para a comanda postada: comanda nova (outro número) posta de novo', async () => {
  const posts = rede({ leituraOk: () => false })
  const { acoes, estadoRef } = montar()
  await j.registrarDaJanela({ acoes, janela: { conversaId: 'conv-1' }, meio: 'cartao-link' })
  const conversa = estadoRef.current.conversas[0]
  estadoRef.current = { conversas: [{ ...conversa, pedido: { ...conversa.pedido, numero: 2 } }] }
  await j.registrarDaJanela({ acoes, janela: { conversaId: 'conv-1' }, meio: 'cartao-link' })
  assert.equal(posts(), 2, `POSTs: ${posts()} (a marca velha segurou a comanda nova)`)
})

// --- 2 (revisão). "+" sobre comanda de fora não transforma os itens dela em montagem da janela --
await caso('2f. o "+" só marca a montagem da janela quando não há comanda de fora', () => {
  existe('somaEhDaJanela')
  assert.equal(j.somaEhDaJanela(comanda(), false), false)
  assert.equal(j.somaEhDaJanela({ pedido: { itens: [] } }, false), true)
  assert.equal(j.somaEhDaJanela(comanda(), true), true)
})
await caso('2g. [regex] o "+" da janela passa por somaEhDaJanela antes de marcar a montagem', () => {
  assert.match(fonte('app/NovoPedidoJanela.jsx'), /if \(somaEhDaJanela\(conversa, montouNaJanela\.current\)\) montouNaJanela\.current = true/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
