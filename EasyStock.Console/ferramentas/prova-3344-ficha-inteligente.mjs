// #3344: a Ficha do cliente lê o cliente como a lista de Clientes (estágio, risco, última compra, sinais) e sugere
// pratos só de compra real. Sem a linha da lista não há leitura; sem compra não há prato.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) return proximo(especificador + '.js', contexto)
    return proximo(especificador, contexto)
  },
})
const { leituraDoCliente, proximoPassoDoCliente } = await import('../src/dominio/leituraDoCliente.js')
const { pratosParaOferecer, perguntaSobreOCliente, pedidoValido } = await import('../src/dominio/pratosParaOferecer.js')

let falhas = 0
const caso = (nome, corpo) => { try { corpo(); console.log(`ok    ${nome}`) } catch (e) { falhas++; console.log(`FALHA ${nome}: ${e.message.split('\n')[0]}`) } }
const agora = Date.parse('2026-11-25T15:00:00Z')

caso('sem a linha da lista não há leitura', () => {
  for (const r of [undefined, null, {}, { estagio: 'inventado' }]) assert.equal(leituraDoCliente(r, agora), null)
})
caso('recorrente em risco: estágio, posição, dias e próximo passo', () => {
  const l = leituraDoCliente({ estagio: 'recorrente', emRisco: true, compras: 3, ultimaCompraEm: '2026-10-26T12:00:00Z' }, agora)
  assert.equal(l.estagio, 'Recorrente'); assert.equal(l.posicao, 2); assert.equal(l.emRisco, true)
  assert.equal(l.ultimaCompra, 'Pediu há 30 dias')
  assert.match(proximoPassoDoCliente(l), /sem pedir/)
})
caso('sem pedido: "Ainda não pediu" e apresenta o cardápio', () => {
  const l = leituraDoCliente({ estagio: 'sem_pedido', compras: 0 }, agora)
  assert.equal(l.ultimaCompra, 'Ainda não pediu'); assert.equal(l.posicao, 0)
  assert.match(proximoPassoDoCliente(l), /cardápio/)
})
caso('reclamação aberta vem antes de qualquer oferta', () => {
  const l = leituraDoCliente({ estagio: 'fiel', ocorrenciasAbertas: 2, ocorrenciasTotal: 3, ultimaCompraEm: '2026-11-25T10:00:00Z' }, agora)
  assert.equal(l.reclamacaoAberta, '2 reclamações abertas'); assert.equal(l.reclamacaoResolvida, null); assert.equal(l.ultimaCompra, 'Pediu hoje')
  assert.match(proximoPassoDoCliente(l), /reclamação/)
})
caso('só resolvidas: diz quantas e não bloqueia', () => {
  const l = leituraDoCliente({ estagio: 'fiel', ocorrenciasAbertas: 0, ocorrenciasTotal: 1 }, agora)
  assert.equal(l.reclamacaoResolvida, '1 reclamação resolvida'); assert.equal(proximoPassoDoCliente(l), null)
})
caso('sem data de compra e com compras: não inventa "há N dias"', () => {
  assert.equal(leituraDoCliente({ estagio: 'recorrente', compras: 2 }, agora).ultimaCompra, null)
})
caso('pratos: favorito primeiro, depois o último pedido, sem repetir e no máximo 3', () => {
  const p = pratosParaOferecer({ favorito: 'Molho bechamel', ultimoPedido: { itens: [{ nome: 'molho BECHAMEL' }, { nome: 'Lasanha' }, { nome: 'Nhoque' }, { nome: 'Ravióli' }] } })
  assert.deepEqual(p.map((x) => x.nome), ['Molho bechamel', 'Lasanha', 'Nhoque'])
  assert.equal(p[0].motivo, 'Costuma pedir'); assert.equal(p[1].motivo, 'Pediu no último pedido')
})
caso('sem compra não há prato', () => {
  assert.deepEqual(pratosParaOferecer(), []); assert.deepEqual(pratosParaOferecer({ favorito: '  ', ultimoPedido: { itens: [] } }), [])
})
caso('pedido cancelado não conta', () => {
  assert.equal(pedidoValido({ status: 'cancelado' }), false); assert.equal(pedidoValido({ status: 'entregue' }), true); assert.equal(pedidoValido(null), false)
})
caso('pergunta ao Assistente leva só nome próprio, estágio e pratos; recompra proíbe desconto', () => {
  const leitura = leituraDoCliente({ estagio: 'recorrente', emRisco: true, compras: 3, ultimaCompraEm: '2026-10-26T12:00:00Z' }, agora)
  const q = perguntaSobreOCliente('recompra', { primeiroNome: 'Andreza', leitura, pratos: pratosParaOferecer({ favorito: 'Molho bechamel' }) })
  assert.match(q, /Andreza/); assert.match(q, /Molho bechamel \(costuma pedir\)/); assert.match(q, /Não prometa desconto/)
  assert.doesNotMatch(q, /\d{4,}/)
})
caso('restrição alimentar entra na pergunta e proíbe o que a contraria', () => {
  const q = perguntaSobreOCliente('pratos', { primeiroNome: 'Ana', leitura: null, pratos: pratosParaOferecer({ favorito: 'Lasanha' }), restricoes: ['sem lactose'] })
  assert.match(q, /Restrições alimentares: sem lactose/); assert.match(q, /contrarie/)
})

if (falhas) { console.log(`\n${falhas} falha(s)`); process.exit(1) }
console.log('\ntodas passaram')
