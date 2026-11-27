// #3387: a ficha mostra o ritmo de compra que a API calculou (mediana das últimas compras, mínimo 3) e destaca a
// última compra parada há 30 dias ou mais. O Console não calcula ritmo nem cria sinal novo: só lê e diz o fato.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) return proximo(especificador + '.js', contexto)
    return proximo(especificador, contexto)
  },
})
const { leituraDoCliente, proximoPassoDoCliente, ritmoDoCliente, ESTAGIOS_DA_LEITURA } = await import('../src/dominio/leituraDoCliente.js')
const { relacionamentoDoDossie } = await import('../src/infra/api/traducaoCliente.js')

let falhas = 0
const caso = (nome, corpo) => { try { corpo(); console.log(`ok    ${nome}`) } catch (e) { falhas++; console.log(`FALHA ${nome}: ${e.message.split('\n')[0]}`) } }
const agora = Date.parse('2026-11-25T15:00:00Z')

caso('ritmo vem da API e só aparece com amostra', () => {
  assert.deepEqual(ritmoDoCliente({ dias: 14, compras: 6 }), { texto: 'Pede a cada ~14 dias', apoio: 'mediana das últimas 6 compras' })
  assert.deepEqual(ritmoDoCliente({ dias: 1, compras: 3 }), { texto: 'Pede todo dia', apoio: 'mediana das últimas 3 compras' })
  for (const r of [null, undefined, {}, { dias: 0, compras: 5 }, { dias: 9, compras: 2 }]) assert.equal(ritmoDoCliente(r), null)
})
caso('última compra parada há 30 dias ou mais fica destacada; antes disso não', () => {
  assert.equal(leituraDoCliente({ estagio: 'recorrente', compras: 3, ultimaCompraEm: '2026-10-26T12:00:00Z' }, agora).parada, true)
  assert.equal(leituraDoCliente({ estagio: 'fiel', compras: 8, ultimaCompraEm: '2026-11-20T12:00:00Z' }, agora).parada, false)
  assert.equal(leituraDoCliente({ estagio: 'sem_pedido', compras: 0 }, agora).parada, false)
})
caso('todo estágio tem um próximo passo; o urgente vem antes', () => {
  for (const estagio of ['sem_pedido', 'primeira_compra', 'recorrente', 'fiel']) assert.ok(proximoPassoDoCliente(leituraDoCliente({ estagio, compras: 1 }, agora)), estagio)
  assert.match(proximoPassoDoCliente(leituraDoCliente({ estagio: 'fiel', emRisco: true, compras: 9 }, agora)), /sem pedir/)
})
caso('os 4 estágios têm nome para a régua da ficha', () => {
  assert.deepEqual(ESTAGIOS_DA_LEITURA, ['Sem pedido', 'Primeira compra', 'Recorrente', 'Fiel'])
})
caso('o dossiê traz ritmo e primeira compra para a ficha', () => {
  const base = { cliente: { id: 'c1', nome: 'Ana' }, notas: [], tags: [], conversasRecentes: [], ultimosPedidos: [], totalPedidos: 6, totalGasto: 642 }
  const lido = relacionamentoDoDossie({ ...base, ritmo: { medianaDias: 14, comprasAnalisadas: 6 }, primeiraCompraEm: '2026-04-02T12:00:00' })
  assert.deepEqual(lido.ritmo, { dias: 14, compras: 6 })
  assert.equal(typeof lido.primeiraCompraEm, 'number')
  const antigo = relacionamentoDoDossie(base)
  assert.equal(antigo.ritmo, null, 'API antiga, sem o campo: sem ritmo, sem erro')
  assert.equal(antigo.primeiraCompraEm, null)
})

if (falhas) { console.log(`\n${falhas} falha(s)`); process.exit(1) }
console.log('\n#3387 leitura: ritmo e última compra parada conferidos.')
