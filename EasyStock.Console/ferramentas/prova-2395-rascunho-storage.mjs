import assert from 'node:assert/strict'
import { gravarRascunhos, lerRascunhos } from '../src/infra/api/rascunhosDaSessao.js'
const sessao = { empresa: { id: 'loja' }, usuario: { id: 'pessoa' } }
const texto = { conversa: 'Texto inteiro só em memória enquanto a quota está bloqueada.' }
globalThis.sessionStorage = { setItem() { throw new DOMException('Quota bloqueada', 'QuotaExceededError') }, removeItem() {}, getItem() { return null } }
assert.equal(gravarRascunhos(sessao, texto), false, 'Falha de persistência de texto é observável pela tela')
const salvos = new Map()
globalThis.sessionStorage = { setItem: (k,v) => salvos.set(k,v), removeItem: k => salvos.delete(k), getItem: k => salvos.get(k) ?? null }
assert.equal(gravarRascunhos(sessao, texto), true)
assert.deepEqual(lerRascunhos(sessao), texto)
assert.deepEqual(lerRascunhos({ ...sessao, usuario: { id: 'outra' } }), {})
console.log('Rascunho: falha explícita, retry e texto integral isolado por sessão passaram.')
