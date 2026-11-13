/* eslint-disable no-console */
// Prova da #2440 (A1): texto, foto, áudio, foto do cardápio e modelo em conversa de OUTRA atendente recebem o 409
// CONVERSA_DE_OUTRA, perguntam (window.confirm com a mensagem da API) e repetem com `confirmar`, com a MESMA chave
// de idempotência. Sem confirmar a dona, não repete. Roda com: node ferramentas/prova-2440-confirmar-outra.mjs
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
registerHooks({
  resolve(s, c, next) {
    if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução padrão */ } }
    return next(s, c)
  },
  load(url, c, next) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return next(url, c)
  },
})
const storage = new Map()
globalThis.sessionStorage = { getItem: k => storage.get(k) ?? null, setItem: (k, v) => storage.set(k, v), removeItem: k => storage.delete(k) }
sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'sintetico', empresa: { id: 'a' }, usuario: { id: 'u' }, expiraEm: Date.now() + 60000 }))
const { enviarTexto, enviarImagem, enviarAudio, enviarImagemCardapio } = await import('../src/infra/api/conversasApi.js')
const { enviarModelo } = await import('../src/infra/api/modelosApi.js')

const MENSAGEM = 'Conversa com Bia. Confirme para assumir.'
let chamadas = []
let perguntas = []
let resposta = true
const json = (status, corpo) => new Response(JSON.stringify(corpo), { status, headers: { 'Content-Type': 'application/json' } })
globalThis.window = { confirm: (m) => { perguntas.push(m); return resposta } }
// A API: sem `confirmar` (corpo ou query) responde 409; com, grava.
globalThis.fetch = async (url, options) => {
  const corpo = typeof options.body === 'string' ? JSON.parse(options.body) : null
  const confirmou = String(url).includes('confirmar=true') || corpo?.confirmar === true
  chamadas.push({ url: String(url), corpo, chave: options.headers?.['Idempotency-Key'], confirmou })
  if (!confirmou) return json(409, { error: { code: 'CONVERSA_DE_OUTRA', message: MENSAGEM } })
  return json(200, { data: { id: 'm1' } })
}
const dataUrl = 'data:image/png;base64,iVBORw0KGgo='
const casos = {
  texto: () => enviarTexto('c1', 'Oi', { chave: 'k-texto' }),
  foto: () => enviarImagem('c1', { dataUrl, nomeArquivo: 'f.png', legenda: 'x' }, { chave: 'k-foto' }),
  audio: () => enviarAudio('c1', { dataUrl: 'data:audio/webm;base64,GkXfow==' }, { chave: 'k-audio' }),
  cardapio: () => enviarImagemCardapio('c1', { cardapioItemId: 'i1', indice: 0 }, { chave: 'k-card' }),
  modelo: () => enviarModelo('c1', { tentativaId: 't1', nome: 'retomar', idioma: 'pt_BR', parametros: ['Ana'] }),
}
const chaves = { texto: 'k-texto', foto: 'k-foto', audio: 'k-audio', cardapio: 'k-card', modelo: undefined }

for (const [nome, enviar] of Object.entries(casos)) {
  chamadas = []; perguntas = []; resposta = true
  assert.deepEqual(await enviar(), { id: 'm1' }, `${nome}: devolve a mensagem`)
  assert.equal(chamadas.length, 2, `${nome}: tenta, pergunta e repete`)
  assert.deepEqual(perguntas, [MENSAGEM], `${nome}: pergunta com a mensagem da API`)
  assert.equal(chamadas[0].confirmou, false, `${nome}: a primeira tentativa vai sem confirmar`)
  assert.equal(chamadas[1].confirmou, true, `${nome}: a repetição leva confirmar`)
  assert.equal(chamadas[0].chave, chaves[nome], `${nome}: chave na primeira`)
  assert.equal(chamadas[1].chave, chaves[nome], `${nome}: a MESMA chave na repetição`)
  if (nome === 'modelo') assert.equal(chamadas[1].corpo.tentativaId, 't1', 'modelo: mesma tentativa')

  chamadas = []; perguntas = []; resposta = false
  await assert.rejects(enviar(), (e) => e.codigo === 'CONVERSA_DE_OUTRA', `${nome}: recusar a pergunta mantém o erro`)
  assert.equal(chamadas.length, 1, `${nome}: sem confirmação da dona não repete`)
}

// Conversa livre ou própria: a API aceita de primeira e ninguém pergunta.
chamadas = []; perguntas = []
globalThis.fetch = async (url) => { chamadas.push({ url: String(url) }); return json(200, { data: { id: 'm2' } }) }
for (const enviar of Object.values(casos)) await enviar()
assert.equal(chamadas.length, Object.keys(casos).length)
assert.deepEqual(perguntas, [])
console.log('prova-2440-confirmar-outra: ok')
