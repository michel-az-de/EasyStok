/* eslint-disable no-console */
// Prova da #3301: a Produção do dia lê estoque, pedidos agendados, venda sem estoque e validade e diz o
// que produzir primeiro. Regras puras (dominio/leituraDaProducao.js e o "Pôr tudo no lote" da grade).
//
//   node --import ./ferramentas/ambienteDoVite.mjs ferramentas/prova-3301-producao-leitura.mjs

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

const { cartoesDaGrade, faltaProduzir, porTudoNoLote, mensagemDoPorTudo, porcoesNoLote, resumoDoLote, TETO_PORCOES_POR_LOTE } = await import('../src/dominio/gradeDeProducao.js')
const { leituraDaGrade, agruparPelaLeitura, resumoDaLeitura } = await import('../src/dominio/leituraDaProducao.js')

let passou = 0
const falhas = []
const confere = (nome, fn) => {
  try { fn(); passou++; console.log('ok   ', nome) } catch (e) { falhas.push(nome); console.log('FALHA', nome, '\n     ', e.message) }
}

// O cardápio, o estoque e a sugestão no formato que a tela já recebe (dominio e aplicacao, sem rede).
const prato = (sku, nome, extra = {}) => ({ sku, nome, porcao: '500 g', categoria: 'Massas', porcoes: [], ...extra })
const CARDAPIO = [
  prato('i-1', 'Talharim'),
  prato('i-2', 'Lasanha', { porcao: '', porcoes: [{ id: 'v-6', rotulo: '600 g', peso: '600 g', padrao: true }, { id: 'v-f', rotulo: 'Família', peso: '' }] }),
  prato('i-3', 'Nhoque'),
  prato('i-4', 'Ravioli'),
  prato('i-5', 'Capeletti'),
  prato('i-7', 'Canelone'),
  prato('i-9', 'Molho branco', { categoria: 'Molhos' }),
  prato('i-10', 'Molho bolonhesa', { categoria: 'Molhos' }),
  prato('i-12', 'Quiche', { categoria: 'Pratos' }),
]
const lote = (dias, extra = {}) => ({ quantidade: 2, diasParaVencer: dias, vencendo: dias >= 0 && dias <= 1, vencido: dias < 0, porcao: null, ...extra })
const estoquePrato = (sku, saldo, extra = {}) => ({ sku, saldo, descoberto: 0, lotes: [], porcoes: [], ...extra })
const ESTOQUE = [
  estoquePrato('i-1', 4),
  estoquePrato('i-2', 3, { porcoes: [{ variacaoId: 'v-6', saldo: 0, descoberto: 2 }, { variacaoId: 'v-f', saldo: 3, descoberto: 0 }], lotes: [lote(1, { porcao: 'Família' })] }),
  estoquePrato('i-3', 0, { descoberto: 3 }),
  estoquePrato('i-4', 8),
  estoquePrato('i-5', 2, { lotes: [lote(1)] }),
  estoquePrato('i-9', 0),
  estoquePrato('i-10', 5, { lotes: [lote(-1)] }),
  estoquePrato('i-12', 1),
]
const sug = (sku, minimo, saldo, agendados, descoberto, sugestao) => ({ sku, minimo, saldo, agendados, descoberto, sugestao })
const SUGESTAO = { ate: '2026-11-27', pratos: [sug('i-1', 6, 4, 4, 0, 6), sug('i-2', 0, 3, 3, 2, 3), sug('i-3', 0, 0, 2, 3, 5), sug('i-9', 4, 0, 0, 0, 4), sug('i-12', 3, 1, 0, 0, 2), sug('i-4', 0, 8, 0, 0, 0)] }

const cartoes = cartoesDaGrade(CARDAPIO, ESTOQUE)
const doPrato = (nome, tamanho) => cartoes.find((c) => c.nome === nome && (tamanho === undefined || c.tamanho === tamanho))
const ler = (sugestao = SUGESTAO, estoque = ESTOQUE) => leituraDaGrade(cartoes, { estoque, sugestao })

confere('sem saldo com venda sem estoque e pedido: urgente, e o motivo diz por quê', () => {
  const l = ler().get(doPrato('Nhoque').chave)
  assert.equal(l.nivel, 'urgente')
  assert.equal(l.falta, 5)
  assert.equal(l.motivo, 'Acabou · 3 vendidas sem estoque · 2 em pedidos até 27/11')
  assert.equal(l.tom, 'perigo')
})

confere('sem saldo e abaixo do mínimo, sem venda: urgente pela sugestão', () => {
  const l = ler().get(doPrato('Molho branco').chave)
  assert.equal(l.nivel, 'urgente')
  assert.equal(l.motivo, 'Acabou · abaixo do mínimo (4)')
})

confere('com saldo mas a sugestão pede: repor, com pedidos e mínimo', () => {
  const talharim = ler().get(doPrato('Talharim').chave)
  assert.deepEqual([talharim.nivel, talharim.falta, talharim.tom], ['repor', 6, 'atencao'])
  assert.equal(talharim.motivo, '4 em pedidos até 27/11 · abaixo do mínimo (6)')
  assert.equal(ler().get(doPrato('Quiche').chave).motivo, 'Abaixo do mínimo (3)')
})

confere('lote perto de vencer ou vencido não pede produção: fica em dia, com o aviso', () => {
  const vence = ler().get(doPrato('Capeletti').chave)
  assert.deepEqual([vence.nivel, vence.motivo, vence.tom], ['vence', 'Lote vence amanhã', 'atencao'])
  assert.equal(ler().get(doPrato('Molho bolonhesa').chave).motivo, 'Lote vencido')
  const hoje = leituraDaGrade(cartoes, { estoque: [estoquePrato('i-5', 2, { lotes: [lote(0)] })], sugestao: null }).get(doPrato('Capeletti').chave)
  assert.equal(hoje.motivo, 'Lote vence hoje')
  const emDias = leituraDaGrade(cartoes, { estoque: [estoquePrato('i-5', 2, { lotes: [lote(3, { vencendo: true })] })], sugestao: null }).get(doPrato('Capeletti').chave)
  assert.equal(emDias.motivo, 'Lote vence em 3 dias')
})

confere('prato em dia não leva motivo; prato sem estoque ligado diz isso e não vira "sem saldo"', () => {
  const ok = ler().get(doPrato('Ravioli').chave)
  assert.deepEqual([ok.nivel, ok.motivo, ok.falta], ['ok', null, 0])
  const semEstoque = ler().get(doPrato('Canelone').chave)
  assert.deepEqual([semEstoque.nivel, semEstoque.motivo], ['semEstoque', null])
  const comPedido = ler({ ate: '2026-11-27', pratos: [sug('i-7', 0, 0, 3, 0, 3)] }).get(doPrato('Canelone').chave)
  assert.deepEqual([comPedido.nivel, comPedido.motivo], ['repor', '3 em pedidos até 27/11'])
})

confere('prato com porções: a venda sem estoque e o lote são da porção; a sugestão do prato não é de nenhuma', () => {
  const l = ler()
  const p600 = l.get(doPrato('Lasanha', '600 g').chave)
  const familia = l.get(doPrato('Lasanha', 'Família').chave)
  assert.deepEqual([p600.nivel, p600.falta, p600.motivo], ['urgente', 0, 'Acabou · 2 vendidas sem estoque'], 'o lote da Família não é do 600 g')
  assert.deepEqual([familia.nivel, familia.motivo], ['vence', 'Lote vence amanhã'])
  assert.equal(ler(null).get(doPrato('Lasanha', '600 g').chave).motivo, 'Acabou · 2 vendidas sem estoque')
})

confere('sem sugestão (sem permissão ou rede fora), a leitura funciona só com o estoque', () => {
  const l = ler(null)
  assert.equal(l.get(doPrato('Nhoque').chave).nivel, 'urgente')
  assert.equal(l.get(doPrato('Nhoque').chave).motivo, 'Acabou · 3 vendidas sem estoque')
  assert.equal(l.get(doPrato('Talharim').chave).nivel, 'ok')
  assert.equal(l.get(doPrato('Molho branco').chave).nivel, 'ok', 'sem a sugestão não há como saber que o mínimo falta')
})

confere('a ordem: urgente primeiro, por quanto falta; depois o que repõe; o resto na ordem do cardápio', () => {
  const { pedem, emDia } = agruparPelaLeitura(cartoes, ler())
  assert.deepEqual(pedem.map((c) => nomeComTamanho(c)), ['Nhoque 500 g', 'Molho branco 500 g', 'Lasanha 600 g', 'Talharim 500 g', 'Quiche 500 g'])
  assert.deepEqual(emDia.map((c) => nomeComTamanho(c)), ['Lasanha Família', 'Ravioli 500 g', 'Capeletti 500 g', 'Canelone 500 g', 'Molho bolonhesa 500 g'])
  const nenhum = agruparPelaLeitura(cartoes, ler(null, []))
  assert.deepEqual([nenhum.pedem.length, nenhum.emDia.length], [0, cartoes.length], 'sem nada que peça, a grade fica como era')
})

confere('o resumo do topo diz quantos pratos pedem e quanto falta; em dia só quando a sugestão veio', () => {
  const r = resumoDaLeitura(ler(), { sugestaoCarregada: true })
  assert.deepEqual([r.texto, r.tom], ['5 pratos pedem produção · faltam 17 porções', 'perigo'])
  const soRepor = resumoDaLeitura(leituraDaGrade(cartoes, { estoque: [estoquePrato('i-1', 4)], sugestao: { ate: '2026-11-27', pratos: [sug('i-1', 6, 4, 4, 0, 6)] } }), { sugestaoCarregada: true })
  assert.deepEqual([soRepor.texto, soRepor.tom], ['1 prato pede produção · faltam 6 porções', 'atencao'])
  const soVence = resumoDaLeitura(leituraDaGrade(cartoes, { estoque: [estoquePrato('i-5', 2, { lotes: [lote(1)] }), estoquePrato('i-10', 5, { lotes: [lote(-1)] })], sugestao: { ate: '2026-11-27', pratos: [] } }), { sugestaoCarregada: true })
  assert.deepEqual([soVence.texto, soVence.tom], ['Nada pede produção agora. 2 pratos têm lote vencido ou perto de vencer.', 'atencao'])
  const tudoEmDia = resumoDaLeitura(leituraDaGrade(cartoes, { estoque: [estoquePrato('i-4', 8)], sugestao: { ate: '2026-11-27', pratos: [] } }), { sugestaoCarregada: true })
  assert.deepEqual([tudoEmDia.texto, tudoEmDia.tom], ['Tudo em dia: nada pede produção agora.', 'ok'])
  assert.equal(resumoDaLeitura(leituraDaGrade(cartoes, { estoque: [estoquePrato('i-4', 8)], sugestao: null }), { sugestaoCarregada: false }), null, 'sem a sugestão não há como garantir "em dia"')
  assert.equal(resumoDaLeitura(new Map(), { sugestaoCarregada: true }), null, 'sem pratos, sem resumo')
})

// --- "Pôr tudo no lote" ---
const novaLinha = () => ({ id: `l-${Math.random()}`, sku: '', variacaoId: '', porcoes: '', pesoPorPorcaoG: '', pesoRealG: '', validadeDias: '5' })
const apoio = { pratos: CARDAPIO, novaLinha }
const falta = faltaProduzir(SUGESTAO.pratos, cartoes)

confere('põe no lote o que falta dos pratos de um tamanho e diz de quem é preciso escolher o tamanho', () => {
  const r = porTudoNoLote([], falta, apoio)
  assert.deepEqual(r.postos.map((i) => [i.nome, i.quantidade]), [['Talharim', 6], ['Nhoque', 5], ['Molho branco', 4], ['Quiche', 2]])
  assert.deepEqual(r.semTamanho.map((i) => i.nome), ['Lasanha'])
  assert.deepEqual(r.cortados, [])
  assert.deepEqual(resumoDoLote(r.linhas), { pratos: 4, porcoes: 17 })
  assert.deepEqual(r.linhas.map((l) => l.sku), ['i-1', 'i-3', 'i-9', 'i-12'], 'o lote segue a ordem do cardápio')
})

confere('nunca rebaixa o que ela já pôs no lote e não repete o que já cobre a falta', () => {
  const talharim = doPrato('Talharim')
  const nhoque = doPrato('Nhoque')
  const inicial = porTudoNoLote([], [falta.find((i) => i.nome === 'Talharim')], apoio).linhas.map((l) => (l.sku === 'i-1' ? { ...l, porcoes: '9' } : l))
  const r = porTudoNoLote(inicial, falta, apoio)
  assert.equal(porcoesNoLote(r.linhas, talharim), 9, 'ela pôs 9, a falta é 6: fica 9')
  assert.equal(porcoesNoLote(r.linhas, nhoque), 5)
  assert.deepEqual(r.postos.map((i) => i.nome), ['Nhoque', 'Molho branco', 'Quiche'], 'Talharim já cobria a falta')
  const repetido = porTudoNoLote(r.linhas, falta, apoio)
  assert.deepEqual(repetido.postos, [])
  assert.deepEqual(repetido.linhas, r.linhas)
})

confere('respeita o teto de 9.999 porções do lote e conta o que ficou com menos', () => {
  const grande = [{ sku: 'i-1', nome: 'Talharim', quantidade: TETO_PORCOES_POR_LOTE - 2, cartao: doPrato('Talharim') }, { sku: 'i-3', nome: 'Nhoque', quantidade: 10, cartao: doPrato('Nhoque') }]
  const r = porTudoNoLote([], grande, apoio)
  assert.equal(resumoDoLote(r.linhas).porcoes, TETO_PORCOES_POR_LOTE)
  assert.equal(porcoesNoLote(r.linhas, doPrato('Nhoque')), 2)
  assert.deepEqual(r.cortados.map((i) => i.nome), ['Nhoque'])
  const cheio = porTudoNoLote(r.linhas, [{ sku: 'i-4', nome: 'Ravioli', quantidade: 3, cartao: doPrato('Ravioli') }], apoio)
  assert.deepEqual([cheio.postos.length, cheio.cortados.map((i) => i.nome)], [0, ['Ravioli']])
  assert.equal(resumoDoLote(cheio.linhas).porcoes, TETO_PORCOES_POR_LOTE, 'lote cheio não passa do teto')
})

confere('a mensagem do botão fala a língua da loja', () => {
  assert.equal(mensagemDoPorTudo({ postos: [{}, {}, {}, {}], semTamanho: [{ nome: 'Lasanha' }], cortados: [] }), '4 pratos no lote. Lasanha tem tamanhos: escolha na grade.')
  assert.equal(mensagemDoPorTudo({ postos: [], semTamanho: [{ nome: 'Lasanha' }, { nome: 'Pizza' }], cortados: [] }), 'Lasanha e Pizza têm tamanhos: escolha na grade.')
  assert.equal(mensagemDoPorTudo({ postos: [{}], semTamanho: [], cortados: [] }), '1 prato no lote.')
  assert.equal(mensagemDoPorTudo({ postos: [], semTamanho: [], cortados: [] }), 'O lote já cobre o que falta.')
  assert.equal(mensagemDoPorTudo({ postos: [{}], semTamanho: [], cortados: [{ nome: 'Nhoque' }] }), '1 prato no lote. O lote vai até 9.999 porções: Nhoque ficou com menos.')
})

function nomeComTamanho(c) { return `${c.nome} ${c.tamanho}`.trim() }

console.log(`\n${passou} ok, ${falhas.length} falhas`)
if (falhas.length > 0) process.exit(1)
