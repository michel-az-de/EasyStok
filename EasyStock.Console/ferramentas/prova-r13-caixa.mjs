/* eslint-disable no-console */
// Prova da issue #44 (rodada 13): caixa do dia amarrado ao modelo do EasyStok
// (lançamentos MovimentoCaixa, pagamento de pedido somado ao resumo sem virar
// lançamento, FechamentoCaixa como snapshot, estorno soft bloqueado com o dia
// fechado) e venda avulsa (UC-11) entrando na esteira e no estoque.
//
// Exercita o domínio puro (`dominio/caixa.js`) e o reducer de verdade, na
// mesma sequência de despachos que a tela (`AbaCaixa.jsx`) faz.
//
//   node ferramentas/prova-r13-caixa.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const dom = await import('../src/dominio/caixa.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const cob = await import('../src/dominio/cobranca.js')
const catalogo = await import('../src/infra/catalogo.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const AGORA = Date.parse('2026-09-27T11:00:00-03:00')
const MINUTO = 60000
const HORA = 60 * MINUTO

// --- Domínio puro --------------------------------------------------------------

confere('dia nunca aberto: não pode lançar nem fechar, pode abrir', () => {
  assert.equal(dom.podeAbrirCaixa([], AGORA), true)
  assert.equal(dom.podeLancarNoCaixa([], AGORA), false)
  assert.equal(dom.caixaAberto([], AGORA), false)
})

const abertura = dom.abrirCaixaMovimento(50, AGORA, 'Thatiane')
confere('abrir o caixa grava o saldo inicial e trava reabrir no mesmo dia', () => {
  assert.equal(abertura.tipo, 'abertura')
  assert.equal(abertura.valor, 50)
  assert.equal(dom.podeAbrirCaixa([abertura], AGORA), false)
  assert.equal(dom.podeLancarNoCaixa([abertura], AGORA), true)
})

confere('criarMovimentoCaixa recusa sem categoria, sem valor ou valor zero', () => {
  assert.equal(dom.criarMovimentoCaixa({
    tipo: 'saida', categoria: '', valor: 10, agora: AGORA, autorNome: 'x',
  }), null)
  assert.equal(dom.criarMovimentoCaixa({
    tipo: 'saida', categoria: 'Sangria', valor: 0, agora: AGORA, autorNome: 'x',
  }), null)
  assert.equal(dom.criarMovimentoCaixa({
    tipo: 'abertura', categoria: 'Sangria', valor: 10, agora: AGORA, autorNome: 'x',
  }), null)
})

const sangria = dom.criarMovimentoCaixa({
  tipo: 'saida', categoria: 'Sangria', valor: 30, meio: 'dinheiro', agora: AGORA + HORA, autorNome: 'Thatiane',
})
confere('sangria é uma saída com categoria e método do caixa (não um tipo à parte)', () => {
  assert.equal(sangria.tipo, 'saida')
  assert.equal(sangria.categoria, 'Sangria')
  assert.equal(sangria.meio, 'dinheiro')
  assert.equal(sangria.estornadoEm, null)
})

confere('estornar exige motivo, e abertura/fechamento nunca se estornam', () => {
  assert.equal(dom.estornarMovimentoCaixa(sangria, AGORA, '  ', 'Thatiane', false), sangria)
  assert.equal(dom.estornarMovimentoCaixa(abertura, AGORA, 'Motivo válido', 'Thatiane', false), abertura)
  const estornada = dom.estornarMovimentoCaixa(sangria, AGORA + 2 * HORA, 'Lancei errado', 'Thatiane', false)
  assert.ok(estornada.estornadoEm)
  assert.equal(estornada.estornadoPorNome, 'Thatiane')
  assert.equal(estornada.motivoEstorno, 'Lancei errado')
})

confere('mapa EasyStok: dia fechado bloqueia o estorno do lançamento', () => {
  const bloqueado = dom.estornarMovimentoCaixa(sangria, AGORA + 2 * HORA, 'Lancei errado', 'Thatiane', true)
  assert.equal(bloqueado, sangria)
})

confere('metodoCaixaDoMeio traduz os meios da cobrança por conversa para os 6 do caixa', () => {
  assert.equal(dom.metodoCaixaDoMeio('pix'), 'pix')
  assert.equal(dom.metodoCaixaDoMeio('maquininha'), 'credito')
  assert.equal(dom.metodoCaixaDoMeio('cartao-link'), 'credito')
  assert.equal(dom.metodoCaixaDoMeio('vale-refeicao'), 'outro')
  assert.equal(dom.metodoCaixaDoMeio('dinheiro'), 'dinheiro')
})

// --- Reducer: dia, lançamentos e estorno ---------------------------------------

const estadoBase = () => estadoInicial({
  conversas: [], catalogo: { cardapio: catalogo.CARDAPIO.map((i) => ({ ...i })) }, regras: [], modoAgente: 'sugerir',
})

confere('ABRIR_CAIXA grava a abertura; lançar antes de abrir não faz nada', () => {
  const antes = estadoBase()
  const semAbrir = reducer(antes, {
    tipo: acao.LANCAR_MOVIMENTO_CAIXA, agora: AGORA, tipoMovimento: 'saida', categoria: 'Despesa', valor: 10, meio: 'dinheiro',
  })
  assert.equal(semAbrir, antes)
  const aberto = reducer(antes, { tipo: acao.ABRIR_CAIXA, agora: AGORA, saldoInicial: 100 })
  assert.equal(aberto.caixa.movimentos.length, 1)
  assert.equal(aberto.caixa.movimentos[0].valor, 100)
})

let estado = reducer(estadoBase(), { tipo: acao.ABRIR_CAIXA, agora: AGORA, saldoInicial: 100 })

confere('lançar entrada e saída com categoria muda o resumo em tempo real', () => {
  estado = reducer(estado, {
    tipo: acao.LANCAR_MOVIMENTO_CAIXA, agora: AGORA + MINUTO, tipoMovimento: 'entrada',
    categoria: 'Reforço de troco', valor: 20, meio: 'dinheiro',
  })
  estado = reducer(estado, {
    tipo: acao.LANCAR_MOVIMENTO_CAIXA, agora: AGORA + 2 * MINUTO, tipoMovimento: 'saida',
    categoria: 'Sangria', valor: 15, meio: 'dinheiro',
  })
  const resumo = dom.resumoCaixa(estado.caixa.movimentos, estado.conversas, estado.catalogo.cardapio, AGORA + 2 * MINUTO)
  assert.equal(resumo.totalEntradasExtras, 20)
  assert.equal(resumo.totalSaidasExtras, 15)
  assert.equal(resumo.saldoEsperado, 100 + 20 - 15)
})

confere('ESTORNAR_MOVIMENTO_CAIXA reduz o saldo esperado de volta e guarda a trilha', () => {
  const sangriaLancada = estado.caixa.movimentos.find((m) => m.categoria === 'Sangria')
  estado = reducer(estado, {
    tipo: acao.ESTORNAR_MOVIMENTO_CAIXA, agora: AGORA + 3 * MINUTO, movimentoId: sangriaLancada.id, motivo: 'Lancei errado',
  })
  const resumo = dom.resumoCaixa(estado.caixa.movimentos, estado.conversas, estado.catalogo.cardapio, AGORA + 3 * MINUTO)
  assert.equal(resumo.totalSaidasExtras, 0)
  assert.equal(resumo.saldoEsperado, 120)
  const registro = estado.caixa.movimentos.find((m) => m.id === sangriaLancada.id)
  assert.equal(registro.motivoEstorno, 'Lancei errado')
})

confere('estornar sem motivo, ou lançamento inexistente, não muda o estado', () => {
  const antes = estado
  const semMotivo = reducer(estado, {
    tipo: acao.ESTORNAR_MOVIMENTO_CAIXA, agora: AGORA, movimentoId: estado.caixa.movimentos[0].id, motivo: '',
  })
  assert.equal(semMotivo, antes)
  const semAlvo = reducer(estado, {
    tipo: acao.ESTORNAR_MOVIMENTO_CAIXA, agora: AGORA, movimentoId: 'nao-existe', motivo: 'x',
  })
  assert.equal(semAlvo, antes)
})

// --- Reducer: venda avulsa (UC-11, RN-26) --------------------------------------

const itemAntes = estado.catalogo.cardapio.find((i) => i.sku === 'LAS-CLA')
estado = reducer(estado, {
  tipo: acao.LANCAR_VENDA_AVULSA, agora: AGORA + 10 * MINUTO, nome: 'Cliente do balcão',
  itens: [{ sku: 'LAS-CLA', qtd: 2 }], meio: 'dinheiro',
})
const conversaAvulsa = estado.conversas.find((c) => c.canal === 'Balcão')

confere('venda avulsa cria o pedido já pago, direto na esteira (RN-26)', () => {
  assert.ok(conversaAvulsa)
  assert.equal(conversaAvulsa.pedido.estado, 'pago')
  assert.equal(conversaAvulsa.pedido.vendaAvulsa, true)
  assert.equal(conversaAvulsa.pedido.itens.find((l) => l.sku === 'LAS-CLA').qtd, 2)
  assert.equal(conversaAvulsa.pedido.cobranca.valor, itemAntes.preco * 2)
})

confere('venda avulsa baixa o mesmo estoque do cardápio (RN-26)', () => {
  const itemDepois = estado.catalogo.cardapio.find((i) => i.sku === 'LAS-CLA')
  assert.equal(itemDepois.estoque, itemAntes.estoque - 2)
})

confere('venda avulsa entra no resumo do caixa como pagamento de pedido, não como lançamento', () => {
  const movimentosAntes = estado.caixa.movimentos.length
  const resumo = dom.resumoCaixa(estado.caixa.movimentos, estado.conversas, estado.catalogo.cardapio, AGORA + 10 * MINUTO)
  assert.equal(estado.caixa.movimentos.length, movimentosAntes) // nenhum MovimentoCaixa novo
  assert.equal(resumo.totalPagamentosPedidos, itemAntes.preco * 2)
  const linhaDinheiro = resumo.porMetodo.find((l) => l.metodo === 'dinheiro')
  assert.ok(linhaDinheiro.valor >= itemAntes.preco * 2)
})

confere('estoque zerado não bloqueia a venda avulsa (E1 de UC-11, D7/RN-48)', () => {
  const zerado = { ...estadoBase(), catalogo: { cardapio: catalogo.CARDAPIO.map((i) => (i.sku === 'LAS-VER' ? { ...i, estoque: 0 } : i)) } }
  const aberto = reducer(zerado, { tipo: acao.ABRIR_CAIXA, agora: AGORA, saldoInicial: 0 })
  const depois = reducer(aberto, {
    tipo: acao.LANCAR_VENDA_AVULSA, agora: AGORA + MINUTO, nome: 'Teste', itens: [{ sku: 'LAS-VER', qtd: 1 }], meio: 'pix',
  })
  const conversa = depois.conversas.find((c) => c.canal === 'Balcão')
  assert.equal(conversa.pedido.itens[0].qtd, 1)
  assert.ok(depois.alertasEstoque.length > 0)
})

confere('venda avulsa sem item válido, ou com o dia fechado, não faz nada', () => {
  const semItem = reducer(estado, {
    tipo: acao.LANCAR_VENDA_AVULSA, agora: AGORA + 11 * MINUTO, nome: 'x', itens: [], meio: 'pix',
  })
  assert.equal(semItem, estado)
})

// --- Reducer: fechar o dia e o estorno de pedido a partir do caixa -------------

confere('pedido pago pelo WhatsApp (cobrança normal) também soma no resumo do caixa', () => {
  const conversaWhats = {
    id: 'c-whats', cadastroId: 'c-whats', conta: 'cliente', nome: 'José', canal: 'WhatsApp',
    estado: 'Em atendimento', responsavel: 'Thatiane', mensagens: [], bloqueio: null,
    cliente: { notas: [], tags: [] },
    pedido: {
      numero: '2026-0500', estado: 'pago', janela: 'j1', entregador: null, itens: [{ sku: 'RAV-LIM', qtd: 1, obs: '' }], pagamentos: [],
      cobranca: cob.aplicarPagamento(
        cob.criarCobranca(
          { numero: '2026-0500', itens: [{ sku: 'RAV-LIM', qtd: 1, obs: '' }] },
          estado.catalogo.cardapio, AGORA + 12 * MINUTO,
          { identificador: 'tx-1', copiaECola: null, link: null, meio: 'maquininha' },
        ),
        AGORA + 12 * MINUTO, null,
      ),
    },
  }
  const comConversaWhats = { ...estado, conversas: [...estado.conversas, conversaWhats] }
  const resumo = dom.resumoCaixa(comConversaWhats.caixa.movimentos, comConversaWhats.conversas, comConversaWhats.catalogo.cardapio, AGORA + 12 * MINUTO)
  const item = comConversaWhats.catalogo.cardapio.find((i) => i.sku === 'RAV-LIM')
  const linhaCredito = resumo.porMetodo.find((l) => l.metodo === 'credito')
  assert.ok(linhaCredito.valor >= item.preco)

  const vendas = dom.vendasDoDia(comConversaWhats.conversas, AGORA + 12 * MINUTO)
  const linha = vendas.find((v) => v.conversaId === 'c-whats')
  assert.equal(linha.meio, 'credito')
  assert.equal(linha.estornado, false)

  // Estorno "a partir do caixa" (aceite): reaproveita marcarEstorno, que já
  // existe (não é ação nova desta frente).
  const estornado = reducer(comConversaWhats, {
    tipo: acao.MARCAR_ESTORNO, id: 'c-whats', agora: AGORA + 13 * MINUTO, motivo: 'Cliente desistiu', valor: null, notaId: 'nota-1',
  })
  const resumoDepois = dom.resumoCaixa(estornado.caixa.movimentos, estornado.conversas, estornado.catalogo.cardapio, AGORA + 13 * MINUTO)
  assert.equal(resumoDepois.totalPagamentosPedidos, resumo.totalPagamentosPedidos - item.preco)
})

confere('fechar o dia grava o FechamentoCaixa com a diferença da conferência', () => {
  const resumoAntes = dom.resumoCaixa(estado.caixa.movimentos, estado.conversas, estado.catalogo.cardapio, AGORA + 20 * MINUTO)
  const fechado = reducer(estado, { tipo: acao.FECHAR_CAIXA, agora: AGORA + 20 * MINUTO, contado: resumoAntes.saldoEsperado - 5 })
  const marcador = fechado.caixa.movimentos.find((m) => m.tipo === 'fechamento')
  assert.ok(marcador)
  assert.equal(marcador.fechamento.saldoFinal, resumoAntes.saldoEsperado)
  assert.equal(marcador.fechamento.diferenca, -5)
})

const fechado = reducer(estado, { tipo: acao.FECHAR_CAIXA, agora: AGORA + 20 * MINUTO, contado: 0 })

confere('mapa EasyStok: dia fechado bloqueia novo lançamento, estorno e novo fechamento', () => {
  const semMudanca1 = reducer(fechado, {
    tipo: acao.LANCAR_MOVIMENTO_CAIXA, agora: AGORA + 21 * MINUTO, tipoMovimento: 'entrada', categoria: 'x', valor: 1, meio: 'dinheiro',
  })
  assert.equal(semMudanca1, fechado)
  const alvo = fechado.caixa.movimentos.find((m) => m.categoria === 'Reforço de troco')
  const semMudanca2 = reducer(fechado, {
    tipo: acao.ESTORNAR_MOVIMENTO_CAIXA, agora: AGORA + 21 * MINUTO, movimentoId: alvo.id, motivo: 'tarde demais',
  })
  assert.equal(semMudanca2, fechado)
  const semMudanca3 = reducer(fechado, { tipo: acao.FECHAR_CAIXA, agora: AGORA + 22 * MINUTO, contado: 0 })
  assert.equal(semMudanca3, fechado)
})

console.log(`\n${passou} verificações passaram.`)
