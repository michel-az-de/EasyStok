// #2921 (fase 1): "Novo pedido" numa janela na própria tela. (1) abrir não troca hash, não seleciona e não anuncia;
// (2) o "+" soma na conversa da janela mesmo com outra selecionada, e a volta de 5 s a retém; (3) Registrar manda o
// corpo completo e falha mantém a montagem; (4) sem `atenderConversas` o botão não aparece. HTTP controlado, sem servidor.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
  load(url, c, next) { return url.endsWith('/infra/fonteDados.js') ? { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" } : next(url, c) },
})
const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')

// 1. O hook só prepara a conversa e abre a janela.
{
  const hook = fonte('aplicacao/novoPedidoDoCliente.js')
  assert.ok(!/window\.location/.test(hook), 'abrir o Novo pedido não mexe na hash')
  assert.ok(!/\bselecionar\b/.test(hook), 'abrir o Novo pedido não seleciona a conversa')
  assert.ok(!/anunciarAberturaDeConversa/.test(hook), 'abrir o Novo pedido não anuncia a abertura')
  assert.match(hook, /abrirJanelaDoPedido\(resumo/)
}

// 2. O "+" soma na conversa da janela; a conversa fica retida na volta do servidor e fora da fila.
const { somarNaJanela, ajustarNaJanela } = await import('../src/aplicacao/janelaDoPedido.js')
{
  const chamadas = []
  const acoes = { adicionarItem: (...a) => chamadas.push(['+', ...a]), ajustarQuantidade: (...a) => chamadas.push(['q', ...a]) }
  const janela = { conversaId: 'da-janela', clienteId: 'c1' }
  assert.equal(somarNaJanela(acoes, janela, { sku: 'bolo' }), true)
  assert.equal(ajustarNaJanela(acoes, janela, 'bolo', -1), true)
  assert.deepEqual(chamadas, [['+', 'da-janela', { sku: 'bolo' }], ['q', 'da-janela', 'bolo', -1]], 'a selecionada do Balcão não entra na conta')
  assert.equal(somarNaJanela(acoes, null, { sku: 'bolo' }), false)
  const { casosApi } = await import('../src/aplicacao/casos/api.js')
  const acao = await import('../src/aplicacao/acoes.js')
  const base = { conversas: [{ id: 'da-janela', pedido: { itens: [{ sku: 'bolo', qtd: 2 }] } }], selecionadaId: null, automaticoPausado: {}, sincronizacao: { estado: 'ok', mensagem: null } }
  const comJanela = casosApi[acao.SINCRONIZAR_CONVERSAS]({ ...base, janelaDoPedido: { conversaId: 'da-janela' } }, { conversas: [] })
  assert.deepEqual(comJanela.conversas.map(c => c.id), ['da-janela'], 'a volta de 5 s retém a conversa da janela')
  assert.equal(comJanela.conversas[0].pedido.itens[0].qtd, 2, 'e a montagem')
  const semJanela = casosApi[acao.SINCRONIZAR_CONVERSAS]({ ...base, janelaDoPedido: null }, { conversas: [] })
  assert.deepEqual(semJanela.conversas, [], 'fechada a janela, a conversa sai')
  const aberta = casosApi[acao.ABRIR_JANELA_DO_PEDIDO](base, { janela: { conversaId: 'da-janela' } })
  assert.equal(casosApi[acao.FECHAR_JANELA_DO_PEDIDO](aberta).janelaDoPedido, null)
  const provider = fonte('aplicacao/AtendimentoProvider.jsx')
  assert.match(provider, /conversasDoBalcao\(\s*conversasDaFila/, 'a conversa da janela fica fora do Balcão')
  assert.match(provider, /conversasEncerradasDoBalcao\(conversasDaFila/)
  assert.match(provider, /conversasBloqueadasDoBalcao\(conversasDaFila/)
}

// 3. Registrar: corpo completo; falha mantém a janela (devolve o erro, não fecha).
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const { registrarDaJanela } = await import('../src/aplicacao/janelaDoPedido.js')
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 60000, empresa: { id: 'e' } }) }
{
  const identidade = { clienteId: 'c1', vinculoRevisao: 3 }
  const pedido = { estado: 'aguardando', identidadeProposta: { ...identidade }, itens: [{ sku: 'bolo', qtd: 2, obs: 'sem nozes' }], janela: 'j1|2026-11-21', meio: 'pix' }
  const corpos = []
  let falhar = false
  globalThis.fetch = async (url, opcoes = {}) => {
    if (opcoes.method === 'POST') {
      corpos.push(JSON.parse(opcoes.body))
      return falhar ? new Response(JSON.stringify({ error: { message: 'Janela lotada' } }), { status: 422 }) : new Response(JSON.stringify({ data: { pedidoId: 'p', enviadoAoCliente: true } }))
    }
    return new Response(JSON.stringify({ data: { pedidoId: 'p', status: 'aguardando_pagamento', total: 50, frete: 0, itens: [], totalPago: 0, pagamentos: [] } }))
  }
  const construir = () => criarAcoesComandaApi({}, { despachar: () => {}, estadoRef: { current: { conversas: [{ id: 'conversa', ...identidade, pedido }] } } })
  const janela = { conversaId: 'conversa', clienteId: 'c1' }
  let registrou = 0
  const ok = await registrarDaJanela({ acoes: construir(), janela, meio: 'pix', enviarAoCliente: false, aoRegistrar: () => { registrou += 1 } })
  assert.equal(ok.ok, true); assert.equal(registrou, 1)
  const corpo = corpos[0]
  assert.equal(corpo.clienteId, 'c1'); assert.equal(corpo.vinculoRevisao, 3)
  assert.deepEqual(corpo.itens.map(i => [i.cardapioItemId, i.qtd, i.observacao]), [['bolo', 2, 'sem nozes']])
  assert.equal(corpo.janelaId, 'j1'); assert.equal(corpo.dataEntrega, '2026-11-21'); assert.ok(corpo.forma); assert.equal(corpo.enviarAoCliente, false)
  falhar = true
  const ruim = await registrarDaJanela({ acoes: construir(), janela, meio: 'pix', aoRegistrar: () => { registrou += 1 } })
  assert.ok(ruim.erro, 'a falha volta como erro'); assert.equal(registrou, 1, 'e não fecha a janela')
}

// 4. Sem `atenderConversas` o botão não aparece.
{
  const { novoPedidoDisponivel } = await import('../src/aplicacao/janelaDoPedido.js')
  const permite = () => true
  const abrir = () => {}
  assert.equal(novoPedidoDisponivel({ abrirConversaDoCliente: abrir, permite, acoes: { atenderConversas: true } }), true)
  assert.equal(novoPedidoDisponivel({ abrirConversaDoCliente: abrir, permite, acoes: { atenderConversas: false } }), false)
  assert.equal(novoPedidoDisponivel({ abrirConversaDoCliente: abrir, permite, acoes: {} }), false)
  assert.equal(novoPedidoDisponivel({ abrirConversaDoCliente: undefined, permite, acoes: { atenderConversas: true } }), false)
}
console.log('prova-2921-janela: 4 blocos ok')
