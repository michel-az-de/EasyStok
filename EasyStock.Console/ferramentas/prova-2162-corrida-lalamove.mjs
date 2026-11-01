/* eslint-disable no-console */
// Prova da issue #2162: a corrida da Lalamove chamada pela API, na viagem.
//   1. "Chamar Lalamove" só com a loja conectada, a viagem montando, uma parada e nenhuma corrida em curso;
//   2. o status do provedor vira rótulo de balcão com tom, e a corrida em curso liga a atualização sozinha;
//   3. a tela fala com as rotas da API (cotação, chamada, consulta, cancelamento, gorjeta e conexão);
//   4. a chamada não leva preço: só o id da cotação, que o servidor relê na Lalamove.
//
//   node ferramentas/prova-2162-corrida-lalamove.mjs

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
  return new Response(JSON.stringify({ data: {} }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const dominio = await import('../src/dominio/entregasApi.js')
const infra = await import('../src/infra/api/entregasApi.js')

let passou = 0
async function confere(nome, fn) {
  await fn()
  passou += 1
  console.log(`ok  ${nome}`)
}

const montando = { id: 'v1', situacao: 'Montando', paradas: [{ pedidoId: 'p1' }] }
const emCurso = { id: 'c1', origem: 'Api', status: 'ProcurandoMotorista', ativa: true }
const manual = { id: 'c0', origem: 'Manual', provedor: 'NoveNove', custoCentavos: 1890 }

await confere('1. Chamar Lalamove só com conexão, viagem montando, uma parada e sem corrida em curso', () => {
  assert.equal(dominio.podeChamarLalamove(montando, [], true), true)
  assert.equal(dominio.podeChamarLalamove(montando, [manual], true), true, 'corrida manual não bloqueia')
  assert.equal(dominio.podeChamarLalamove(montando, [], false), false, 'sem conexão o botão some')
  assert.equal(dominio.podeChamarLalamove({ ...montando, situacao: 'EmRota' }, [], true), false)
  assert.equal(dominio.podeChamarLalamove(montando, [emCurso], true), false, 'uma corrida em curso por viagem')
  const duas = { ...montando, paradas: [{}, {}] }
  assert.equal(dominio.podeChamarLalamove(duas, [], true), false)
  assert.match(dominio.motivoSemChamada(duas, [], true), /um pedido por vez/, 'o motivo aparece em texto')
  assert.equal(dominio.motivoSemChamada(montando, [], false), null, 'sem conexão não há texto: a tela não promete')
})

await confere('2. status do provedor em rótulo de balcão; corrida em curso liga a atualização', () => {
  // #2168: cada status vira Selo (tom, ícone, texto) e etapa da linha do tempo; a tabela inteira, sem buraco.
  const tabela = Object.fromEntries(['Solicitando', 'ProcurandoMotorista', 'MotoristaACaminho', 'Coletada', 'Entregue', 'Cancelada',
    'Rejeitada', 'Expirada', 'Falha'].map((s) => [s, dominio.statusDaCorrida({ status: s })]))
  assert.deepEqual(Object.fromEntries(Object.entries(tabela).map(([s, v]) => [s, [v.tom, v.icone, v.etapa]])), {
    Solicitando: ['neutro', 'hourglass', 0], ProcurandoMotorista: ['alerta', 'lupa', 1], MotoristaACaminho: ['marca', 'moto', 2],
    Coletada: ['marca', 'package', 3], Entregue: ['sucesso', 'circle-check', 4], Cancelada: ['neutro', 'circle-x', null],
    Rejeitada: ['erro', 'alerta', null], Expirada: ['erro', 'alerta', null], Falha: ['erro', 'circle-x', null],
  })
  assert.equal(tabela.MotoristaACaminho.rotulo, 'Motorista a caminho da cozinha')
  assert.ok(Object.values(tabela).every((v) => v.rotulo && !/_/.test(v.rotulo)), 'nenhum status cru da Lalamove na tela')
  assert.equal(dominio.corridaEmCurso([manual, emCurso]), true)
  assert.equal(dominio.corridaEmCurso([manual, { ...emCurso, ativa: false }]), false)
  assert.equal(dominio.corridaDaApi(manual), false)
  assert.equal(dominio.motoristaDaCorrida({ motorista: 'Carlos', motoristaPlaca: 'XYZ9A88' }), 'Carlos · XYZ9A88')
  assert.equal(dominio.resumoDaCotacao({ valorCentavos: 1740, distanciaMetros: 4120 }).replace(/\s/g, ' '), 'R$ 17,40 · 4,1 km')
})

await confere('3. rotas da API da corrida', async () => {
  chamadas.length = 0
  await infra.conexaoDespacho()
  await infra.cotarCorrida('v1', 'Carro')
  await infra.chamarCorrida('v1', { cotacaoId: 'q1', veiculo: 'Carro', observacao: null })
  await infra.atualizarCorrida('v1', 'c1')
  await infra.cancelarCorrida('v1', 'c1')
  await infra.gorjetaCorrida('v1', 'c1', 500)
  await infra.liberarCorrida('v1', 'c1')
  assert.deepEqual(chamadas.map((c) => `${c.metodo} ${c.url}`), [
    'GET /api/atendimento/corridas/conexao',
    'POST /api/atendimento/viagens/v1/corridas/cotacao',
    'POST /api/atendimento/viagens/v1/corridas/chamar',
    'GET /api/atendimento/viagens/v1/corridas/c1',
    'POST /api/atendimento/viagens/v1/corridas/c1/cancelar',
    'POST /api/atendimento/viagens/v1/corridas/c1/gorjeta',
    'POST /api/atendimento/viagens/v1/corridas/c1/liberar',
  ])
  assert.deepEqual(chamadas[1].corpo, { veiculo: 'Carro' })
  assert.deepEqual(chamadas[5].corpo, { centavos: 500 })
})

// #2168: o corpo da chamada, a tela montada e o "Corrigir" só na corrida manual estão na prova-2168, com o cartão de verdade.
await confere('4. a chamada leva só o id da cotação, nunca o preço', () => {
  assert.deepEqual(chamadas[2].corpo, { cotacaoId: 'q1', veiculo: 'Carro', observacao: null }, 'o servidor relê a cotação na Lalamove')
})

console.log(`\n${passou} verificações passaram.`)
