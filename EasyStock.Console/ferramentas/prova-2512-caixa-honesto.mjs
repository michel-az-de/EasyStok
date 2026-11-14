// Prova da #2512 (pacote "Caixa honesto" da auditoria QA de 13/11, `docs/dev/revisoes/2026-11-13-qa-console-web.md`).
//
// Q08: "Pagamentos de pedidos" ficava na fileira da gaveta e o mesmo Pix aparecia de novo em "Fora da gaveta".
// O resumo agora tem duas contas que fecham no mesmo total: onde o dinheiro está e de onde ele veio.
// Q03: o aviso de devolução do Caixa diz o recorte (hoje ou desde a abertura) e leva ao Extrato (90 dias).
// Q04: lançamento de outro dia, no caixa aberto há dias, mostra a data junto da hora.
// Q06, Q07, N3, N4: nome de quem fechou, um botão de fechar, grade sem contar linhas, observação com quebras.
//
// Roda com: node ferramentas/prova-2512-caixa-honesto.mjs

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

const ler = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const caixa = await import('../src/dominio/caixa.js')

// Dia da auditoria: troco de R$ 100, R$ 50 de entrada em dinheiro e um pedido pago por Pix (R$ 19).
const dia = {
  data: '2026-11-13',
  saldoInicial: 100,
  totalVendas: 0,
  totalEntradasExtras: 50,
  totalSaidasExtras: 0,
  totalPagamentosPedidos: 19,
  saldoEsperado: 169,
  movimentos: [{ tipo: 'entrada', valor: 50, meio: 'dinheiro' }],
  linhasExtras: [{ valor: 19, meio: 'pix' }],
}
const soma = (linhas) => Math.round(linhas.reduce((s, l) => s + (l.saida ? -l.valor : l.valor), 0) * 100) / 100

caso('Q08: resumoEmDuasContas existe', () => assert.equal(typeof caixa.resumoEmDuasContas, 'function'))
caso('Q08: o pagamento de pedido fica só em "de onde veio", não na conta da gaveta', () => {
  const { lugar, origem } = caixa.resumoEmDuasContas(dia, caixa.gavetaDoDia(dia))
  assert.ok(origem.some((l) => l.rotulo === 'Pagamentos de pedidos'))
  assert.ok(!lugar.some((l) => l.rotulo === 'Pagamentos de pedidos'))
  assert.ok(!lugar.some((l) => l.rotulo === 'Saldo inicial'))
})
caso('Q08: as duas contas fecham no total do caixa', () => {
  const { lugar, origem } = caixa.resumoEmDuasContas(dia, caixa.gavetaDoDia(dia))
  assert.equal(soma(lugar), dia.saldoEsperado)
  assert.equal(soma(origem), dia.saldoEsperado)
})
caso('Q08: a gaveta é a primeira linha de "onde está" e o Pix vai para fora da gaveta', () => {
  const { lugar } = caixa.resumoEmDuasContas(dia, caixa.gavetaDoDia(dia))
  assert.deepEqual(lugar.map((l) => [l.rotulo, l.valor]), [['Na gaveta (dinheiro)', 150], [caixa.ROTULO_FORA_DA_GAVETA, 19]])
})
caso('Q08: saída entra na conta como saída', () => {
  const comSaida = { ...dia, totalSaidasExtras: 20, saldoEsperado: 149, movimentos: [...dia.movimentos, { tipo: 'saida', valor: 20, meio: 'dinheiro' }] }
  const { lugar, origem } = caixa.resumoEmDuasContas(comSaida, caixa.gavetaDoDia(comSaida))
  assert.equal(soma(origem), 149)
  assert.equal(soma(lugar), 149)
})
caso('Q08: sem abertura não fala de gaveta nem de saldo inicial', () => {
  const semTroco = { ...dia, saldoInicial: 0, saldoEsperado: 69 }
  const { lugar, origem } = caixa.resumoEmDuasContas(semTroco, caixa.gavetaDoDia(semTroco), true)
  assert.ok(!origem.some((l) => l.rotulo === 'Saldo inicial'))
  assert.ok(!lugar.some((l) => /gaveta/i.test(l.rotulo)))
  assert.equal(soma(lugar), 69)
})

caso('Q03: o aviso do Caixa diz o recorte de hoje', () => {
  const texto = caixa.avisoDeDevolucoesNasTelas({ total: 19, pedidos: 1 }, { desde: null })
  assert.match(texto, /R\$\s19,00 a devolver de 1 pedido cancelado com pagamento recebido hoje/)
  assert.match(texto, /Extrato/)
})
caso('Q03: no caixa aberto há dias, o recorte é desde a abertura', () => {
  const texto = caixa.avisoDeDevolucoesNasTelas({ total: 19, pedidos: 1 }, { desde: '2026-10-28' })
  assert.match(texto, /com pagamento recebido desde 28\/10/)
})

caso('Q04: quandoNoCaixa existe', () => assert.equal(typeof caixa.quandoNoCaixa, 'function'))
caso('Q04: lançamento do dia do caixa mostra só a hora', () => {
  assert.equal(caixa.quandoNoCaixa('2026-11-13T21:39:00-03:00', '2026-11-13'), '21:39')
})
caso('Q04: lançamento de outro dia mostra a data junto da hora', () => {
  assert.equal(caixa.quandoNoCaixa('2026-11-05T21:39:00-03:00', '2026-11-13'), '05/11 21:39')
})
caso('Q04: a meia-noite conta no fuso da loja, não em UTC', () => {
  assert.equal(caixa.quandoNoCaixa('2026-11-14T01:30:00Z', '2026-11-13'), '22:30')
})
caso('Q04: data inválida não lança', () => assert.equal(caixa.quandoNoCaixa('nao-e-data', '2026-11-13'), ''))

const tela = ler('features/gestao/caixa/AbaCaixaApi.jsx')
const css = ler('features/gestao/caixa/abaCaixa.module.css')

caso('Q04: as duas listas da tela usam quandoNoCaixa', () => {
  assert.equal((tela.match(/quandoNoCaixa\(m\.em, dia\.data\)/g) ?? []).length, 2)
  assert.match(tela, /quandoNoCaixa\(f\.fechadoEm, f\.data\)/)
})
caso('Q03: o aviso do Caixa leva ao Extrato', () => {
  assert.match(tela, /href="#\/m\/financeiro\/extrato"/)
})
caso('Q07: o caixa esquecido aberto tem um botão de fechar só', () => {
  assert.equal((tela.match(/Fechar o caixa de/g) ?? []).length, 1)
})
caso('Q06: fechamento sem nome diz que não há registro', () => {
  assert.match(tela, /Sem registro de quem fechou/)
})
caso('N3: a grade do resumo não depende do número de linhas', () => {
  assert.doesNotMatch(tela, /linhas\.length === 6/)
  assert.doesNotMatch(css, /resumoEmQuatro/)
})
caso('N4: a observação do fechamento mantém as quebras de linha', () => {
  assert.match(css, /\.observacao\s*\{[^}]*white-space:\s*pre-line/)
  assert.equal((tela.match(/css\.observacao\}/g) ?? []).length, 2)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
