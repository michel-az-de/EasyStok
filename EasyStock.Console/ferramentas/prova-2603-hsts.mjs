/* eslint-disable no-console */
// Prova da #2603: Q16 da auditoria QA de 13/11. O app.easystok.online não mandava Strict-Transport-Security.
// O nginx do console manda os cabeçalhos de segurança; add_header dentro de location anula os do server,
// então todo bloco que manda a CSP tem de mandar também o HSTS, sem includeSubDomains (só o app.).
//
//   node ferramentas/prova-2603-hsts.mjs

import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const conf = readFileSync(new URL('../nginx.conf', import.meta.url), 'utf8').replace(/\r\n/g, '\n')

// Blocos com cabeçalho de segurança: o nível do server (antes da primeira location) e cada location.
const partes = conf.split(/\n\s*location\s+/)
const blocos = partes
  .map((texto, i) => ({ nome: i === 0 ? 'server' : `location ${texto.split('{')[0].trim()}`, texto: i === 0 ? texto : texto.split(/\n\s*}\s*\n/)[0] }))
  .filter((b) => b.texto.includes('Content-Security-Policy'))

const HSTS = /add_header Strict-Transport-Security "max-age=31536000" always;/

caso('há blocos com cabeçalho de segurança', () => assert.ok(blocos.length >= 4, `achei ${blocos.length}`))

for (const b of blocos) {
  caso(`${b.nome} manda HSTS de 1 ano`, () => assert.match(b.texto, HSTS))
}

caso('nenhum HSTS com includeSubDomains ou preload', () => {
  assert.doesNotMatch(conf, /Strict-Transport-Security[^\n]*(includeSubDomains|preload)/i)
})

console.log(`\n${ok}/${ok + falhas.length} casos ok`)
if (falhas.length) process.exit(1)
