import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

globalThis.chamadasFotoCliente = []
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec === './cliente') return proximo('./cliente.js', contexto)
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/api/cliente.js')) return {
      format: 'module', shortCircuit: true,
      source: 'export const chamarApi=(url,opcoes)=>{ globalThis.chamadasFotoCliente.push({url,opcoes});return Promise.resolve(null) }',
    }
    return proximo(url, contexto)
  },
})
const { obterFotoCliente, enviarFotoCliente, removerFotoCliente } = await import('../src/infra/api/fotoClienteApi.js')
const id = 'a1111111-1111-4111-8111-111111111111'
const rota = `/api/clientes/${id}/foto?v=${'a'.repeat(32)}`
await obterFotoCliente(rota)
assert.equal(chamadasFotoCliente[0].url, rota)
assert.equal(chamadasFotoCliente[0].opcoes.arquivo, true)
for (const invalida of ['https://externo.invalid/foto', '//externo.invalid/foto', `${rota}&token=indevido`, '/api/clientes/outro/foto']) {
  assert.throws(() => obterFotoCliente(invalida), /inválido/)
}
assert.equal(chamadasFotoCliente.length, 1)
await enviarFotoCliente(id, new Blob(['foto'], { type: 'image/jpeg' }))
assert.equal(chamadasFotoCliente[1].opcoes.metodo, 'POST')
assert.ok(chamadasFotoCliente[1].opcoes.formulario.get('file'))
await removerFotoCliente(id)
assert.equal(chamadasFotoCliente[2].opcoes.metodo, 'DELETE')
assert.throws(() => removerFotoCliente('https://externo.invalid'), /inválido/)
console.log('Foto do cliente: rota privada versionada, download autenticado, multipart e recusa de URL externa aprovados.')
