// #2944 Bloco C (E1-17 a E1-21): cancelar pedido em Pedidos, voltar do navegador, registrar atualiza, Cozinha, cliente sem celular.
//   node ferramentas/prova-2944-jornada.mjs
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const pc = await import('../src/dominio/pedidosCadastro.js')
const painel = fonte('features/gestao/cadastros/PainelPedidos.jsx')

await caso('17a. elegibilidade: entregue e cancelado dizem por que não cancelam; o resto cancela', () => {
  assert.equal(typeof pc.motivoParaNaoCancelar, 'function', 'motivoParaNaoCancelar ainda não existe')
  assert.equal(pc.motivoParaNaoCancelar('entregue'), 'Já foi entregue')
  assert.equal(pc.motivoParaNaoCancelar('cancelado'), 'Já foi cancelado')
  for (const s of ['aguardando_pagamento', 'aguardando', 'aprovado_baba', 'preparando', 'pronto', 'saiu_para_entrega', 'rascunho']) assert.equal(pc.motivoParaNaoCancelar(s), null, s)
})
await caso('17b. a linha oferece "Cancelar pedido" com motivo de bloqueio, confirmação e recarga da lista e da Ficha', () => {
  assert.match(painel, /Cancelar pedido/)
  assert.match(painel, /motivo=\{motivoParaNaoCancelar\(item\.status\)\}/)
  assert.match(painel, /cancelarPedido\(/)
  assert.match(painel, /Manter pedido/)
  assert.match(painel, /consulta\.recarregar\(\)/)
  assert.match(painel, /easystok:sincronizar-agora/)
})

// E1-18: voltar do navegador fecha só a janela. A regra é pura; o navegador entra por um duplo de `window`.
const duplo = () => {
  const ouvintes = {}
  const pilha = [{ estado: null, url: '#/atendimento' }]
  let i = 0
  const w = {
    location: { get hash() { return pilha[i].url } },
    addEventListener: (n, f) => { (ouvintes[n] ??= new Set()).add(f) },
    removeEventListener: (n, f) => { ouvintes[n]?.delete(f) },
    history: {
      get state() { return pilha[i].estado },
      pushState: (estado, _t, url) => { pilha.splice(i + 1); pilha.push({ estado, url: url ?? pilha[i].url }); i += 1 },
      back: () => { if (i > 0) { i -= 1; for (const f of [...(ouvintes.popstate ?? [])]) f({ state: pilha[i].estado }) } },
      forward: () => { if (i < pilha.length - 1) { i += 1; for (const f of [...(ouvintes.popstate ?? [])]) f({ state: pilha[i].estado }) } },
    },
  }
  return { w, pilha, indice: () => i }
}
await caso('18a. abrir empilha um estado sem mexer na hash; voltar fecha a janela', async () => {
  const h = await import('../src/aplicacao/historicoDaJanela.js').catch(() => ({}))
  assert.equal(typeof h.ligarVoltarDaJanela, 'function', 'ligarVoltarDaJanela ainda não existe')
  const { w, pilha, indice } = duplo()
  let fechou = 0
  h.ligarVoltarDaJanela(w, () => { fechou += 1 })
  assert.equal(pilha.length, 2); assert.equal(w.location.hash, '#/atendimento')
  w.history.back()
  assert.equal(fechou, 1); assert.equal(indice(), 0); assert.equal(w.location.hash, '#/atendimento')
  w.history.forward()
  assert.equal(fechou, 1, 'avançar não reabre nem fecha de novo')
})
await caso('18b. fechar pela tela desfaz a entrada do histórico sem fechar de novo', async () => {
  const h = await import('../src/aplicacao/historicoDaJanela.js').catch(() => ({}))
  const { w, indice } = duplo()
  let fechou = 0
  const desligar = h.ligarVoltarDaJanela(w, () => { fechou += 1 })
  desligar()
  assert.equal(fechou, 0); assert.equal(indice(), 0)
  const dois = duplo(); let n = 0
  const d2 = h.ligarVoltarDaJanela(dois.w, () => { n += 1 })
  dois.w.history.back(); d2()
  assert.equal(n, 1); assert.equal(dois.indice(), 0, 'já voltou: não volta outra vez')
})
await caso('18c. a janela liga o histórico e ignora o voltar enquanto registra', () => {
  const jsx = fonte('app/NovoPedidoJanela.jsx')
  assert.match(jsx, /ligarVoltarDaJanela\(/)
  assert.match(jsx, /registrandoRef/)
})
fim()
