// #3303 (pendência da #3299): o hook dos números de Produtos nunca mostra a empresa anterior. Troca com a resposta
// antiga em voo, e volta para uma empresa cuja leitura falha. Chromium, hook real e HTTP controlado.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const raiz = resolve(import.meta.dirname, '..'), url = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const { chromium } = createRequire(resolve(raiz, 'package.json'))(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(raiz, '.build/3303/troca'); mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React, { useState } from 'react'; import { createRoot } from 'react-dom/client'
import { useDesempenhoDosProdutos } from '../../../src/aplicacao/cadastros'
const sessao = (emp) => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture', empresa: { id: emp }, usuario: { id: 'op' }, expiraEm: Date.now() + 600000 }))
function Cena() {
  const [emp, trocar] = useState('A')
  const d = useDesempenhoDosProdutos({ empresaId: emp, identidade: emp + ':op' }, 0)
  const ir = (nova) => { sessao(nova); trocar(nova) }
  return <><button onClick={() => ir(emp === 'A' ? 'B' : 'A')}>Trocar</button><p id="saida">{emp}={d.desempenho?.resumo?.receita ?? 'nenhum'}{d.erro ? ' erro' : ''}</p></>
}
sessao('A'); createRoot(document.getElementById('root')).render(<Cena/>)
`)
const browser = await chromium.launch({ headless: true })
const ctx = await browser.newContext()
let chamadasA = 0
await ctx.route('**/api/produtos/desempenho**', async r => {
  const emp = new URL(r.request().url()).searchParams.get('empresaId')
  const corpo = (receita) => ({ json: { data: { janelaDias: 90, hoje: '2026-11-25', resumo: { receita }, produtos: [] } } })
  if (emp === 'B') return r.fulfill(corpo(222))
  chamadasA += 1
  if (chamadasA === 1) { await new Promise(ok => setTimeout(ok, 1500)); return r.fulfill(corpo(111)).catch(() => {}) }
  return r.fulfill({ status: 500, json: { error: { code: 'X', message: 'falhou' } } })
})
const pg = await ctx.newPage(); const erros = []; pg.on('pageerror', e => erros.push(e.message))
await pg.goto(`${url}/.build/3303/troca/index.html`)
await pg.evaluate(() => { window.vistos = []; const o = new MutationObserver(() => window.vistos.push(document.getElementById('saida')?.textContent)); o.observe(document.body, { subtree: true, childList: true, characterData: true }) })
// 1) A ainda em voo, troca para B: B aparece e o 111 de A nunca aparece, nem quando A responderia.
await pg.getByRole('button', { name: 'Trocar' }).click()
await pg.getByText('B=222').waitFor()
await pg.waitForTimeout(2000)
assert.equal(await pg.locator('#saida').textContent(), 'B=222')
// 2) Volta para A, que falha: não mostra o 222 de B.
await pg.getByRole('button', { name: 'Trocar' }).click()
await pg.getByText('A=nenhum erro').waitFor()
const vistos = await pg.evaluate(() => window.vistos)
assert.ok(!vistos.some(t => t?.includes('111')), 'nunca mostrou o número de A depois da troca')
assert.ok(!vistos.some(t => t?.startsWith('A=222')), 'A nunca mostrou o número de B')
assert.deepEqual(erros, [])
writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ ok: true, vistos }, null, 2))
await browser.close(); console.log('prova 3303 troca ok:', pasta)
