/* eslint-disable no-console */
// Prova da #2603 (Q16): em produção o app.easystok.online é servido pelo Caddy compartilhado com file_server,
// não pelo nginx deste console. Quem escreve os cabeçalhos do estático é o scripts/deploy/console-cache.sh.
// Roda o Python dele num Caddyfile de cópia, na forma que está no ar desde a #2407 (sem HSTS), e confere:
// o HSTS de 1 ano entra, sem includeSubDomains, e a segunda rodada não muda nada (idempotente).
//
//   node ferramentas/prova-2603-hsts-caddy.mjs

import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { spawnSync } from 'node:child_process'
import assert from 'node:assert/strict'

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const script = readFileSync(new URL('../../scripts/deploy/console-cache.sh', import.meta.url), 'utf8').replace(/\r\n/g, '\n')
const python = script.match(/python3 - <<'PY'\n([\s\S]*?)\nPY\n/)?.[1]

const CSP = "default-src 'self'; script-src 'self' https://accounts.google.com https://connect.facebook.net; style-src 'self' 'unsafe-inline' https://accounts.google.com; img-src 'self' data: blob: https:; font-src 'self' data:; media-src 'self' data: blob:; connect-src 'self' https://accounts.google.com https://connect.facebook.net https://graph.facebook.com https://www.facebook.com; frame-src https://accounts.google.com https://www.google.com https://www.facebook.com https://web.facebook.com; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'"
// Bloco do app. medido no Caddyfile de produção em 14/11/2026 (forma da #2407).
const NO_AR_2407 = [
  'app.easystok.online {',
  '\tencode gzip',
  '\thandle /api/* {',
  '\t\treverse_proxy ez-api:8080',
  '\t}',
  '\thandle {',
  '\t\troot * /srv/docs/easystok-console',
  '\t\theader {',
  `\t\t\tContent-Security-Policy-Report-Only "${CSP}"`,
  '\t\t\tX-Content-Type-Options "nosniff"',
  '\t\t\tReferrer-Policy "strict-origin-when-cross-origin"',
  '\t\t\tX-Frame-Options "DENY"',
  '\t\t}',
  '\t\t@imutavel path /assets/*',
  '\t\theader @imutavel Cache-Control "public, max-age=31536000, immutable"',
  '\t\t@mutavel not path /assets/*',
  '\t\theader @mutavel Cache-Control "no-store, max-age=0"',
  '\t\t@spa {',
  '\t\t\tnot file',
  '\t\t\tnot path_regexp \\.[A-Za-z0-9]+$',
  '\t\t}',
  '\t\trewrite @spa /index.html',
  '\t\tfile_server',
  '\t}',
  '}',
  '',
].join('\n')
const OUTRO_SITE = 'casadababa.com {\n\theader Strict-Transport-Security "max-age=31536000; includeSubDomains; preload"\n}\n'

const interpretador = ['python3', 'python'].find((p) => spawnSync(p, ['--version']).status === 0)
const dir = mkdtempSync(join(tmpdir(), 'prova-2603-caddy-'))
const caddyfile = join(dir, 'Caddyfile')
const rodar = () => {
  const r = spawnSync(interpretador, ['-'], { input: python.replace("'/opt/stacks/shared/Caddyfile'", JSON.stringify(caddyfile)), encoding: 'utf8' })
  assert.equal(r.status, 0, `python saiu com ${r.status}: ${r.stderr || r.stdout}`)
  // No Windows o open('r+') de texto do Python grava CRLF; a VPS é Linux.
  return readFileSync(caddyfile, 'utf8').replace(/\r\n/g, '\n')
}

try {
  caso('o console-cache.sh tem o Python e há interpretador', () => {
    assert.ok(python, 'bloco PY não encontrado')
    assert.ok(interpretador, 'sem python3/python')
  })

  writeFileSync(caddyfile, OUTRO_SITE + '\n' + NO_AR_2407)
  let depois = ''
  caso('migra a forma da #2407 sem erro', () => { depois = rodar() })
  const app = depois.slice(depois.indexOf('app.easystok.online {'))

  caso('o estático do app. passa a mandar HSTS de 1 ano', () => {
    assert.match(app, /\n\t\t\tStrict-Transport-Security "max-age=31536000"\n/)
  })
  caso('o HSTS do app. não leva includeSubDomains nem preload', () => {
    assert.doesNotMatch(app, /Strict-Transport-Security[^\n]*(includeSubDomains|preload)/i)
  })
  caso('os outros cabeçalhos e o outro site ficam iguais', () => {
    assert.ok(depois.startsWith(OUTRO_SITE))
    assert.equal(app.replace('\t\t\tStrict-Transport-Security "max-age=31536000"\n', ''), NO_AR_2407)
  })
  caso('segunda rodada não muda o arquivo (idempotente)', () => {
    assert.equal(rodar(), depois)
  })
} finally {
  rmSync(dir, { recursive: true, force: true })
}

console.log(`\n${ok}/${ok + falhas.length} casos ok`)
if (falhas.length) process.exit(1)
