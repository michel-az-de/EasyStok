import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(spec, ctx, next) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return next(spec + '.js', ctx)
    return next(spec, ctx)
  },
  load(url, ctx, next) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const API_BASE = ''" }
    return next(url, ctx)
  },
})
const { carregarNotificacoesDaSessao, lerNotificacaoDaSessao } = await import('../src/infra/api/notificacoesDaSessao.js')
const chamadas = []
globalThis.fetch = async (url, opcoes) => {
  chamadas.push({ url, ...opcoes })
  const data = url.endsWith('/badge') ? { count: 12 } : [{ id: 'aviso-1', titulo: 'Aviso da cozinha' }]
  return new Response(JSON.stringify({ data }), { status: 200 })
}
const resultado = await carregarNotificacoesDaSessao()
assert.equal(resultado.total, 12)
assert.equal(resultado.recentes.length, 1)
assert.deepEqual(chamadas.map(c => c.url), ['/api/notificacoes/badge', '/api/notificacoes/recentes?limit=10'])
await lerNotificacaoDaSessao('id/teste')
assert.equal(chamadas.at(-1).method, 'PATCH')
assert.equal(chamadas.at(-1).url, '/api/notificacoes/id%2Fteste/lida')
// Falha de conexão ou resposta inválida não pode virar "nenhum aviso".
globalThis.fetch = async () => { throw new Error('offline') }
await assert.rejects(carregarNotificacoesDaSessao, /Sem conexão/)
globalThis.fetch = async () => new Response('{}', { status: 200 })
await assert.rejects(carregarNotificacoesDaSessao, /não foram recebidos/)
globalThis.fetch = async () => new Response('{"error":{"message":"Indisponível"}}', { status: 503 })
await assert.rejects(lerNotificacaoDaSessao('aviso-1'), /Indisponível/)
console.log('Avisos: badge da API, lista limitada, leitura individual, IDs escapados e falhas sem falso vazio aprovados.')
