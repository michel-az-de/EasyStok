/* eslint-disable no-console */
// Prova da #2832: a Gestão da loja abre no painel "Agora" (Turno de hoje), com cardápio do dia, caixa, pedidos de
// hoje, o que precisa de você e as telas que você mais usa. As regras são puras (dominio/turnoDeHoje); a tela só
// desenha. A prova cobre:
//   1. o cardápio do dia lista o que está no ar e o que saiu, sem o removido e o item em validação;
//   2. o caixa soma o recebido de hoje por forma, com o estorno descontando;
//   3. a situação do caixa (fechado, esquecido aberto, aberto, ainda não aberto);
//   4. as pendências saem do caixa, dos pedidos esperando pagamento e do prato sem saldo, e só as da tela que a
//      pessoa pode abrir;
//   5. "Você mais usa" conta o uso por tela e devolve só telas que existem para a pessoa;
//   6. a Gestão abre em "Agora" no modo API e mantém "Todas as telas".
//
//   node ferramentas/prova-2832-turno-de-hoje.mjs

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
})

const turno = await import('../src/dominio/turnoDeHoje.js')

// 1. Cardápio do dia.
const cardapio = [
  { sku: 'a', nome: 'Lasanha bolonhesa', estoque: 2, disponivelHoje: true },
  { sku: 'b', nome: 'Nhoque ao sugo', estoque: 7 },
  { sku: 'c', nome: 'Lasanha 4 queijos', estoque: 5, disponivelHoje: false },
  { sku: 'd', nome: 'Torta antiga', estoque: 3, removidoEm: '2026-11-01T10:00:00Z' },
  { sku: 'e', nome: 'Prato novo', estoque: 4, emValidacao: true },
  { sku: 'f', nome: 'Quiche', estoque: 0 },
  { sku: 'g', nome: 'Pão de queijo', estoque: null },
]
const dia = turno.cardapioDoDia(cardapio)
assert.deepEqual(dia.itens.map((i) => i.sku), ['a', 'b', 'c', 'f', 'g'], 'sem o removido e o item em validação')
assert.equal(dia.noAr, 4, 'no ar é o que a loja vende hoje, saldo zero incluído')
assert.equal(dia.itens.find((i) => i.sku === 'c').noAr, false)
assert.equal(dia.itens.find((i) => i.sku === 'c').rotulo, 'Fora do cardápio de hoje')
assert.equal(dia.itens.find((i) => i.sku === 'a').tom, 'aviso', 'pouco saldo pede atenção')
assert.equal(dia.itens.find((i) => i.sku === 'f').rotulo, 'Esgotado hoje')

// 2. Recebido por forma.
const caixa = {
  aberto: true, fechado: false, esquecidoAberto: false, aberturaAConfirmar: false, saldoEsperado: 1240.5,
  linhasExtras: [
    { tipo: 'pagamento', valor: 800, meio: 'pix' },
    { tipo: 'venda', valor: 12, meio: 'pix' },
    { tipo: 'venda', valor: 318.5, meio: 'cartao_credito' },
    { tipo: 'venda', valor: 120, meio: 'dinheiro' },
    { tipo: 'estorno', valor: -10, meio: 'dinheiro' },
    { tipo: 'venda', valor: 0, meio: 'outro' },
  ],
}
const formas = turno.recebidoPorForma(caixa)
assert.deepEqual(formas.map((f) => [f.metodo, f.valor]), [['pix', 812], ['cartao_credito', 318.5], ['dinheiro', 110]],
  'soma por forma, estorno desconta, zero some, maior primeiro')
assert.ok(formas.every((f) => typeof f.nome === 'string' && f.nome.length > 0))
assert.deepEqual(turno.recebidoPorForma(null), [])

// 3. Situação do caixa.
assert.equal(turno.situacaoDoCaixa(caixa).rotulo, 'Aberto')
assert.equal(turno.situacaoDoCaixa({ ...caixa, aberto: false, fechado: true }).rotulo, 'Fechado')
assert.equal(turno.situacaoDoCaixa({ ...caixa, aberto: false }).rotulo, 'Não aberto')
assert.match(turno.situacaoDoCaixa({ ...caixa, esquecidoAberto: true, abertoDesde: '2026-11-17' }).rotulo, /17\/11/)

// 4. Pendências.
const todas = new Set(['caixa', 'cadastros-pedidos', 'estoque-do-dia'])
const pend = turno.pendenciasDoTurno({
  caixa: { ...caixa, esquecidoAberto: true, abertoDesde: '2026-11-17' },
  pedidos: { pagamento: 2 },
  cardapio,
  abas: todas,
})
assert.deepEqual(pend.map((p) => p.chave), ['caixa-esquecido', 'pedidos-pagamento', 'sem-saldo'])
assert.match(pend[1].titulo, /^2 pedidos esperando pagamento$/)
assert.match(pend[2].detalhe, /Quiche/, 'nomeia o prato sem saldo que ainda está no ar')
assert.doesNotMatch(pend[2].detalhe, /4 queijos/, 'o que já saiu do cardápio não conta')
assert.ok(pend.every((p) => todas.has(p.aba)), 'toda pendência abre uma tela')

const umPedido = turno.pendenciasDoTurno({ caixa: { ...caixa, aberto: false }, pedidos: { pagamento: 1 }, cardapio: [], abas: todas })
assert.deepEqual(umPedido.map((p) => p.chave), ['caixa-nao-aberto', 'pedidos-pagamento'])
assert.equal(umPedido[1].titulo, '1 pedido esperando pagamento')

assert.deepEqual(turno.pendenciasDoTurno({ caixa: { ...caixa, aberturaAConfirmar: true }, pedidos: {}, cardapio: [], abas: todas })
  .map((p) => p.chave), ['caixa-conferir'])

const semCaixa = turno.pendenciasDoTurno({ caixa: { ...caixa, aberto: false }, pedidos: { pagamento: 3 }, cardapio, abas: new Set(['estoque-do-dia']) })
assert.deepEqual(semCaixa.map((p) => p.chave), ['sem-saldo'], 'sem a tela do caixa e dos pedidos, a pendência deles não aparece')

assert.deepEqual(turno.pendenciasDoTurno({ caixa: null, pedidos: null, cardapio: [], abas: todas }), [], 'sem dado, sem pendência')

// 5. Você mais usa.
let uso = {}
for (const aba of ['cardapio', 'caixa', 'cardapio', 'estoque-do-dia', 'cardapio', 'caixa', 'sumida'])
  uso = turno.registrarUso(uso, aba)
assert.deepEqual(uso, { cardapio: 3, caixa: 2, 'estoque-do-dia': 1, sumida: 1 })
const telas = [
  { id: 'itens', aba: 'cardapio', rotulo: 'Itens do cardápio' },
  { id: 'caixa', aba: 'caixa', rotulo: 'Caixa do dia' },
  { id: 'estoque', aba: 'estoque-do-dia', rotulo: 'Estoque do dia' },
]
assert.deepEqual(turno.maisUsadas(uso, telas, 2).map((t) => t.aba), ['cardapio', 'caixa'], 'mais usadas primeiro, sem a tela que a pessoa não tem')
assert.deepEqual(turno.maisUsadas(null, telas), [])
assert.deepEqual(turno.registrarUso({ caixa: 'x' }, 'caixa'), { caixa: 1 }, 'contagem estragada recomeça')

// 6. A Gestão abre em "Agora".
const modal = readFileSync(new URL('../src/features/gestao/ModalGestao.jsx', import.meta.url), 'utf8')
assert.match(modal, /PainelTurnoDeHoje/, 'a Gestão monta o painel Turno de hoje')
assert.match(modal, />Agora</, 'tem a aba Agora')
assert.match(modal, />Todas as telas</, 'e mantém Todas as telas')
const painel = readFileSync(new URL('../src/features/gestao/turno/PainelTurnoDeHoje.jsx', import.meta.url), 'utf8')
assert.match(painel, /alternarDisponibilidade/, 'Esgotou e Voltar usam a mesma ação do cardápio do balcão')
assert.match(painel, /usePedidosDeHoje/, 'os pedidos de hoje vêm do hook que relê ao vivo')
assert.match(painel, /useCaixaDoDiaApi/, 'o caixa vem do mesmo hook da aba Caixa')

console.log('prova-2832-turno-de-hoje ok: cardápio, caixa, pedidos, pendências e mais usadas')
