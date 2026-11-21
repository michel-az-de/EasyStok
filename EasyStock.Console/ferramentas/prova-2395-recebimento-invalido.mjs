import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2395/recebimento-invalido')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1"><div id="root"></div><script type="module" src="./browser.jsx"></script></html>')
writeFileSync(resolve(pasta, 'browser.jsx'), `import React from 'react';import{createRoot}from'react-dom/client';import{BaixaAMao}from'../../../src/features/ficha-cliente/BlocoCobranca';import'../../../src/estilos/base.css';window.recebidos=[];createRoot(document.getElementById('root')).render(<BaixaAMao valorCobrado={69} aoCancelar={()=>{}} aoConfirmar={async(valor,metodo)=>{window.recebidos.push({valor,metodo});return{}}}/>);`)
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 390, height: 844 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const page = await context.newPage(); const entradas = [], erros = []
page.on('pageerror', e => erros.push(e.message))
await context.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture', empresa: { id: 'empresa' }, usuario: { id: 'dona' }, expiraEm: Date.now() + 600000 })))
await context.route('**/api/cadastros/metodos-pagamento*', route => route.fulfill({ json: { data: [{ codigo: 'dinheiro', nome: 'Dinheiro', ativo: true, revisao: 0 }] } }))
try {
  await page.goto(`${process.env.CONSOLE_TEST_URL ?? 'http://127.0.0.1:5209'}/.build/2395/recebimento-invalido/index.html`)
  await page.getByLabel('Como recebeu', { exact: true }).selectOption('dinheiro')
  const campo = page.getByRole('textbox', { name: 'Valor recebido', exact: true })
  for (const entrada of ['-10', '', '0', '100000']) {
    await campo.fill(entrada)
    const habilitado = await page.getByRole('button', { name: 'Marcar pago', exact: true }).isEnabled()
    entradas.push({ entrada, campo: await campo.inputValue(), habilitado })
    assert.equal(habilitado, false, `${entrada} não pode confirmar recebimento: ${JSON.stringify(entradas.at(-1))}`)
    assert.equal(await campo.inputValue(), entrada, 'Entrada inválida preservada para correção')
    await campo.press('Tab')
    if (entrada === '-10') assert.equal(await campo.inputValue(), '-10,00')
    await campo.press('Enter'); assert.deepEqual(await page.evaluate(() => window.recebidos), [])
  }
  await campo.fill('69,25')
  await page.getByRole('button', { name: 'Marcar pago', exact: true }).click()
  assert.deepEqual(await page.evaluate(() => window.recebidos), [{ valor: 69.25, metodo: 'dinheiro' }])
  assert.deepEqual(erros, [])
  console.log('Recebimento real390: negativo/vazio/zero/acima limite recusados sem trocar entrada; correção69,25 confirma valor exato.')
} finally {
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ entradas, erros }, null, 2))
  await page.screenshot({ path: resolve(pasta, 'recebimento-390.png') })
  await browser.close()
}
