// Prova da #1924 (parte do console). Medido antes na tela, no modo API contra o laboratório do master:
// depois do segundo link vencido a Ficha dizia "Gere uma cobrança nova ou cancele o pedido", mas a barra
// só tinha "Marcar pago" e o Pagamento só "Encerrar atendimento" e "Conferir pagamento"; e o caminho de
// gerar cobrança (`cobrar`) respondia "Link vencido é reemitido e enviado sozinho", falso desde o #1717-3.
//
//   1  no segundo vencimento a barra e o Pagamento oferecem "Mandar link novo"; antes disso, não;
//   2  o texto do link vencido aponta para o botão que existe;
//   3  "Mandar link novo" troca a forma para o link do Mercado Pago (o EasyStok gera e manda na conversa)
//      e avisa quando o link não saiu na conversa;
//   4  link ainda valendo não gera outro e o aviso não promete reemissão automática.
//
//   node ferramentas/prova-1924-link-novo.mjs

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

const cobranca = await import('../src/dominio/cobranca.js')
const { acaoDoPix } = await import('../src/features/ficha-cliente/acaoDoPix.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { criarAcoesComandaApi, LINK_NOVO_SEM_ENVIO, LINK_AINDA_VALE } = await import('../src/aplicacao/api/comanda.js')

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8').replaceAll(String.fromCharCode(13), '')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

// ------------------------------------------------------------------ 1 e 2

await confere('1  só o segundo link vencido, com o pedido esperando pagamento, pede link novo à atendente', () => {
  assert.equal(cobranca.esperaLinkNovoApi({ tentativa: 2 }, 'aguardando_pagamento'), true)
  assert.equal(cobranca.esperaLinkNovoApi({ tentativa: 1 }, 'aguardando_pagamento'), false, 'o job ainda manda o segundo')
  assert.equal(cobranca.esperaLinkNovoApi({ tentativa: 2 }, 'cancelado'), false)
  assert.equal(cobranca.esperaLinkNovoApi({ tentativa: 2 }, 'aguardando'), false, 'pedido já na fila: o link só venceu')
})
await confere('1  a barra oferece "Mandar link novo" e deixa "Marcar pago" como secundário', () => {
  const comBotao = acaoDoPix({ chave: 'expirada' }, { linkVencidoApi: 'texto', aoAbrirBaixa() {}, aoMandarLinkNovo() {} })
  assert.deepEqual(comBotao.botoes.map((b) => [b.rotulo, Boolean(b.secundario)]), [['Mandar link novo', false], ['Marcar pago', true]])
  const semBotao = acaoDoPix({ chave: 'expirada' }, { linkVencidoApi: 'texto', aoAbrirBaixa() {} })
  assert.deepEqual(semBotao.botoes.map((b) => b.rotulo), ['Marcar pago'])
})
await confere('1  a barra e o Pagamento ligam o botão pela mesma regra e pelo mesmo caminho do reenvio', () => {
  assert.match(fonte('features/ficha-cliente/BarraProximoPasso.jsx'),
    /aoMandarLinkNovo: fonteApi && pedido\.pedidoId && esperaLinkNovoApi\(pedido\.cobranca, pedido\.statusApi\)\s*\? \(\) => executar\(\(\) => aoReenviarPix\(\)\) : null/)
  assert.match(fonte('features/ficha-cliente/RelogioPix.jsx'),
    /\{fonteApi && esperaLinkNovoApi\(cobranca, statusApi\) && \(\s*<Botao largo variante="primario" icone="refresh-cw" onClick=\{aoReenviar\}>\{ROTULO_MANDAR_LINK_NOVO\}<\/Botao>/)
})
await confere('2  o texto do segundo vencimento manda usar o botão que existe', () => {
  const texto = cobranca.avisoDeLinkVencidoApi({ tentativa: 2 }, 'aguardando_pagamento')
  assert.match(texto, /Use Mandar link novo ou cancele o pedido\./)
  assert.doesNotMatch(texto, /Gere uma cobrança nova/)
})

// ------------------------------------------------------------------ 3 e 4: ações reais, HTTP na fronteira

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
const PEDIDO_ID = '11111111-1111-1111-1111-111111111111'
const iso = (deslocamentoMs) => new Date(Date.now() + deslocamentoMs).toISOString()
const doServidor = (cob) => ({
  pedidoId: PEDIDO_ID, status: 'aguardando_pagamento', total: 50, frete: 5, itens: [], totalPago: 0, pagamentos: [],
  cobranca: { cobrancaId: 'cb', provedor: 'mercadopago', valor: 50, linkPagamento: 'https://mp/x', criadaEm: iso(-3600000), ...cob },
})
const vencida = { status: 'Expirada', tentativa: 2, expiraEm: iso(-60000) }
const valendo = { status: 'Pendente', tentativa: 1, expiraEm: iso(20 * 60000) }

function montar(cob, respostaDaTroca) {
  const chamadas = []
  const despachados = []
  globalThis.fetch = async (url, opcoes = {}) => {
    const metodo = opcoes.method ?? 'GET'
    chamadas.push({ url: String(url), metodo, corpo: opcoes.body ? JSON.parse(opcoes.body) : null })
    if (metodo === 'POST' && String(url).endsWith('/cobranca/forma')) return respostaDaTroca()
    return new Response(JSON.stringify({ data: doServidor(cob) }))
  }
  const pedido = { ...pedidoDaApi(doServidor(cob)), meio: 'pix' }
  const estadoRef = { current: { conversas: [{ id: 'conversa', pedido }] } }
  const acoes = criarAcoesComandaApi({}, { despachar: (a) => despachados.push(a), estadoRef })
  return {
    acoes, pedido,
    avisos: () => despachados.filter((d) => d.mensagem).map((d) => d.mensagem),
    posts: () => chamadas.filter((c) => c.metodo === 'POST'),
  }
}
const troca = (enviadaNaConversa) => () => new Response(JSON.stringify({
  data: { cobranca: { cobrancaId: 'cb2', status: 'Pendente', linkPagamento: 'https://mp/novo', valor: 50, tentativa: 2 }, enviadaNaConversa },
}))

await confere('3  link vencido: "Mandar link novo" troca a forma para o link e o EasyStok manda na conversa', async () => {
  const t = montar(vencida, troca(true))
  await t.acoes.reenviarCobranca('conversa', t.pedido)
  assert.deepEqual(t.posts().map((p) => [p.url.replace(/^.*\/api/, '/api'), p.corpo]),
    [[`/api/pedidos/${PEDIDO_ID}/cobranca/forma`, { forma: 'online' }]])
  assert.ok(!t.avisos().some((a) => /reemitido e enviado sozinho|não saiu/.test(a)), t.avisos().join(' | '))
})
await confere('3  o link novo que não saiu na conversa vira aviso com o caminho (copiar em Pagamento)', async () => {
  const t = montar(vencida, troca(false))
  await t.acoes.gerarCobranca('conversa', t.pedido)
  assert.ok(t.avisos().includes(LINK_NOVO_SEM_ENVIO), t.avisos().join(' | '))
})
await confere('3  recusa do EasyStok aparece com o motivo', async () => {
  const t = montar(vencida, () => new Response(JSON.stringify({ error: { message: 'Pedido já pago.' } }), { status: 409 }))
  await t.acoes.reenviarCobranca('conversa', t.pedido)
  assert.ok(t.avisos().includes('Link novo: Pedido já pago.'), t.avisos().join(' | '))
})
await confere('4  link ainda valendo não gera outro e não promete reemissão automática', async () => {
  const t = montar(valendo, troca(true))
  await t.acoes.reenviarCobranca('conversa', t.pedido)
  assert.equal(t.posts().length, 0)
  assert.deepEqual(t.avisos(), [LINK_AINDA_VALE])
  assert.doesNotMatch(fonte('aplicacao/api/comanda.js'), /reemitido e enviado sozinho/)
})

if (falhas.length) {
  console.error(`\n${falhas.length} falha(s), ${passou} ok.`)
  process.exit(1)
}
console.log(`\n${passou} cenários da #1924 aprovados.`)
