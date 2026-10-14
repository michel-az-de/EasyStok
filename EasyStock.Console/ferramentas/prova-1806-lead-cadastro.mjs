/* eslint-disable no-console */
// Prova #1806 (revisão gravada de 14/10): "não posso vender para um lead que não tem cadastro;
// aparece a telinha de cadastro auto preenchida". No modo API, lead sem cadastro e sem comanda
// tem como primeiro passo cadastrar; cliente cadastrado e a demonstração seguem para os itens.
//
//   node ferramentas/prova-1806-lead-cadastro.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    try {
      return proximo(especificador, contexto)
    } catch (erro) {
      if (especificador.startsWith('.') && !/\.m?jsx?$/.test(especificador)) {
        return proximo(especificador + '.js', contexto)
      }
      throw erro
    }
  },
})

const { primeiroPassoAntesDaVenda } = await import('../src/dominio/ficha.js')

let passos = 0
const ok = (texto) => { passos += 1; console.log(`ok ${passos} - ${texto}`) }

assert.equal(primeiroPassoAntesDaVenda({ conversa: { conta: 'lead', clienteId: null }, pedido: null, fonteApi: true }), 'cadastro')
ok('lead sem cadastro e sem comanda: primeiro passo é cadastrar')
assert.equal(primeiroPassoAntesDaVenda({ conversa: { conta: 'cliente', clienteId: 'c1' }, pedido: null, fonteApi: true }), 'itens')
ok('cliente cadastrado: primeiro passo são os itens')
assert.equal(primeiroPassoAntesDaVenda({ conversa: { conta: 'lead', clienteId: null }, pedido: { itens: [] }, fonteApi: true }), null)
ok('com comanda aberta, a barra do pedido é quem diz o próximo passo')
assert.equal(primeiroPassoAntesDaVenda({ conversa: { conta: 'lead', clienteId: null }, pedido: null, fonteApi: false }), 'itens')
ok('demonstração segue igual')

console.log(`\n${passos} passos verdes.`)
