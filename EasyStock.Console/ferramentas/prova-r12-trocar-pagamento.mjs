/* eslint-disable no-console */
// Prova da issue #13 (rodada 12, feedback da Thatiane no vídeo): depois que a
// cobrança sai, não dava para trocar a forma de pagamento, e "Marcar pago"
// não tinha volta. O caso dela: escolheu vale no lugar da cliente, tentou
// refazer em Pix e ficou presa em "Pago fora da cobrança" até o pedido vencer.
//
// Exercita as regras de domínio (`dominio/cobranca.js`) e o reducer de
// verdade, na mesma sequência de despachos que o Provider faz.
//
//   node ferramentas/prova-r12-trocar-pagamento.mjs

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

const cob = await import('../src/dominio/cobranca.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const { pedidosNaCozinha } = await import('../src/dominio/cozinha.js')
const catalogo = await import('../src/infra/catalogo.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const AGORA = Date.parse('2026-09-26T15:00:00-03:00')
const MINUTO = 60000
const EMISSAO = {
  pix: { identificador: 'tx-pix', copiaECola: 'copia-pix', link: 'https://pix.exemplo/1', meio: 'pix' },
  'cartao-link': { identificador: 'tx-cartao', copiaECola: null, link: 'https://mp.exemplo/1', meio: 'cartao-link' },
  maquininha: { identificador: null, copiaECola: null, link: null, meio: 'maquininha' },
  'vale-refeicao': { identificador: null, copiaECola: null, link: null, meio: 'vale-refeicao' },
}

// --- Domínio -----------------------------------------------------------------
confere('Pix e cartão em destaque; maquininha e vale em segundo plano', () => {
  const destaque = catalogo.MEIOS_DE_PAGAMENTO.filter((m) => m.destaque).map((m) => m.id)
  assert.deepEqual(destaque, ['pix', 'cartao-link'])
})

const pedidoCom = (cobranca, estado = 'aguardando') => ({
  numero: '2026-0902', estado, itens: [{ sku: 'LAS-CLA', qtd: 1, obs: '' }], cobranca,
})
const pixAberto = cob.criarCobranca(pedidoCom(null), catalogo.CARDAPIO, AGORA, EMISSAO.pix)
const valeCombinado = cob.criarCobranca(pedidoCom(null), catalogo.CARDAPIO, AGORA, EMISSAO['vale-refeicao'])
const pixPago = cob.aplicarPagamento(pixAberto, AGORA + MINUTO, null)

confere('pode alterar a forma: Pix em aberto, Pix vencido e vale combinado', () => {
  assert.equal(cob.podeAlterarMeio(pedidoCom(pixAberto)), true)
  assert.equal(cob.podeAlterarMeio(pedidoCom({ ...pixAberto, expiraEm: AGORA - 1 })), true)
  assert.equal(cob.podeAlterarMeio(pedidoCom(valeCombinado, 'pago')), true)
})

confere('não altera: sem cobrança, paga, cancelada, pedido cancelado', () => {
  assert.equal(cob.podeAlterarMeio(pedidoCom(null)), false)
  assert.equal(cob.podeAlterarMeio(pedidoCom(pixPago, 'pago')), false)
  assert.equal(cob.podeAlterarMeio(pedidoCom(cob.cancelarCobranca(pixAberto, AGORA))), false)
  assert.equal(cob.podeAlterarMeio(pedidoCom(pixAberto, 'cancelado')), false)
})

confere('desfazer pagamento exige motivo e guarda o que foi desfeito', () => {
  assert.equal(cob.desfazerPagamento(pixPago, AGORA, ''), pixPago)
  const desfeita = cob.desfazerPagamento(pixPago, AGORA + 2 * MINUTO, 'Marquei por engano')
  assert.equal(desfeita.pagaEm, null)
  assert.equal(desfeita.valorPago, null)
  assert.equal(desfeita.liberadaEm, null)
  assert.deepEqual(desfeita.pagamentoDesfeito, {
    em: AGORA + 2 * MINUTO, motivo: 'Marquei por engano', pagaEm: pixPago.pagaEm, valorPago: null,
  })
})

confere('desfazer não mexe em cobrança sem pagamento nem em estornada', () => {
  assert.equal(cob.desfazerPagamento(pixAberto, AGORA, 'x'), pixAberto)
  const estornada = cob.estornarCobranca(pixPago, AGORA, { motivo: 'devolvi' })
  assert.equal(cob.desfazerPagamento(estornada, AGORA, 'x'), estornada)
})

confere('pode desfazer: Pix pago antes do preparo; vale recebido em qualquer passo', () => {
  assert.equal(cob.podeDesfazerPagamento(pedidoCom(pixPago, 'pago')), true)
  assert.equal(cob.podeDesfazerPagamento(pedidoCom(pixPago, 'preparo')), false)
  const valeRecebido = cob.aplicarPagamento(valeCombinado, AGORA, null)
  assert.equal(cob.podeDesfazerPagamento(pedidoCom(valeRecebido, 'entrega')), true)
  assert.equal(cob.podeDesfazerPagamento(pedidoCom(pixAberto)), false)
})

confere('texto ao cliente conta que a forma mudou e que a anterior não vale', () => {
  const nova = { ...cob.criarCobranca(pedidoCom(null), catalogo.CARDAPIO, AGORA, EMISSAO.pix), meioAnterior: 'vale-refeicao' }
  const texto = cob.textoDaCobranca(nova, pedidoCom(null), catalogo.CARDAPIO)
  assert.match(texto, /Mudei a forma de pagamento do pedido 2026-0902 para Pix/)
  assert.match(texto, /vale não vale mais/)
  assert.ok(!/[–—]/.test(texto))
})

// --- Reducer -----------------------------------------------------------------
const conversaInicial = {
  id: 'c1', cadastroId: 'cad-1', nome: 'Ana Teste', canal: 'WhatsApp', estado: 'Em atendimento',
  conta: 'cliente', responsavel: 'Thatiane', mensagens: [], bloqueio: null,
  cliente: { notas: [], tags: [] },
  pedido: {
    numero: '2026-0902', estado: 'aguardando', janela: 'j3', entregador: null, meio: null,
    itens: [{ sku: 'LAS-CLA', qtd: 1, obs: '' }], agradecimentoEnviado: false, pagamentos: [], cobranca: null,
  },
}
const inicio = estadoInicial({
  conversas: [conversaInicial],
  catalogo: {
    canais: catalogo.CANAIS, cardapio: catalogo.CARDAPIO, janelas: catalogo.JANELAS_ENTREGA,
    linhas: catalogo.LINHAS_PRODUTO,
  },
  regras: [], modoAgente: 'sugerir',
})
const pedidoDe = (estado) => estado.conversas[0].pedido
let seq = 0
// Mesma sequência do Provider: emite, despacha, e sem link libera a esteira.
const gerar = (estado, meio) => {
  let depois = reducer(estado, {
    tipo: acao.GERAR_PEDIDO, id: 'c1', agora: AGORA, mensagemId: 'g' + (seq += 1), cobrancaId: 'g' + (seq += 1),
    emissao: EMISSAO[meio],
  })
  if (!EMISSAO[meio].link) depois = reducer(depois, { tipo: acao.DESPACHAR_MESMO_ASSIM, id: 'c1', agora: AGORA })
  return depois
}
const alterar = (estado, meio, agora = AGORA + MINUTO) => {
  let depois = reducer(estado, {
    tipo: acao.ALTERAR_MEIO_PAGAMENTO, id: 'c1', agora, mensagemId: 'a' + (seq += 1), sistemaId: 'a' + (seq += 1),
    emissao: EMISSAO[meio],
  })
  if (!EMISSAO[meio].link) depois = reducer(depois, { tipo: acao.DESPACHAR_MESMO_ASSIM, id: 'c1', agora })
  return depois
}

const comVale = gerar(inicio, 'vale-refeicao')
confere('o caso dela: vale enviado põe o pedido em "pago" sem pagamento (esteira anda)', () => {
  assert.equal(pedidoDe(comVale).estado, 'pago')
  assert.equal(pedidoDe(comVale).cobranca.meio, 'vale-refeicao')
})

const valeParaPix = alterar(comVale, 'pix')
confere('vale vira Pix na hora: cobrança nova com link, pedido volta a esperar pagamento', () => {
  const p = pedidoDe(valeParaPix)
  assert.equal(p.cobranca.meio, 'pix')
  assert.ok(p.cobranca.link)
  assert.equal(p.cobranca.meioAnterior, 'vale-refeicao')
  assert.equal(p.estado, 'aguardando')
  assert.equal(p.meio, 'pix')
  assert.equal(pedidosNaCozinha(valeParaPix.conversas).length, 0)
})

confere('sem perder o pedido: mesmo número, mesmos itens', () => {
  assert.equal(pedidoDe(valeParaPix).numero, '2026-0902')
  assert.deepEqual(pedidoDe(valeParaPix).itens, conversaInicial.pedido.itens)
})

confere('cobrança anterior fica no histórico, cancelada, com o motivo', () => {
  const [anterior] = pedidoDe(valeParaPix).cobrancasAnteriores
  assert.equal(anterior.meio, 'vale-refeicao')
  assert.equal(anterior.canceladaEm, AGORA + MINUTO)
  assert.match(anterior.motivoCancelamento, /forma de pagamento/i)
})

confere('trilha: nota de sistema na conversa e cobrança nova ao cliente', () => {
  const msgs = valeParaPix.conversas[0].mensagens
  const sistema = msgs.filter((m) => m.dir === 'sistema').at(-1)
  assert.match(sistema.texto, /Forma de pagamento alterada de vale para Pix/)
  const cliente = msgs.filter((m) => m.dir === 'out').at(-1)
  assert.match(cliente.texto, /https:\/\/pix\.exemplo\/1/)
})

confere('Pix pendente vira vale: cobra na entrega e o pedido entra na cozinha', () => {
  const comPix = gerar(inicio, 'pix')
  const depois = alterar(comPix, 'vale-refeicao')
  assert.equal(pedidoDe(depois).cobranca.meio, 'vale-refeicao')
  assert.equal(pedidoDe(depois).estado, 'pago')
  assert.equal(pedidosNaCozinha(depois.conversas).length, 1)
})

confere('Pix pendente vira cartão por link', () => {
  const depois = alterar(gerar(inicio, 'pix'), 'cartao-link')
  assert.equal(pedidoDe(depois).cobranca.meio, 'cartao-link')
  assert.equal(pedidoDe(depois).estado, 'aguardando')
})

confere('mesma forma não faz nada', () => {
  const comPix = gerar(inicio, 'pix')
  assert.equal(alterar(comPix, 'pix'), comPix)
})

const pago = reducer(gerar(inicio, 'pix'), {
  tipo: acao.CONFIRMAR_PAGAMENTO, id: 'c1', agora: AGORA + 2 * MINUTO, mensagemId: 'p1', valorPago: null,
})
confere('cobrança paga não troca de forma', () => {
  assert.equal(pedidoDe(pago).estado, 'pago')
  assert.equal(alterar(pago, 'vale-refeicao'), pago)
})

const desfazer = (estado, motivo) => reducer(estado, {
  tipo: acao.DESFAZER_PAGAMENTO, id: 'c1', motivo, agora: AGORA + 3 * MINUTO,
  mensagemId: 'd' + (seq += 1), notaId: 'n' + (seq += 1),
})

confere('desfazer pagamento sem motivo não faz nada', () => {
  assert.equal(desfazer(pago, '  '), pago)
})

const desfeito = desfazer(pago, 'Marquei pago por engano')
confere('desfazer pagamento: Pix volta a aguardar e sai da cozinha', () => {
  const p = pedidoDe(desfeito)
  assert.equal(p.cobranca.pagaEm, null)
  assert.equal(p.estado, 'aguardando')
  assert.equal(pedidosNaCozinha(desfeito.conversas).length, 0)
})

confere('desfazer pagamento deixa registro na conversa e nota no cadastro', () => {
  const c = desfeito.conversas[0]
  const sistema = c.mensagens.filter((m) => m.dir === 'sistema').at(-1)
  assert.match(sistema.texto, /Pagamento desfeito/)
  assert.match(sistema.texto, /Marquei pago por engano/)
  assert.match(c.cliente.notas[0].texto, /Pagamento desfeito no pedido 2026-0902/)
})

confere('depois de desfeito, dá para alterar a forma de novo', () => {
  const depois = alterar(desfeito, 'cartao-link', AGORA + 4 * MINUTO)
  assert.equal(pedidoDe(depois).cobranca.meio, 'cartao-link')
})

confere('vale recebido por engano: desfaz a baixa e o pedido segue na esteira', () => {
  const recebido = reducer(comVale, { tipo: acao.MARCAR_RECEBIDO_ENTREGA, id: 'c1', agora: AGORA, recebido: true })
  assert.ok(pedidoDe(recebido).cobranca.pagaEm)
  const depois = desfazer(recebido, 'Ainda não recebi')
  assert.equal(pedidoDe(depois).cobranca.pagaEm, null)
  assert.equal(pedidoDe(depois).estado, 'pago')
})

console.log(`\n${passou} verificações passaram.`)
