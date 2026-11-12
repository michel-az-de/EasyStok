// Prova da #2392: borda HTTP do console e renovação da sessão em 401 de leitura.
//
// 1. nginx.conf: headers de segurança (always) em cada location que serve HTML/estático.
// 2. nginx.conf: client_max_body_size cobre o acervo (6 MB) e o SSE repassa X-Forwarded-*.
// 3. GET com 401 renova a sessão e repete uma vez; POST com 401 não repete.
//
// Roda com: node ferramentas/prova-2392-borda-http.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) {
    falhas.push(descricao)
    console.log('FALHA ' + descricao + '\n      ' + String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' | '))
  }
}

// ---- nginx.conf ----
const conf = readFileSync(new URL('../nginx.conf', import.meta.url), 'utf8')
const bloco = (cabecalho) => {
  const ini = conf.indexOf(cabecalho)
  if (ini < 0) throw new Error(`location ausente: ${cabecalho}`)
  const fim = conf.indexOf('\n    }', ini)
  return conf.slice(ini, fim)
}
const SEGURANCA = [
  /add_header Content-Security-Policy-Report-Only "[^"]*frame-ancestors 'none'[^"]*" always;/,
  /add_header X-Content-Type-Options "nosniff" always;/,
  /add_header Referrer-Policy "strict-origin-when-cross-origin" always;/,
  /add_header X-Frame-Options "DENY" always;/,
]
for (const loc of ['location ^~ /assets/', 'location ~ \\.[A-Za-z0-9]+$', 'location / {']) {
  await confere(`${loc}: headers de segurança com always`, () => {
    const b = bloco(loc)
    for (const re of SEGURANCA) assert.match(b, re)
  })
}
await confere('CSP libera os SDKs do Google e do Facebook e a mesma origem', () => {
  assert.match(conf, /script-src 'self' https:\/\/accounts\.google\.com https:\/\/connect\.facebook\.net/)
  assert.match(conf, /connect-src 'self'/)
  assert.match(conf, /img-src 'self' data: blob:/)
})
await confere('HSTS fica com o Caddy, não no nginx', () => assert.doesNotMatch(conf, /Strict-Transport-Security/))
await confere('client_max_body_size cobre o acervo de 6 MB', () => {
  const m = conf.match(/client_max_body_size\s+(\d+)m;/)
  assert.ok(m, 'client_max_body_size ausente')
  assert.ok(Number(m[1]) >= 8)
})
await confere('SSE repassa X-Forwarded-For e X-Forwarded-Proto', () => {
  const b = bloco('location ^~ /api/operacao/eventos')
  assert.match(b, /proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;/)
  assert.match(b, /proxy_set_header X-Forwarded-Proto \$scheme;/)
})

// ---- cliente.js: 401 em leitura ----
const jwt = (exp) => `a.${Buffer.from(JSON.stringify({ exp, empresaId: 'emp', sessaoPersistente: 'true' })).toString('base64url')}.c`
const guardada = new Map()
globalThis.localStorage = { getItem: (k) => guardada.get(k) ?? null, setItem: (k, v) => guardada.set(k, v), removeItem: (k) => guardada.delete(k) }
globalThis.sessionStorage = { getItem: () => null, setItem() {}, removeItem() {} }
globalThis.window = { dispatchEvent() {} }
Object.defineProperty(globalThis, 'navigator', { value: { locks: { request: (_n, fn) => fn() } }, configurable: true })

const { chamarApi } = await import('../src/infra/api/cliente.js')
const { gravarSessao } = await import('../src/infra/api/sessao.js')
const semear = () => {
  const velho = jwt(Math.floor(Date.now() / 1000) + 3600)
  gravarSessao({ token: velho, refreshToken: 'r1', persistente: true, venceEm: Date.now() + 3600000, expiraEm: Date.now() + 3540000, empresa: { id: 'emp' } })
  return velho
}
const resp = (status, corpo = {}) => new Response(status === 401 ? '' : JSON.stringify(corpo), { status })

await confere('GET com 401 renova a sessão e repete a leitura uma vez', async () => {
  const velho = semear()
  const novo = jwt(Math.floor(Date.now() / 1000) + 7200)
  const chamadas = []
  globalThis.fetch = async (url, o) => {
    chamadas.push({ url, auth: o.headers.Authorization })
    if (url.includes('/api/auth/refresh')) return resp(200, { data: { accessToken: novo, refreshToken: 'r2', expiresIn: 7200 } })
    return o.headers.Authorization === `Bearer ${velho}` ? resp(401) : resp(200, { data: { ok: true } })
  }
  const dados = await chamarApi('/api/pedidos')
  assert.deepEqual(dados, { ok: true })
  assert.equal(chamadas.filter((c) => c.url.includes('/api/auth/refresh')).length, 1)
  assert.equal(chamadas.filter((c) => c.url === '/api/pedidos').length, 2)
})

await confere('GET que continua em 401 depois de renovar não repete de novo', async () => {
  semear()
  const novo = jwt(Math.floor(Date.now() / 1000) + 7200)
  let leituras = 0
  globalThis.fetch = async (url) => {
    if (url.includes('/api/auth/refresh')) return resp(200, { data: { accessToken: novo, refreshToken: 'r2', expiresIn: 7200 } })
    leituras += 1
    return resp(401)
  }
  await assert.rejects(chamarApi('/api/pedidos'), (e) => e.status === 401)
  assert.equal(leituras, 2)
})

await confere('POST com 401 não renova nem repete', async () => {
  semear()
  const chamadas = []
  globalThis.fetch = async (url, o) => { chamadas.push(`${o.method} ${url}`); return resp(401) }
  await assert.rejects(chamarApi('/api/pedidos', { metodo: 'POST', corpo: { a: 1 } }), (e) => e.status === 401)
  assert.deepEqual(chamadas, ['POST /api/pedidos'])
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
