/* eslint-disable no-console */
// #2495 (S5, B6): aviso de cliente duplicado. A busca da API é por "contém"; o console só avisa quem tem
// os mesmos dígitos, sem o 55 do Brasil quando o número tem 12 ou 13 dígitos, e nunca o próprio cliente.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})
const { termoDeDuplicado, duplicadosDoCliente, avisoDeDuplicado } = await import('../src/dominio/clienteDuplicado.js')

// Termo: só a partir de 8 dígitos de telefone e 11 de documento; o 55 do Brasil sai.
assert.equal(termoDeDuplicado('telefone', '(11) 9999'), null)
assert.equal(termoDeDuplicado('telefone', '(11) 99999-8888'), '11999998888')
assert.equal(termoDeDuplicado('telefone', '+55 11 99999-8888'), '11999998888')
assert.equal(termoDeDuplicado('telefone', '3333-4444'), '33334444')
assert.equal(termoDeDuplicado('documento', '123.456.789'), null)
assert.equal(termoDeDuplicado('documento', '123.456.789-00'), '12345678900')
assert.equal(termoDeDuplicado('nome', 'Ana'), null)

const itens = [
  { id: 'A', nome: 'Ana', telefone: '+5511999998888', documento: '12345678900' },
  { id: 'B', nome: 'Bia', telefone: '11999998888' },
  { id: 'C', nome: 'Caio', telefone: '+5511999998888777' },
  { id: 'D', nome: 'Duda', telefone: '1199999888', documento: '123456789001' },
  { id: 'E', nome: '', telefone: '(11) 99999-8888' },
]
// Mesmo telefone com e sem +55 conta; "contém" não conta; o próprio cliente não conta.
assert.deepEqual(duplicadosDoCliente('telefone', '11 99999-8888', itens, 'B').map(c => c.id), ['A', 'E'])
assert.deepEqual(duplicadosDoCliente('telefone', '5511999998888', itens, undefined).map(c => c.id), ['A', 'B', 'E'])
assert.deepEqual(duplicadosDoCliente('documento', '123.456.789-00', itens, undefined).map(c => c.id), ['A'])
assert.deepEqual(duplicadosDoCliente('telefone', '1199', itens, undefined), [], 'Abaixo do mínimo nada avisa')
assert.deepEqual(duplicadosDoCliente('telefone', '11999998888', undefined, undefined), [])

assert.equal(avisoDeDuplicado('telefone', itens[0]), 'Já existe Ana com este telefone.')
assert.equal(avisoDeDuplicado('documento', itens[0]), 'Já existe Ana com este documento.')
assert.equal(avisoDeDuplicado('telefone', itens[4]), 'Já existe um cadastro sem nome com este telefone.')
console.log('#2495 S5 domínio: só dígitos iguais avisam, sem o 55, sem o próprio cliente.')
