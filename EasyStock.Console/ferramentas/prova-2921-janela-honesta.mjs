// #2921: a janela do Novo pedido não finge registro nem apaga o que não é dela. (A) `criarPedido` devolve undefined
// quando a conversa já tem pedido: Registrar volta `{ erro }` e a janela avisa já na abertura; (B) comanda que já estava
// na conversa não vira rascunho nem some no "Descartar"; (C) o rascunho só é restaurado com o cardápio carregado.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
  load(url, c, next) { return url.endsWith('/infra/fonteDados.js') ? { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" } : next(url, c) },
})
const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const j = await import('../src/aplicacao/janelaDoPedido.js')
const jsx = fonte('app/NovoPedidoJanela.jsx')

// A. Nada criado nunca vira sucesso, e o rascunho fica.
{
  let registrou = 0
  const janela = { conversaId: 'c', clienteId: 'k' }
  for (const resposta of [undefined, null]) {
    const r = await j.registrarDaJanela({ acoes: { gerarPedido: async () => resposta }, janela, meio: 'pix', aoRegistrar: () => { registrou += 1 } })
    assert.ok(r.erro && !r.ok, 'sem pedido na resposta é erro')
  }
  assert.equal(registrou, 0, 'não fecha nem apaga o rascunho')
  const ok = await j.registrarDaJanela({ acoes: { gerarPedido: async () => ({ pedidoId: 'p' }) }, janela, meio: 'pix', aoRegistrar: () => { registrou += 1 } })
  assert.equal(ok.ok, true); assert.equal(registrou, 1)
  assert.equal(j.pedidoJaCriadoNaConversa({ pedido: { pedidoId: 'p' } }), true)
  assert.equal(j.pedidoJaCriadoNaConversa({ pedido: { itens: [] } }), false)
  assert.equal(j.pedidoJaCriadoNaConversa(null), false)
  assert.match(jsx, /pedidoJaCriadoNaConversa\(conversa\)/, 'a janela checa na abertura')
  assert.match(jsx, /AVISO_PEDIDO_EM_ANDAMENTO/)
}

// B. Comanda pré-existente: não grava rascunho e não oferece "Descartar".
{
  assert.equal(j.comandaEmAndamento({ pedido: { itens: [{ sku: 'a', qtd: 1 }] } }), true)
  assert.equal(j.comandaEmAndamento({ pedido: { itens: [] } }), false)
  assert.match(jsx, /comandaDaConversa/, 'a janela lembra a comanda que já existia')
  assert.match(jsx, /!comandaDaConversa\.current && itens\.length > 0/, 'Descartar some com comanda alheia')
  assert.ok(jsx.includes('!restaurado.current || comandaDaConversa.current || jaTemPedido || registrando'), 'o efeito não grava a comanda alheia')
}

// C. Só restaura com o cardápio carregado.
{
  assert.equal(j.cardapioPronto([]), false)
  assert.equal(j.cardapioPronto(undefined), false)
  assert.equal(j.cardapioPronto([{ sku: 'a' }]), true)
  assert.ok(jsx.includes('guardado && !cardapioPronto(cardapio)) return'), 'sem cardápio, não restaura nem marca restaurado')
  assert.ok(jsx.includes('}, [cardapio])'), 'e tenta de novo quando o cardápio chega')
}
console.log('prova-2921-janela-honesta: 3 blocos ok')
