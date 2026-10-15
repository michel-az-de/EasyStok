/* eslint-disable no-console */
// Prova da issue #1691 (I9.2): corrida pedida por fora (99, Lalamove, Uber) anotada na viagem, com custo.
//   1. nenhuma tela mostra cotação inventada da 99: o provedor simulado saiu do modo demonstração;
//   2. o formulário da corrida vira o corpo da API (`CorridaExternaBody`) em centavos e avisa antes de enviar;
//   3. a viagem mostra cada corrida e o custo somado;
//   4. a tela fala com GET/POST/PUT `api/atendimento/viagens/{id}/corridas`.
//
//   node ferramentas/prova-1691-corrida-manual.mjs

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

const chamadas = []
globalThis.fetch = async (url, opcoes = {}) => {
  chamadas.push({ url, metodo: opcoes.method ?? 'GET', corpo: opcoes.body ? JSON.parse(opcoes.body) : undefined })
  return new Response(JSON.stringify({ data: [] }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const integracoes = await import('../src/dominio/integracoes.js')
const corrida = await import('../src/dominio/corrida.js')
const provedores = await import('../src/infra/provedoresDeEntrega.js')
const dominio = await import('../src/dominio/entregasApi.js')
const infra = await import('../src/infra/api/entregasApi.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

// --- 1. O 99 simulado saiu ----------------------------------------------------------------
await confere('a 99 não está mais entre os provedores do modo demonstração', () => {
  assert.deepEqual(integracoes.PROVEDORES_LOGISTICA.map((p) => p.chave), ['proprio', 'lalamove'])
  assert.equal(integracoes.provedorPorChave('99entrega'), null)
  assert.deepEqual(Object.keys(integracoes.configuracaoPadraoIntegracoes().provedores), ['proprio', 'lalamove'])
  assert.equal(corrida.ehProvedorIntegrado('99entrega'), false)
  assert.equal(corrida.ehProvedorIntegrado('lalamove'), true)
})
await confere('configuração antiga com a 99 ligada não a oferece no despacho', () => {
  const antiga = {
    padrao: '99entrega',
    provedores: { proprio: { ativo: true, credencial: null }, '99entrega': { ativo: true, credencial: 'chave' } },
  }
  assert.deepEqual(integracoes.provedoresParaDespacho(antiga).map((p) => p.chave), ['proprio'])
})
await confere('nenhum código de corrida nem entregador inventado para a 99', async () => {
  const { codigo } = await provedores.chamar({ provedor: '99entrega', pedido: { numero: 'P-1' } })
  assert.doesNotMatch(codigo, /^99E/)
  const r = await provedores.consultarStatus({ provedor: '99entrega', status: 'procurando', aleatorio: () => 1 })
  assert.equal(r.entregador, null)
})
await confere('o aviso de simulação não cita mais a 99', () => {
  assert.doesNotMatch(integracoes.AVISO_SIMULADO, /99/)
  assert.doesNotMatch(integracoes.AVISO_SIMULADO, /\u2014/)
})

// --- 2. Formulário da corrida ---------------------------------------------------------------
await confere('a corrida é de terceiro: a lista traz 99, Lalamove, Uber, iFood e outra, sem "Própria"', () => {
  assert.deepEqual(dominio.EMPRESAS_DE_CORRIDA.map((e) => e.valor), ['NoveNove', 'Lalamove', 'Uber', 'Ifood', 'Outra'])
  assert.equal(dominio.rotuloEmpresaEntregador('Uber'), 'Uber')
})
await confere('formulário vira o corpo da API em centavos', () => {
  assert.deepEqual(dominio.corpoCorrida({ empresa: 'NoveNove', custo: '18,90', motorista: ' João, Honda CG ' }),
    { provedor: 'NoveNove', custoCentavos: 1890, motorista: 'João, Honda CG' })
  assert.deepEqual(dominio.corpoCorrida({ empresa: 'Uber', custo: 'R$ 7', motorista: '' }),
    { provedor: 'Uber', custoCentavos: 700, motorista: null })
  assert.equal(dominio.corpoCorrida({ empresa: 'Uber', custo: '19,99', motorista: '' }).custoCentavos, 1999)
})
await confere('custo vazio, negativo ou acima de R$ 5.000 é avisado antes de chamar a API', () => {
  const com = (custo) => dominio.erroDoFormularioCorrida({ empresa: 'NoveNove', custo, motorista: '' })
  assert.equal(com('18,90'), null)
  assert.equal(com('0'), null, 'corrida de cortesia')
  assert.match(com(''), /custo/i)
  assert.match(com('abc'), /custo/i)
  assert.match(com('-5'), /negativo/i)
  assert.match(com('5000,01'), /5\.000/)
})
await confere('a empresa sugerida é a do entregador da viagem; própria ou sem entregador sugere a 99', () => {
  const entregadores = [{ id: 'e1', empresa: 'Lalamove' }, { id: 'e2', empresa: 'Propria' }]
  assert.equal(dominio.empresaPadraoDaCorrida({ entregadorId: 'e1' }, entregadores), 'Lalamove')
  assert.equal(dominio.empresaPadraoDaCorrida({ entregadorId: 'e2' }, entregadores), 'NoveNove')
  assert.equal(dominio.empresaPadraoDaCorrida({ entregadorId: null }, entregadores), 'NoveNove')
})

// --- 3. O que a viagem mostra ------------------------------------------------------------------
const CORRIDAS = [
  { id: 'c1', provedor: 'NoveNove', custoCentavos: 1890, motorista: 'João', corrigidoEm: null },
  { id: 'c2', provedor: 'Uber', custoCentavos: 300, motorista: null, corrigidoEm: '2026-10-12T20:00:00Z' },
]
await confere('cada corrida vira uma linha com empresa, custo e motorista', () => {
  assert.match(dominio.resumoDaCorrida(CORRIDAS[0]), /^99 · R\$\s18,90 · João$/)
  assert.match(dominio.resumoDaCorrida(CORRIDAS[1]), /^Uber · R\$\s3,00 · corrigida$/)
})
await confere('o custo da viagem é a soma das corridas', () => {
  assert.equal(dominio.custoDasCorridas(CORRIDAS), 2190)
  assert.equal(dominio.custoDasCorridas([]), 0)
  assert.equal(dominio.custoDasCorridas(null), 0)
})
await confere('a corrida registrada volta ao formulário para corrigir', () => {
  assert.deepEqual(dominio.formularioDaCorrida(CORRIDAS[0]), { empresa: 'NoveNove', custo: '18,90', motorista: 'João' })
})

await confere('viagem concluída no dia escolhido continua à mão para corrigir o custo', () => {
  const viagens = [
    { id: 'a', situacao: 'Concluida', concluidaEm: '2026-10-12T21:30:00Z' }, // 18h30 em São Paulo
    { id: 'b', situacao: 'Concluida', concluidaEm: '2026-10-13T02:30:00Z' }, // 23h30 do dia 12 em São Paulo
    { id: 'c', situacao: 'Concluida', concluidaEm: '2026-10-11T15:00:00Z' },
    { id: 'd', situacao: 'EmRota', concluidaEm: null },
    { id: 'e', situacao: 'Desfeita', concluidaEm: null },
  ]
  assert.deepEqual(dominio.viagensConcluidasNoDia(viagens, '2026-10-12').map((v) => v.id), ['a', 'b'])
  assert.deepEqual(dominio.viagensConcluidasNoDia(null, '2026-10-12'), [])
})

// --- 4. Rotas da API -----------------------------------------------------------------------------
await confere('a tela lista, registra e corrige nas rotas de corrida da viagem', async () => {
  chamadas.length = 0
  await infra.listarCorridas('v1')
  await infra.registrarCorrida('v1', { provedor: 'NoveNove', custoCentavos: 1890, motorista: null })
  await infra.corrigirCorrida('v1', 'c1', { custoCentavos: 2150, motorista: 'João' })
  assert.deepEqual(chamadas.map((x) => `${x.metodo} ${x.url}`), [
    'GET /api/atendimento/viagens/v1/corridas',
    'POST /api/atendimento/viagens/v1/corridas',
    'PUT /api/atendimento/viagens/v1/corridas/c1',
  ])
  assert.deepEqual(chamadas[1].corpo, { provedor: 'NoveNove', custoCentavos: 1890, motorista: null })
  assert.deepEqual(chamadas[2].corpo, { custoCentavos: 2150, motorista: 'João' })
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
process.exit(falhas.length ? 1 : 0)