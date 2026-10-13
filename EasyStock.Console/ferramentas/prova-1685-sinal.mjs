import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({ resolve(spec, contexto, proximo) {
  if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
    try { return proximo(spec + '.js', contexto) } catch { /* resolução original */ }
  }
  return proximo(spec, contexto)
} })
const { aguardaSinalManual, valorSugeridoParaBaixa } = await import('../src/dominio/cobranca.js')
const base = { sinalNaReserva:true, statusApi:'aguardando_pagamento', totalApi:100,
  totalPagoApi:0, cobranca:{valor:100,link:null,pagaEm:null} }
assert.equal(aguardaSinalManual(base), true)
assert.equal(valorSugeridoParaBaixa(base), 50)
assert.equal(valorSugeridoParaBaixa({...base,totalPagoApi:20}), 30)
assert.equal(valorSugeridoParaBaixa({...base,totalApi:25.01}), 12.51)
assert.equal(valorSugeridoParaBaixa({...base,totalApi:100.01,totalPagoApi:20}), 30.01)
assert.equal(valorSugeridoParaBaixa({...base,alocadoApi:0,totalPagoApi:20}), 50)
assert.equal(valorSugeridoParaBaixa({...base,alocadoApi:50}), 0)
assert.equal(valorSugeridoParaBaixa({...base,alocadoApi:60}), 0)
assert.equal(valorSugeridoParaBaixa({...base,statusApi:'aguardando',alocadoApi:50}), 50)
assert.equal(valorSugeridoParaBaixa({...base,sinalNaReserva:false}), 100)
assert.equal(valorSugeridoParaBaixa({...base,cobranca:{...base.cobranca,link:'https://example.test'}}), 100)
assert.equal(aguardaSinalManual({...base,cobranca:{...base.cobranca,canceladaEm:1}}), false)
assert.equal(aguardaSinalManual({...base,cobranca:{...base.cobranca,pagaEm:1}}), false)
assert.equal(aguardaSinalManual({...base,statusApi:'cancelado'}), false)
console.log('OK: 14 verificações de entrada, saldo, alocação, arredondamento e cobranças encerradas.')
