/* eslint-disable no-console */
// Prova da issue #1443: no modo API, abrir a loja é abrir o caixa num gesto só (mostra o caixa de
// ontem, confere ou retifica o saldo inicial, abre o caixa e só então a loja) e fechar a loja dentro
// do horário pede justificativa, que vai para a API. A aba Caixa da Gestão fala com /api/caixa.
//
//   node ferramentas/prova-1443-loja-abre-caixa.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    return proximo(url, contexto)
  },
})

const acao = await import('../src/aplicacao/acoes.js')
const { NAO_LIGADAS } = await import('../src/aplicacao/api/naoLigadas.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const { FUNCIONAMENTO_PADRAO } = await import('../src/dominio/funcionamento.js')
const {
  situacaoDoCaixaParaAbrirLoja, retificacaoDoSaldo, observacaoDaAbertura, observacaoDoFechamento,
  fecharLojaPedeJustificativa, justificativaValida,
} = await import('../src/dominio/aberturaDaLoja.js')
const { caixaDiaDaApi, fechamentoDaApi } = await import('../src/infra/api/caixaApi.js')
const { criarAcoesExpedienteApi } = await import('../src/aplicacao/api/expediente.js')
const { criarAcoesCaixaApi } = await import('../src/aplicacao/api/caixa.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

// Segunda, 05/10/2026: 12:00 em Brasília (dentro do 08–22 h) e 23:30 (fora).
const NO_HORARIO = Date.parse('2026-10-05T15:00:00Z')
const FORA_DO_HORARIO = Date.parse('2026-10-06T02:30:00Z')

const diaApi = (campos = {}) => ({
  data: '2026-10-05', saldoInicial: 0, totalVendas: 0, totalPagamentosPedidos: 0, totalEntradasExtras: 0,
  totalSaidasExtras: 0, saldoEsperado: 0, aberto: false, fechado: false, fechamento: null, movimentos: [],
  aberturaPendenteCrossDay: false, abertoDesde: null, linhasExtras: [], ...campos,
})
const fechamentoApi = (campos = {}) => ({
  id: 'f1', data: '2026-10-04', saldoInicial: 100, totalVendas: 80, totalPagamentosPedidos: 80, totalEntradasExtras: 0,
  totalSaidasExtras: 30, saldoFinal: 150, fechadoPorNome: 'Thatiane', observacoes: null, fechadoEm: '2026-10-04T23:10:00Z', ...campos,
})

// Fetch falso: responde por rota e guarda a ordem das chamadas.
const chamadas = []
let respostas = {}
globalThis.fetch = async (url, opcoes = {}) => {
  const metodo = opcoes.method ?? 'GET'
  const chave = `${metodo} ${String(url).split('?')[0]}`
  chamadas.push({ chave, url: String(url), corpo: opcoes.body ? JSON.parse(opcoes.body) : undefined })
  const resposta = respostas[chave]
  if (resposta?.status) {
    return new Response(JSON.stringify({ error: { code: 'X', message: resposta.mensagem } }), { status: resposta.status })
  }
  return new Response(JSON.stringify({ data: resposta ?? null }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
const limpar = () => { chamadas.length = 0; respostas = {} }

// 1. Domínio: o que a tela mostra antes de abrir.
await confere('caixa de ontem esquecido aberto: precisa fechar antes de abrir a loja', () => {
  const situacao = situacaoDoCaixaParaAbrirLoja(
    caixaDiaDaApi(diaApi({ aberto: true, aberturaPendenteCrossDay: true, abertoDesde: '2026-10-04', saldoEsperado: 210 })),
    fechamentoDaApi(fechamentoApi({ data: '2026-10-03' })),
  )
  assert.equal(situacao.tipo, 'esquecido')
  assert.equal(situacao.desde, '2026-10-04')
  assert.equal(situacao.saldoEsperado, 210)
})

await confere('caixa novo: sugere o saldo final do último fechamento', () => {
  const situacao = situacaoDoCaixaParaAbrirLoja(caixaDiaDaApi(diaApi()), fechamentoDaApi(fechamentoApi()))
  assert.equal(situacao.tipo, 'novo')
  assert.equal(situacao.saldoSugerido, 150)
  assert.equal(situacao.ultimo.data, '2026-10-04')
})

await confere('caixa de hoje já aberto: a loja abre direto', () => {
  const situacao = situacaoDoCaixaParaAbrirLoja(caixaDiaDaApi(diaApi({ aberto: true, saldoInicial: 150, saldoEsperado: 180 })), null)
  assert.equal(situacao.tipo, 'aberto')
  assert.equal(situacao.saldoEsperado, 180)
})

await confere('caixa de hoje já fechado: não reabre', () => {
  const situacao = situacaoDoCaixaParaAbrirLoja(caixaDiaDaApi(diaApi({ fechado: true, fechamento: fechamentoApi({ data: '2026-10-05' }) })), null)
  assert.equal(situacao.tipo, 'fechadoHoje')
})

await confere('saldo diferente do último fechamento pede motivo da retificação', () => {
  const situacao = situacaoDoCaixaParaAbrirLoja(caixaDiaDaApi(diaApi()), fechamentoDaApi(fechamentoApi()))
  assert.deepEqual(retificacaoDoSaldo(150, situacao), { diferenca: 0, exigeMotivo: false })
  const r = retificacaoDoSaldo(140, situacao)
  assert.equal(r.diferenca, -10)
  assert.equal(r.exigeMotivo, true)
  const texto = observacaoDaAbertura({ saldoInformado: 140, situacao, motivo: 'Troco levado ao banco' })
  assert.match(texto, /04\/10/)
  assert.match(texto, /Troco levado ao banco/)
})

await confere('fechamento do caixa leva o contado e a diferença nas observações', () => {
  assert.match(observacaoDoFechamento({ contado: 195, saldoEsperado: 200 }), /Contado na gaveta: R\$\s?195,00.*Diferença: -R\$\s?5,00/)
})

await confere('fechar a loja pede justificativa só dentro do horário', () => {
  assert.equal(fecharLojaPedeJustificativa(NO_HORARIO, FUNCIONAMENTO_PADRAO), true)
  assert.equal(fecharLojaPedeJustificativa(FORA_DO_HORARIO, FUNCIONAMENTO_PADRAO), false)
  assert.equal(justificativaValida('porque'), false)
  assert.equal(justificativaValida('  Falta de luz no bairro '), true)
})

// 2. Gesto: alternarLoja não abre a loja solta; pede o gesto.
const estadoBase = (campos = {}) => ({ funcionamento: FUNCIONAMENTO_PADRAO, lojaAberta: null, ...campos })
function montarExpediente(estado) {
  const despachos = []
  const acoes = criarAcoesExpedienteApi({ despachar: (a) => despachos.push(a), estadoRef: { current: estado } })
  return { acoes, despachos }
}

await confere('loja fechada: tocar "abrir" pede o gesto abrir caixa e loja, sem chamar a API', async () => {
  limpar()
  const { acoes, despachos } = montarExpediente(estadoBase())
  acoes.alternarLoja(FORA_DO_HORARIO)
  await new Promise((r) => setTimeout(r, 0))
  assert.deepEqual(despachos.map((d) => d.tipo), [acao.PEDIR_GESTO_LOJA])
  assert.equal(despachos[0].gesto, 'abrir')
  assert.equal(chamadas.length, 0)
})

await confere('loja aberta no horário: tocar "fechar" pede o gesto com justificativa', async () => {
  limpar()
  const { acoes, despachos } = montarExpediente(estadoBase())
  acoes.alternarLoja(NO_HORARIO)
  await new Promise((r) => setTimeout(r, 0))
  assert.deepEqual(despachos.map((d) => [d.tipo, d.gesto]), [[acao.PEDIR_GESTO_LOJA, 'fechar']])
  assert.equal(chamadas.length, 0)
})

await confere('loja aberta na mão fora do horário: fecha direto, sem justificativa', async () => {
  limpar()
  respostas['POST /api/atendimento/expediente/controle'] = { controleManual: 'ForcarFechada', horarios: [] }
  const { acoes } = montarExpediente(estadoBase({ lojaAberta: true }))
  acoes.alternarLoja(FORA_DO_HORARIO)
  await new Promise((r) => setTimeout(r, 0))
  assert.deepEqual(chamadas.map((c) => c.chave), ['POST /api/atendimento/expediente/controle'])
  assert.equal(chamadas[0].corpo.controle, 'ForcarFechada')
  assert.equal(chamadas[0].corpo.justificativa, undefined)
})

await confere('abrir loja com caixa: abre o caixa e SÓ ENTÃO a loja', async () => {
  limpar()
  respostas['POST /api/caixa/abrir'] = { id: 'm1', tipo: 'abertura', valor: 140 }
  respostas['POST /api/atendimento/expediente/controle'] = { controleManual: 'ForcarAberta', horarios: [] }
  const { acoes, despachos } = montarExpediente(estadoBase())
  await acoes.abrirLojaComCaixa({ saldoInicial: 140, observacoes: 'Retificado: troco', caixaJaAberto: false })
  assert.deepEqual(chamadas.map((c) => c.chave), ['POST /api/caixa/abrir', 'POST /api/atendimento/expediente/controle'])
  assert.deepEqual(chamadas[0].corpo, { saldoInicial: 140, observacoes: 'Retificado: troco' })
  assert.equal(chamadas[1].corpo.controle, 'ForcarAberta')
  assert.ok(despachos.some((d) => d.tipo === acao.SINCRONIZAR_EXPEDIENTE))
  assert.ok(despachos.some((d) => d.tipo === acao.FECHAR_GESTO_LOJA))
})

await confere('caixa recusado: a loja não abre', async () => {
  limpar()
  respostas['POST /api/caixa/abrir'] = { status: 400, mensagem: 'Caixa do dia já foi fechado. Não é possível reabrir.' }
  const { acoes } = montarExpediente(estadoBase())
  await assert.rejects(acoes.abrirLojaComCaixa({ saldoInicial: 0, caixaJaAberto: false }), /já foi fechado/)
  assert.deepEqual(chamadas.map((c) => c.chave), ['POST /api/caixa/abrir'])
})

await confere('caixa já aberto hoje: só abre a loja', async () => {
  limpar()
  respostas['POST /api/atendimento/expediente/controle'] = { controleManual: 'ForcarAberta', horarios: [] }
  const { acoes } = montarExpediente(estadoBase())
  await acoes.abrirLojaComCaixa({ caixaJaAberto: true })
  assert.deepEqual(chamadas.map((c) => c.chave), ['POST /api/atendimento/expediente/controle'])
})

await confere('fechar no horário manda a justificativa à API', async () => {
  limpar()
  respostas['POST /api/atendimento/expediente/controle'] = { controleManual: 'ForcarFechada', horarios: [] }
  const { acoes } = montarExpediente(estadoBase())
  await acoes.fecharLojaComJustificativa('  Falta de luz no bairro ')
  assert.deepEqual(chamadas[0].corpo, { controle: 'ForcarFechada', justificativa: 'Falta de luz no bairro' })
})

await confere('fechar no horário recusado (403) devolve o motivo da API', async () => {
  limpar()
  respostas['POST /api/atendimento/expediente/controle'] = { status: 403, mensagem: 'Só gerente ou dona abre e fecha a loja na mão.' }
  const { acoes } = montarExpediente(estadoBase())
  await assert.rejects(acoes.fecharLojaComJustificativa('Falta de luz no bairro'), /gerente/)
})

await confere('reducer guarda e limpa o gesto pedido', () => {
  let estado = estadoInicial({ conversas: [], catalogo: { cardapio: [], janelas: [], canais: [] }, regras: [] })
  estado = reducer(estado, { tipo: acao.PEDIR_GESTO_LOJA, gesto: 'abrir' })
  assert.equal(estado.ui.gestoLoja, 'abrir')
  estado = reducer(estado, { tipo: acao.FECHAR_GESTO_LOJA })
  assert.equal(estado.ui.gestoLoja, null)
})

// 3. Aba Caixa ligada.
await confere('abrir, lançar, estornar e fechar o caixa saem da lista "não ligadas"', () => {
  for (const nome of ['abrirCaixa', 'lancarMovimentoCaixa', 'estornarMovimentoCaixa', 'fecharCaixa']) {
    assert.equal(nome in NAO_LIGADAS, false, `${nome} ainda está em NAO_LIGADAS`)
  }
})

await confere('ações do caixa falam com /api/caixa', async () => {
  limpar()
  const caixa = criarAcoesCaixaApi({ despachar: () => {} })
  await caixa.abrirCaixa(NO_HORARIO, 50, 'ok')
  await caixa.lancarMovimentoCaixa(NO_HORARIO, { tipoMovimento: 'saida', categoria: 'Sangria', valor: 20, meio: 'dinheiro', descricao: 'Banco' })
  await caixa.estornarMovimentoCaixa(NO_HORARIO, 'm9', 'Lançado errado')
  await caixa.fecharCaixa(NO_HORARIO, 30, 30)
  assert.deepEqual(chamadas.map((c) => c.chave), [
    'POST /api/caixa/abrir', 'POST /api/caixa/movimentos', 'POST /api/caixa/movimentos/m9/estornar', 'POST /api/caixa/fechar',
  ])
  assert.deepEqual(chamadas[1].corpo, { tipo: 'saida', valor: 20, categoria: 'Sangria', metodo: 'dinheiro', descricao: 'Banco' })
  assert.equal(chamadas[2].corpo.motivo, 'Lançado errado')
  assert.match(chamadas[3].corpo.observacoes, /Contado na gaveta/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
