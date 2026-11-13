// Prova da #2305 (CAIXA-1): o console normaliza a forma de pagamento como o servidor
// (`MetodosPagamentoCaixa.Normalizar`: trim + minúscula). Com "Dinheiro " gravado, a gaveta do
// console dava R$ 185,47 contra R$ 186,47 do servidor.
//
// Roda com: node ferramentas/prova-2305-caixa-metodo.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const { gavetaDoDia, nomeDoMetodoCaixa } = await import('../src/dominio/caixa.js')

const dia = (meio) => ({
  saldoInicial: 100,
  movimentos: [{ tipo: 'entrada', valor: 86.47, meio }],
})

for (const meio of ['dinheiro', 'Dinheiro ', ' DINHEIRO']) {
  assert.equal(gavetaDoDia(dia(meio)).naGaveta, 186.47, `gaveta com meio ${JSON.stringify(meio)}`)
}
assert.equal(gavetaDoDia(dia('Pix ')).naGaveta, 100)
assert.equal(gavetaDoDia(dia('Pix ')).pixECartao, 86.47)
assert.equal(nomeDoMetodoCaixa(' Pix'), 'Pix')

console.log('prova-2305-caixa-metodo: ok')
