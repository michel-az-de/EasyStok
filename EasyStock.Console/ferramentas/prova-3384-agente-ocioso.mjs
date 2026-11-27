// #3384 (r4 P1-2): o Assistente ocioso não diz "Nada pendente" quando há mensagem do cliente sem resposta.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
    }
    return proximo(spec, contexto)
  },
})
const { fraseSemResposta } = await import('../src/dominio/mensagem.js')
const em = (m) => new Date(Date.UTC(2026, 10, 25, 12, m)).toISOString()
const conversa = (...dirs) => ({ mensagens: dirs.map((dir, i) => ({ id: `m${i}`, dir, texto: 'oi', em: em(i), status: 'enviada' })) })

assert.equal(fraseSemResposta(conversa('in')), '1 mensagem sem resposta')
assert.equal(fraseSemResposta(conversa('in', 'in')), '2 mensagens sem resposta')
assert.equal(fraseSemResposta(conversa('in', 'out')), null, 'respondida: nada pendente de verdade')
assert.equal(fraseSemResposta({ mensagens: [] }), null)
console.log('prova-3384-agente-ocioso: ok')
