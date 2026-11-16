/* eslint-disable no-console */
// #2734 (D2): os passos da corrida só usam tons que a Pilula conhece (ok|aviso|perigo|marca|neutro).
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
const { PASSOS_CORRIDA } = await import('../src/dominio/corrida.js')

assert.deepEqual(Object.fromEntries(Object.entries(PASSOS_CORRIDA).map(([passo, d]) => [passo, d.tom])), {
  procurando: 'aviso',
  'a-caminho-coleta': 'aviso',
  coletado: 'ok',
  entregue: 'ok',
  falha: 'perigo',
  cancelado: 'perigo',
})
const TONS_DA_PILULA = ['ok', 'aviso', 'perigo', 'marca', 'neutro']
for (const [passo, d] of Object.entries(PASSOS_CORRIDA)) assert.ok(TONS_DA_PILULA.includes(d.tom), `${passo}: tom ${d.tom} desconhecido da Pilula`)
console.log('#2734 corrida: tons dos passos são todos da Pilula.')
