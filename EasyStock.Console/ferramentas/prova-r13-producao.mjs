// Prova da issue #43 (rodada 13, UC-08, UC-09, RN-44 a RN-51): registrar o
// dia de produção em lote, saldo do cardápio a partir dos lotes, venda que
// baixa o lote mais antigo (RN-50), venda acima do saldo que não trava e vira
// descoberto com alerta em texto (RN-48/RN-49), e ajuste de contagem com
// motivo que fecha o alerta (UC-09).
//
// Mesmo gancho de `ferramentas/prova-bloqueio-preparo.mjs`: registerHooks
// resolve import sem extensão para `.js`, sem JSX nem CSS na árvore.
//
// Roda com: node ferramentas/prova-r13-producao.mjs

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

const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const {
  ajustarContagemLotes, baixarLotesFifo, criarLoteProducao, saldoEmPorcoes, situacaoDeVencimento,
  textoDescoberto,
} = await import('../src/dominio/producao.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

// --- Domínio -----------------------------------------------------------------

confere('domínio: lote nasce com porções, saldo e sobra (RN-44, RN-45, RN-46)', () => {
  const lote = criarLoteProducao({
    id: 'lote-1',
    sku: 'LAS-CLA',
    identificador: 'Lote 27/09',
    pesoRealG: 1072,
    porcoes: [{ destino: 'comer-agora', quantidade: 2, pesoPorcaoG: 500 }],
    validadeDias: 3,
    produzidoEmIso: '2026-09-27T09:00:00-03:00',
  })
  assert.equal(lote.pesoRealG, 1072)
  assert.equal(lote.sobraG, 72)
  assert.equal(lote.porcoes[0].saldo, 2)
  assert.equal(saldoEmPorcoes([lote], 'LAS-CLA'), 2)
  assert.equal(lote.validadeEm, new Date('2026-09-30T09:00:00-03:00').toISOString())
})

confere('domínio: baixa usa o lote mais antigo com saldo (RN-50)', () => {
  const antigo = criarLoteProducao({
    id: 'l-antigo',
    sku: 'LAS-CLA',
    pesoRealG: 500,
    porcoes: [{ destino: 'comer-agora', quantidade: 1, pesoPorcaoG: 500 }],
    validadeDias: 3,
    produzidoEmIso: '2026-09-25T09:00:00-03:00',
  })
  const novo = criarLoteProducao({
    id: 'l-novo',
    sku: 'LAS-CLA',
    pesoRealG: 500,
    porcoes: [{ destino: 'comer-agora', quantidade: 1, pesoPorcaoG: 500 }],
    validadeDias: 3,
    produzidoEmIso: '2026-09-27T09:00:00-03:00',
  })
  const { lotes, descoberto } = baixarLotesFifo([novo, antigo], 'LAS-CLA', 1)
  assert.equal(descoberto, 0)
  const alteradoAntigo = lotes.find((l) => l.id === 'l-antigo')
  const alteradoNovo = lotes.find((l) => l.id === 'l-novo')
  assert.equal(alteradoAntigo.porcoes[0].saldo, 0, 'o lote mais antigo desce primeiro')
  assert.equal(alteradoNovo.porcoes[0].saldo, 1, 'o lote novo continua intacto')
})

confere('domínio: venda acima do saldo não trava, vira descoberto (RN-48)', () => {
  const lote = criarLoteProducao({
    id: 'l1',
    sku: 'LAS-CLA',
    pesoRealG: 500,
    porcoes: [{ destino: 'comer-agora', quantidade: 1, pesoPorcaoG: 500 }],
    validadeDias: 3,
    produzidoEmIso: '2026-09-27T09:00:00-03:00',
  })
  const { descoberto } = baixarLotesFifo([lote], 'LAS-CLA', 3)
  assert.equal(descoberto, 2)
})

confere('domínio: alerta descreve o desacerto em texto (RN-49)', () => {
  assert.equal(
    textoDescoberto('Lasanha clássica', '800 g', 2),
    'Vendeu 2 Lasanha clássica 800 g sem produção lançada.',
  )
  assert.ok(!/[–—]/.test(textoDescoberto('Item', 'porção', 1)), 'sem travessão')
})

confere('domínio: lote perto da validade fica destacado (RN-51)', () => {
  const lote = criarLoteProducao({
    id: 'l1',
    sku: 'LAS-CLA',
    pesoRealG: 500,
    porcoes: [{ destino: 'comer-agora', quantidade: 1, pesoPorcaoG: 500 }],
    validadeDias: 1,
    produzidoEmIso: '2026-09-26T09:00:00-03:00',
  })
  const agoraQuaseVencendo = Date.parse('2026-09-27T08:00:00-03:00')
  const agoraVencido = Date.parse('2026-09-27T10:00:00-03:00')
  assert.equal(situacaoDeVencimento(lote, agoraQuaseVencendo), 'perto')
  assert.equal(situacaoDeVencimento(lote, agoraVencido), 'vencido')
})

confere('domínio: ajuste manual encosta no lote mais antigo (UC-09)', () => {
  const lote = criarLoteProducao({
    id: 'l1',
    sku: 'LAS-CLA',
    pesoRealG: 500,
    porcoes: [{ destino: 'comer-agora', quantidade: 1, pesoPorcaoG: 500 }],
    validadeDias: 3,
    produzidoEmIso: '2026-09-27T09:00:00-03:00',
  })
  const { lotes, aplicadoEmLote } = ajustarContagemLotes([lote], 'LAS-CLA', 3)
  assert.equal(aplicadoEmLote, true)
  assert.equal(saldoEmPorcoes(lotes, 'LAS-CLA'), 3)
})

// --- Reducer -------------------------------------------------------------

const AGORA = Date.parse('2026-09-27T15:00:00-03:00')
const CARDAPIO = [
  { sku: 'LAS-CLA', nome: 'Lasanha clássica', linha: 'servir', porcao: '800 g', preco: 85, estoque: 4 },
  { sku: 'RAV-LIM', nome: 'Ravióli de limão siciliano', linha: 'casa', porcao: '500 g', preco: 62, estoque: 6 },
]
const conversa = () => ({
  id: 'c1', mensagens: [], pedido: null,
})
const inicio = () => estadoInicial({
  conversas: [conversa()], catalogo: { cardapio: CARDAPIO }, regras: [], modoAgente: 'sugerir',
})

confere('reducer: estado nasce com producao vazia', () => {
  const estado = inicio()
  assert.deepEqual(estado.producao, { lotes: [], descobertos: {}, ajustes: [] })
})

confere('reducer: registrar produção cria lote e o saldo do cardápio vem dele (aceite 1 e 2)', () => {
  let estado = inicio()
  estado = reducer(estado, {
    tipo: acao.PRODUCAO_REGISTRAR_LOTE,
    loteId: 'lote-1',
    sku: 'LAS-CLA',
    identificador: 'Lote 27/09',
    pesoRealG: 1072,
    porcoes: [{ destino: 'comer-agora', quantidade: 2, pesoPorcaoG: 500 }],
    validadeDias: 3,
    insumo: false,
    agora: AGORA,
  })
  assert.equal(estado.producao.lotes.length, 1)
  const item = estado.catalogo.cardapio.find((i) => i.sku === 'LAS-CLA')
  assert.equal(item.estoque, 2, 'saldo do cardápio passa a vir do lote (2 porções), não do estoque antigo (4)')
})

confere('reducer: insumo intermediário não abastece o cardápio (UC-08 alt A)', () => {
  let estado = inicio()
  estado = reducer(estado, {
    tipo: acao.PRODUCAO_REGISTRAR_LOTE,
    loteId: 'lote-molho',
    sku: 'LAS-CLA',
    identificador: 'Molho de tomate',
    pesoRealG: 800,
    porcoes: [{ destino: 'comer-agora', quantidade: 1, pesoPorcaoG: 800 }],
    validadeDias: 5,
    insumo: true,
    agora: AGORA,
  })
  const item = estado.catalogo.cardapio.find((i) => i.sku === 'LAS-CLA')
  assert.equal(item.estoque, 4, 'insumo não mexe no saldo vendável')
  assert.equal(estado.producao.lotes[0].insumo, true)
})

confere('reducer: venda baixa o lote mais antigo (RN-50), sku sem lote não muda em nada', () => {
  let estado = inicio()
  estado = reducer(estado, {
    tipo: acao.PRODUCAO_REGISTRAR_LOTE,
    loteId: 'lote-1',
    sku: 'LAS-CLA',
    identificador: 'Lote 27/09',
    pesoRealG: 1000,
    porcoes: [{ destino: 'comer-agora', quantidade: 2, pesoPorcaoG: 500 }],
    validadeDias: 3,
    insumo: false,
    agora: AGORA,
  })
  // Venda de 1 porção com produção lançada: lote e cardápio descem juntos.
  estado = reducer(estado, {
    tipo: acao.ADICIONAR_ITEM, id: 'c1', item: { sku: 'LAS-CLA', nome: 'Lasanha clássica', estoque: 2 },
    numeroPedido: '2026-0001', alertaId: 'alerta-1', agora: AGORA,
  })
  assert.equal(estado.catalogo.cardapio.find((i) => i.sku === 'LAS-CLA').estoque, 1)
  assert.equal(saldoEmPorcoes(estado.producao.lotes, 'LAS-CLA'), 1)

  // Sku sem produção lançada (RAV-LIM): continua no caminho antigo, `producao`
  // intocado. É a prova de que a integração não quebra o Saldo zero existente.
  const producaoAntesDaVenda = estado.producao
  estado = reducer(estado, {
    tipo: acao.ADICIONAR_ITEM, id: 'c1', item: { sku: 'RAV-LIM', nome: 'Ravióli de limão siciliano', estoque: 6 },
    numeroPedido: '2026-0001', alertaId: 'alerta-2', agora: AGORA,
  })
  assert.equal(estado.catalogo.cardapio.find((i) => i.sku === 'RAV-LIM').estoque, 5)
  assert.equal(estado.producao, producaoAntesDaVenda, 'producao não muda para sku sem lote')
})

confere('reducer: venda acima do saldo não bloqueia e abre alerta persistente (RN-48/RN-49, UC-09)', () => {
  let estado = inicio()
  estado = reducer(estado, {
    tipo: acao.PRODUCAO_REGISTRAR_LOTE,
    loteId: 'lote-1',
    sku: 'LAS-CLA',
    identificador: 'Lote 27/09',
    pesoRealG: 500,
    porcoes: [{ destino: 'comer-agora', quantidade: 1, pesoPorcaoG: 500 }],
    validadeDias: 3,
    insumo: false,
    agora: AGORA,
  })
  const item = { sku: 'LAS-CLA', nome: 'Lasanha clássica', porcao: '800 g', estoque: 1 }
  estado = reducer(estado, {
    tipo: acao.ADICIONAR_ITEM, id: 'c1', item, numeroPedido: '2026-0001', alertaId: 'alerta-1', agora: AGORA,
  })
  // Segunda venda: saldo já é zero, a comanda aceita mesmo assim (D7).
  estado = reducer(estado, {
    tipo: acao.ADICIONAR_ITEM, id: 'c1', item: { ...item, estoque: 0 },
    numeroPedido: '2026-0001', alertaId: 'alerta-2', agora: AGORA,
  })
  assert.equal(estado.conversas[0].pedido.itens.find((l) => l.sku === 'LAS-CLA').qtd, 2, 'venda foi aceita')
  assert.equal(estado.producao.descobertos['LAS-CLA'].quantidade, 1)
})

confere('reducer: ajuste de contagem com motivo corrige o saldo e fecha o alerta (UC-09, aceite 4)', () => {
  let estado = inicio()
  estado = reducer(estado, {
    tipo: acao.PRODUCAO_REGISTRAR_LOTE,
    loteId: 'lote-1',
    sku: 'LAS-CLA',
    identificador: 'Lote 27/09',
    pesoRealG: 500,
    porcoes: [{ destino: 'comer-agora', quantidade: 1, pesoPorcaoG: 500 }],
    validadeDias: 3,
    insumo: false,
    agora: AGORA,
  })
  const item = { sku: 'LAS-CLA', nome: 'Lasanha clássica', porcao: '800 g', estoque: 1 }
  estado = reducer(estado, {
    tipo: acao.ADICIONAR_ITEM, id: 'c1', item, numeroPedido: '2026-0001', alertaId: 'a1', agora: AGORA,
  })
  estado = reducer(estado, {
    tipo: acao.ADICIONAR_ITEM, id: 'c1', item: { ...item, estoque: 0 },
    numeroPedido: '2026-0001', alertaId: 'a2', agora: AGORA,
  })
  assert.ok(estado.producao.descobertos['LAS-CLA'], 'alerta aberto antes do ajuste')

  // Motivo curto demais: não aplica (defesa equivalente ao estorno do EasyStok).
  const semEfeito = reducer(estado, {
    tipo: acao.PRODUCAO_AJUSTAR_CONTAGEM, ajusteId: 'aj-0', sku: 'LAS-CLA', loteId: null,
    novoSaldoTotal: 3, motivo: 'ok', agora: AGORA,
  })
  assert.equal(semEfeito, estado, 'motivo com menos de 3 letras não muda o estado')

  estado = reducer(estado, {
    tipo: acao.PRODUCAO_AJUSTAR_CONTAGEM, ajusteId: 'aj-1', sku: 'LAS-CLA', loteId: null,
    novoSaldoTotal: 3, motivo: 'contei no congelador', agora: AGORA,
  })
  assert.equal(estado.catalogo.cardapio.find((i) => i.sku === 'LAS-CLA').estoque, 3)
  assert.equal(estado.producao.descobertos['LAS-CLA'], undefined, 'ajuste fecha o alerta')
  assert.equal(estado.producao.ajustes.length, 1)
  assert.equal(estado.producao.ajustes[0].motivo, 'contei no congelador')
})

console.log('\n' + passou + ' verificações passaram.')
