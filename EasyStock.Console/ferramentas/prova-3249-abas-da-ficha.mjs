// #3249: as abas da Ficha do cliente (Pedidos, Endereços, Telefones e Conversas). O número da aba é sempre o total
// conhecido: pedidos pelo cadastro (orderCount), endereços e telefones pela lista completa do detalhe. As conversas
// do dossiê têm teto, então a aba não tem número; sem dossiê lido, não há aba de conversas. Total desconhecido não
// vira "0".
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) return proximo(especificador + '.js', contexto)
    return proximo(especificador, contexto)
  },
})
const { abasDaFicha } = await import('../src/dominio/fichaCliente.js')

let falhas = 0
const caso = (nome, corpo) => { try { corpo(); console.log(`ok    ${nome}`) } catch (e) { falhas++; console.log(`FALHA ${nome}: ${e.message.split('\n')[0]}`) } }

caso('cliente com tudo: quatro abas na ordem, com o total e o nome acessível', () => {
  assert.deepEqual(abasDaFicha({ pedidos: 12, enderecos: 2, telefones: 3, comConversas: true }), [
    { id: 'pedidos', rotulo: 'Pedidos', contador: 12, nome: 'Pedidos, 12' },
    { id: 'enderecos', rotulo: 'Endereços', contador: 2, nome: 'Endereços, 2' },
    { id: 'telefones', rotulo: 'Telefones', contador: 3, nome: 'Telefones, 3' },
    { id: 'conversas', rotulo: 'Conversas' },
  ])
})
caso('sem dossiê lido, sem aba de conversas', () => {
  assert.deepEqual(abasDaFicha({ pedidos: 0, enderecos: 0, telefones: 1, comConversas: false }).map(a => a.id), ['pedidos', 'enderecos', 'telefones'])
})
caso('zero conhecido aparece como 0', () => {
  assert.equal(abasDaFicha({ pedidos: 0, enderecos: 0, telefones: 0 })[0].contador, 0)
})
caso('total desconhecido não vira 0: sem número e sem número no nome', () => {
  for (const pedidos of [undefined, null, '', 'sete', -1, 2.5]) {
    const [aba] = abasDaFicha({ pedidos, enderecos: 1, telefones: 1 })
    assert.deepEqual(aba, { id: 'pedidos', rotulo: 'Pedidos' }, `pedidos=${String(pedidos)}`)
  }
})
caso('contagem que chega como texto numérico vale', () => {
  assert.equal(abasDaFicha({ pedidos: '7', enderecos: 1, telefones: 1 })[0].contador, 7)
})

if (falhas) { console.log(`\n${falhas} falha(s)`); process.exit(1) }
console.log('\nAbas da Ficha do cliente: ordem, totais e ausência de número aprovadas.')
