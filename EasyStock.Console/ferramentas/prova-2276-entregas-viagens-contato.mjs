/* eslint-disable no-console */
// Prova da issue #2276 (auditoria UX da Casa da Babá, itens 08·5 e 06·1):
//   1. a lista de viagens do dia não mostra viagem "Montando" vazia criada em outro dia (ao virar o dia
//      ela some; com pedido, continua aparecendo até ser resolvida);
//   2. o dia de hoje, de madrugada no fuso da loja, conta como o dia da viagem criada à noite;
//   3. contato sem nome próprio ("Cliente WhatsApp") mostra o telefone formatado, no lugar do padrão;
//   4. nome real passa intacto, e as três telas que mostram o contato usam essa mesma regra.
//
//   node ferramentas/prova-2276-entregas-viagens-contato.mjs

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

const { paineisDeEntregas } = await import('../src/dominio/entregasApi.js')
const nomes = await import('../src/dominio/nomeDoContato.js')

const falhas = []
let total = 0
function confere(titulo, teste) {
  total++
  try {
    teste()
    console.log(`ok  ${titulo}`)
  } catch (erro) {
    falhas.push(titulo)
    console.log(`FALHA ${titulo}\n    ${erro.message.split('\n').slice(0, 4).join('\n    ')}`)
  }
}

const DIA = '2026-11-07'
const ONTEM = '2026-11-06T15:00:00Z' // 12h de 06/11 em Brasília
const HOJE_CEDO = '2026-11-07T12:00:00Z' // 09h de 07/11
const HOJE_A_NOITE = '2026-11-08T01:30:00Z' // 22h30 de 07/11 em Brasília (já é 08/11 em UTC)

const viagens = [
  { id: 'vazia-de-ontem', situacao: 'Montando', entregadorId: null, criadaEm: ONTEM, paradas: [] },
  { id: 'vazia-de-hoje', situacao: 'Montando', entregadorId: null, criadaEm: HOJE_CEDO, paradas: [] },
  { id: 'vazia-a-noite', situacao: 'Montando', entregadorId: null, criadaEm: HOJE_A_NOITE, paradas: [] },
  { id: 'com-pedido-de-ontem', situacao: 'Montando', entregadorId: null, criadaEm: ONTEM, paradas: [{ pedidoId: 'p1', ordem: 1 }] },
  { id: 'em-rota-de-ontem', situacao: 'EmRota', entregadorId: 'x', criadaEm: ONTEM, paradas: [] },
]

confere('08·5: viagem Montando vazia de outro dia não aparece na lista do dia', () => {
  const p = paineisDeEntregas([], viagens, DIA)
  assert.deepEqual(p.montando.map((v) => v.id), ['vazia-de-hoje', 'vazia-a-noite', 'com-pedido-de-ontem'])
})

confere('08·5: viagem em rota continua na lista, mesmo vazia de outro dia', () => {
  const p = paineisDeEntregas([], viagens, DIA)
  assert.deepEqual(p.emRota.map((v) => v.id), ['em-rota-de-ontem'])
})

confere('08·5: viagem criada à noite no fuso da loja conta como do dia', () => {
  const p = paineisDeEntregas([], [viagens[2]], DIA)
  assert.deepEqual(p.montando.map((v) => v.id), ['vazia-a-noite'])
  const outroDia = paineisDeEntregas([], [viagens[2]], '2026-11-08')
  assert.deepEqual(outroDia.montando, [])
})

// A referência é o dia de HOJE, não o dia escolhido na tela: a vazia de hoje segue visível mesmo que o
// operador esteja olhando amanhã, e a de ontem some. O terceiro parâmetro é só "hoje" (default: agora).
confere('08·5: vazia criada hoje segue visível quando hoje = 07/11', () => {
  const p = paineisDeEntregas([], [viagens[1]], DIA)
  assert.deepEqual(p.montando.map((v) => v.id), ['vazia-de-hoje'])
})

confere('08·5: chamada sem o 3º parâmetro usa hoje (agora): vazia criada agora aparece', () => {
  const agora = new Date().toISOString()
  const p = paineisDeEntregas([], [{ id: 'agora', situacao: 'Montando', criadaEm: agora, paradas: [] }])
  assert.deepEqual(p.montando.map((v) => v.id), ['agora'])
})

confere('08·5: chamada sem o 3º parâmetro esconde a vazia de dois dias atrás', () => {
  const antes = new Date(Date.now() - 2 * 86400000).toISOString()
  const p = paineisDeEntregas([], [{ id: 'antiga', situacao: 'Montando', criadaEm: antes, paradas: [] }])
  assert.deepEqual(p.montando, [])
})

// criadaEm ausente ou inválido não pode derrubar a tela (RangeError do fuso) nem sumir com a viagem.
confere('08·5: criadaEm inválido ou ausente não lança erro e não esconde a viagem', () => {
  const sujas = [
    { id: 'invalida', situacao: 'Montando', criadaEm: 'nao-e-data', paradas: [] },
    { id: 'ausente', situacao: 'Montando', paradas: [] },
  ]
  let p
  assert.doesNotThrow(() => { p = paineisDeEntregas([], sujas, DIA) })
  assert.deepEqual(p.montando.map((v) => v.id), ['invalida', 'ausente'])
})

// A tela de Entregas não pode mais passar o dia escolhido como referência.
confere('08·5: a tela de Entregas não usa o dia escolhido como hoje', () => {
  const fonte = readFileSync(new URL('../src/features/entregas/ConteudoEntregasApi.jsx', import.meta.url), 'utf8')
  assert.doesNotMatch(fonte, /paineisDeEntregas\(pedidos, viagens, dia\)/)
})

confere('06·1: contato "Cliente WhatsApp" sem nome mostra o telefone formatado', () => {
  assert.equal(typeof nomes.nomeDaConversa, 'function', 'nomeDaConversa precisa existir em dominio/nomeDoContato.js')
  assert.equal(nomes.nomeDaConversa({ nome: 'Cliente WhatsApp', contato: '555434619500' }), '+55 (54) 3461-9500')
  assert.equal(nomes.nomeDaConversa({ nome: 'Cliente WhatsApp', contato: '5511987654321' }), '+55 (11) 98765-4321')
})

confere('06·1: primeiro nome do contato sem nome mostra o telefone inteiro, não o "+55"', () => {
  assert.equal(typeof nomes.primeiroNomeDaConversa, 'function', 'primeiroNomeDaConversa precisa existir')
  assert.equal(nomes.primeiroNomeDaConversa({ nome: 'Cliente WhatsApp', contato: '555434619500' }), '+55 (54) 3461-9500')
  assert.equal(nomes.primeiroNomeDaConversa({ nome: 'Maria Souza', contato: '555434619500' }), 'Maria')
})

confere('06·1: nome real passa intacto, mesmo com telefone no contato', () => {
  assert.equal(nomes.nomeDaConversa({ nome: 'Maria Souza', contato: '555434619500' }), 'Maria Souza')
})

confere('06·1: padrão sem telefone utilizável segue como veio', () => {
  assert.equal(nomes.nomeDaConversa({ nome: 'Cliente WhatsApp', contato: 'abc-123' }), 'Cliente WhatsApp')
  assert.equal(nomes.nomeDaConversa({ nome: 'Cliente WhatsApp', contato: null }), 'Cliente WhatsApp')
})

confere('06·1: as telas que mostram o contato leem o nome pela mesma regra', () => {
  const telas = [
    'src/features/atendimento/PainelAtendimento.jsx',
    'src/features/caixa-de-entrada/apresentacaoDoCartao.js',
    'src/app/Moldura.jsx',
    'src/features/atendimento/Composer.jsx',
    'src/features/atendimento/DevolverAoAssistente.jsx',
    'src/features/atendimento/PassarConversa.jsx',
    'src/features/atendimento/ModalProgramarMensagem.jsx',
    'src/features/atendimento/ModalEnviarModelo.jsx',
  ]
  const base = new URL('../', import.meta.url)
  for (const tela of telas) {
    const fonte = readFileSync(new URL(tela, base), 'utf8')
    assert.match(fonte, /(primeiroNomeDaConversa|nomeDaConversa)\(/, `${tela} precisa usar nomeDaConversa ou primeiroNomeDaConversa`)
  }
})

if (falhas.length > 0) {
  console.log(`\n${falhas.length} falha(s) de ${total}`)
  process.exit(1)
}
console.log('\nprova-2276: todos os testes passaram')
