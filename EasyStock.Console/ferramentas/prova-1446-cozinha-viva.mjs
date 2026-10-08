// Prova da issue #1446: a Cozinha do modo API alcança a do protótipo. Cobre as
// partes puras de `dominio/kds.js`:
//   - o cartão do KDS vira o MESMO canhoto de 80 mm do protótipo
//     (`documentoDoCanhoto`), agrupado por linha, com molho e observação, sem
//     preço e com a observação do pedido;
//   - a transição entre duas leituras da fila (quem entrou, quem mudou de
//     coluna, quem saiu), que é o que a tela anima;
//   - a regra do soltar no arrasto (só o próximo passo aceita);
//   - o filtro por linha, a urgência e a barra de tempo do cartão e a fila de
//     impressão da cozinha.
//
// Roda com: node ferramentas/prova-1446-cozinha-viva.mjs

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

const kds = await import('../src/dominio/kds.js')
const { documentoDoCanhoto } = await import('../src/dominio/impressao.js')
const {
  canhotoDoKds, filtrarPorLinha, transicaoDaFila, respostaAoSoltarKds, urgenciaDoPedido,
  ETAPAS_KDS, gruposDoKds, impressoesDaCozinha, tempoAteInicio, avisoDeInicio,
} = kds

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const LINHAS = {
  servir: { rotulo: 'Para servir' },
  casa: { rotulo: 'Preparar em casa' },
}

const pedido = (extra = {}) => ({
  id: 'p1',
  numeroCurto: 'A1B2C3D4',
  clienteNome: 'Ana Souza',
  clienteApt: 'Apto 12',
  janela: { label: '11h-12h', data: '2026-10-07', inicio: '11:00:00', fim: '12:00:00' },
  status: 'aguardando',
  statusRotulo: 'Aguardando',
  linhas: ['paraServir', 'prepararEmCasa'],
  itens: [
    { nome: 'Nhoque', variacao: '500 g', qtd: 2, observacao: 'sem cebola', linha: 'paraServir', molho: 'Sugo' },
    { nome: 'Lasanha', variacao: null, qtd: 1, observacao: null, linha: 'prepararEmCasa', molho: null },
    { nome: 'Pão', variacao: null, qtd: 1, observacao: null, linha: null, molho: null },
  ],
  inicioPrevistoEm: '2026-10-07T13:30:00Z',
  atrasado: false,
  pagoEm: '2026-10-07T13:00:00Z',
  criadoEm: '2026-10-07T12:55:00Z',
  observacoes: 'Interfone quebrado, ligar ao chegar',
  endereco: 'Rua das Flores, 10',
  ...extra,
})

const textos = (documento) => documento.paginas[0].desenhos.filter((d) => d.tipo === 'texto').map((d) => d.texto)

// --- Canhoto ------------------------------------------------------------------
confere('canhoto: cartão do KDS vira o canhoto de 80 mm do protótipo', () => {
  const doc = documentoDoCanhoto(canhotoDoKds(pedido(), LINHAS))
  const t = textos(doc)
  assert.equal(doc.nomeArquivo, 'canhoto-A1B2C3D4.pdf')
  assert.ok(t.includes('CANHOTO DE PEDIDO, NÃO É CUPOM FISCAL'))
  assert.ok(t.includes('Nº A1B2C3D4'))
  assert.ok(t.some((x) => x.startsWith('Ana Souza · Apto 12') && x.includes('11h-12h')), t.join('|'))
  assert.ok(t.includes('Rua das Flores, 10'))
})

confere('canhoto: itens agrupados por linha, servir antes de casa, porção e molho com observação', () => {
  const t = textos(documentoDoCanhoto(canhotoDoKds(pedido(), LINHAS)))
  const iServir = t.indexOf('PARA SERVIR')
  const iCasa = t.indexOf('PREPARAR EM CASA')
  assert.ok(iServir >= 0 && iCasa > iServir, t.join('|'))
  assert.ok(t.indexOf('Nhoque') > iServir && t.indexOf('Nhoque') < iCasa)
  assert.ok(t.includes('500 g'))
  assert.ok(t.includes('Molho Sugo · sem cebola'), t.join('|'))
  assert.ok(t.indexOf('Lasanha') > iCasa)
})

confere('canhoto: item sem linha não some do papel (cai em Para servir)', () => {
  const t = textos(documentoDoCanhoto(canhotoDoKds(pedido(), LINHAS)))
  const iCasa = t.indexOf('PREPARAR EM CASA')
  const iPao = t.indexOf('Pão')
  assert.ok(iPao >= 0 && iPao < iCasa, t.join('|'))
})

confere('canhoto: observação do pedido sai no papel; sem preço', () => {
  const t = textos(documentoDoCanhoto(canhotoDoKds(pedido(), LINHAS)))
  assert.ok(t.some((x) => x.includes('Interfone quebrado')), t.join('|'))
  assert.ok(!t.some((x) => /R\$/.test(x)))
})

confere('canhoto: sem janela, sem endereço e sem observação não quebra', () => {
  const doc = documentoDoCanhoto(canhotoDoKds(pedido({ janela: null, endereco: null, observacoes: null, clienteApt: null }), LINHAS))
  const t = textos(doc)
  assert.ok(t.some((x) => x.startsWith('Ana Souza · Janela não escolhida')), t.join('|'))
})

confere('comanda em tela: mesmos grupos do papel', () => {
  const grupos = gruposDoKds(pedido(), LINHAS)
  assert.deepEqual(grupos.map((g) => g.chave), ['servir', 'casa'])
  assert.deepEqual(grupos[0].itens.map((l) => l.produto.nome), ['Nhoque', 'Pão'])
  assert.equal(grupos[0].itens[0].obs, 'Molho Sugo · sem cebola')
})

// --- Filtro por linha -----------------------------------------------------------
confere('filtro: por linha do console, aceita o token da API', () => {
  const soServir = pedido({ id: 'a', itens: [{ nome: 'X', qtd: 1, linha: 'paraServir' }] })
  const soCasa = pedido({ id: 'b', itens: [{ nome: 'Y', qtd: 1, linha: 'preparar_em_casa' }] })
  assert.deepEqual(filtrarPorLinha([soServir, soCasa], null).map((p) => p.id), ['a', 'b'])
  assert.deepEqual(filtrarPorLinha([soServir, soCasa], 'servir').map((p) => p.id), ['a'])
  assert.deepEqual(filtrarPorLinha([soServir, soCasa], 'casa').map((p) => p.id), ['b'])
})

// --- Transição da fila (o que a tela anima) -----------------------------------------
confere('transição: primeira leitura não anima nada', () => {
  const t = transicaoDaFila(null, [pedido()])
  assert.deepEqual(t, { novos: [], mudaram: [], sairam: [] })
})

confere('transição: entrou, mudou de coluna e saiu', () => {
  const antes = [pedido({ id: 'a' }), pedido({ id: 'b' }), pedido({ id: 'c', status: 'saiu_para_entrega' })]
  const depois = [pedido({ id: 'a' }), pedido({ id: 'b', status: 'preparando' }), pedido({ id: 'd' })]
  const t = transicaoDaFila(antes, depois)
  assert.deepEqual(t.novos, ['d'])
  assert.deepEqual(t.mudaram, ['b'])
  assert.deepEqual(t.sairam.map((p) => p.id), ['c'])
})

// --- Arrastar e soltar -----------------------------------------------------------
confere('soltar: só a coluna do próximo passo aceita', () => {
  assert.deepEqual(respostaAoSoltarKds('aguardando', 'preparando'), { aceita: true, motivo: null })
  assert.deepEqual(respostaAoSoltarKds('aguardando', 'aguardando'), { aceita: false, motivo: null })
  const pulo = respostaAoSoltarKds('aguardando', 'pronto')
  assert.equal(pulo.aceita, false)
  assert.match(pulo.motivo, /Um passo por vez.*Em preparo/)
  const volta = respostaAoSoltarKds('pronto', 'preparando')
  assert.equal(volta.aceita, false)
  assert.match(volta.motivo, /Saiu para entrega/)
  assert.equal(respostaAoSoltarKds('entregue', 'aguardando').aceita, false)
})

// --- Urgência ------------------------------------------------------------------
confere('urgência: atrasado, agora, logo ou nada; só enquanto aguarda', () => {
  const agora = Date.parse('2026-10-07T13:20:00Z')
  assert.equal(urgenciaDoPedido(pedido({ atrasado: true }), agora), 'atrasado')
  assert.equal(urgenciaDoPedido(pedido({ inicioPrevistoEm: '2026-10-07T13:20:00Z' }), agora), 'agora')
  assert.equal(urgenciaDoPedido(pedido({ inicioPrevistoEm: '2026-10-07T13:30:00Z' }), agora), 'logo')
  assert.equal(urgenciaDoPedido(pedido({ inicioPrevistoEm: '2026-10-07T14:30:00Z' }), agora), null)
  assert.equal(urgenciaDoPedido(pedido({ inicioPrevistoEm: null }), agora), null)
  assert.equal(urgenciaDoPedido(pedido({ status: 'preparando', inicioPrevistoEm: '2026-10-07T13:00:00Z' }), agora), null)
})

// --- Etapas e fila de impressão --------------------------------------------------------
confere('etapas: a trilha da comanda vai de Aguardando a Entregue', () => {
  assert.deepEqual(ETAPAS_KDS.map((e) => e.status), ['aguardando', 'preparando', 'pronto', 'saiu_para_entrega', 'entregue'])
})

confere('fila de impressão da cozinha: só pedido que está na fila do KDS, mais antigo primeiro', () => {
  const fila = [
    { id: 'i2', pedidoId: 'p2', status: 'pendente', criadaEm: '2026-10-07T13:05:00Z' },
    { id: 'i9', pedidoId: 'fora', status: 'pendente', criadaEm: '2026-10-07T12:00:00Z' },
    { id: 'i1', pedidoId: 'p1', status: 'pendente', criadaEm: '2026-10-07T13:01:00Z' },
    { id: 'i3', pedidoId: 'p3', status: 'falhou', criadaEm: '2026-10-07T13:00:00Z' },
  ]
  const naCozinha = [pedido({ id: 'p1' }), pedido({ id: 'p2' }), pedido({ id: 'p3' })]
  assert.deepEqual(impressoesDaCozinha(fila, naCozinha).map((i) => i.id), ['i1', 'i2'])
  assert.deepEqual(impressoesDaCozinha(fila, null), [])
})

confere('tempo até o início: fração que falta entre o pagamento e o início previsto', () => {
  const p = pedido({ pagoEm: '2026-10-07T13:00:00Z', inicioPrevistoEm: '2026-10-07T13:40:00Z' })
  assert.equal(tempoAteInicio(p, Date.parse('2026-10-07T13:00:00Z')), 1)
  assert.equal(tempoAteInicio(p, Date.parse('2026-10-07T13:30:00Z')), 0.25)
  assert.equal(tempoAteInicio(p, Date.parse('2026-10-07T14:00:00Z')), 0)
  assert.equal(tempoAteInicio(pedido({ inicioPrevistoEm: null }), Date.parse('2026-10-07T13:00:00Z')), null)
  assert.equal(tempoAteInicio(pedido({ status: 'preparando' }), Date.parse('2026-10-07T13:00:00Z')), null)
})

confere('aviso de início: "Começar em" só enquanto aguarda; atraso de outro dia vale em qualquer coluna', () => {
  const agora = Date.parse('2026-10-07T13:00:00Z')
  assert.match(avisoDeInicio(pedido({ status: 'aguardando' }), agora).texto, /Começar em 30 min/)
  assert.equal(avisoDeInicio(pedido({ status: 'preparando' }), agora), null)
  assert.equal(avisoDeInicio(pedido({ status: 'pronto' }), agora), null)
  assert.equal(avisoDeInicio(pedido({ status: 'preparando', inicioPrevistoEm: null, atrasado: true }), agora).atrasado, true)
})

console.log(`prova #1446 (cozinha viva no modo API): ${passou} conferências ok`)
