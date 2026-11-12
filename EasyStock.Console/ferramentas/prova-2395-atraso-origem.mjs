// ComandaKds real com transporte controlado: autorização, erro, preservação e duplo gesto.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2395/atraso-origem')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><div id="root"></div><script type="module" src="./browser.jsx"></script></html>')
writeFileSync(resolve(pasta, 'browser.jsx'), `import React,{useState}from'react';import{createRoot}from'react-dom/client';import{ComandaKds}from'../../../src/features/cozinha/ComandaKds';import{ContextoAcessoModulos}from'../../../src/aplicacao/acessoModulos';import'../../../src/estilos/base.css';function Prova(){const[pode,setPode]=useState(false);return <><button onClick={()=>setPode(true)}>Usar Dona</button><ContextoAcessoModulos.Provider value={{acoes:{gerenciarOcorrencias:pode}}}><ComandaKds pedido={{id:'pedido-exclusivo',numeroComanda:7,status:'aguardando',itens:[]}} agora={Date.now()} linhas={[]} aoFechar={()=>{}} aoAvancar={()=>{}} aoReimprimir={()=>{}}/></ContextoAcessoModulos.Provider></>}createRoot(document.getElementById('root')).render(<Prova/>);`)
const browser = await chromium.launch({ headless: true }), context = await browser.newContext(), page = await context.newPage()
const posts = [], erros = []; let liberar
page.on('pageerror', e => erros.push(e.message))
await context.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture', empresa: { id: 'empresa' }, usuario: { id: 'dona' }, expiraEm: Date.now() + 600000 })))
await page.route('**/api/ocorrencias', async r => {
  posts.push({ corpo: r.request().postDataJSON(), chave: r.request().headers()['idempotency-key'] })
  if (posts.length === 1) return r.fulfill({ status: 400, json: { error: { code: 'VALIDATION_ERROR', message: 'Confira o relato da cozinha.' } } })
  await new Promise(resolve => { liberar = resolve })
  await r.fulfill({ json: { data: { id: 'ocorrencia-confirmada', conversaId: posts.length === 2 ? 'conversa-vinculada' : null, categoria: 'atraso', relato: posts.at(-1).corpo.relato } } })
})
try {
  await page.goto(`${process.env.CONSOLE_TEST_URL ?? 'http://127.0.0.1:5209'}/.build/2395/atraso-origem/index.html`)
  await page.getByRole('dialog').waitFor()
  assert.equal(await page.getByRole('button', { name: 'Registrar atraso', exact: true }).count(), 0)
  // A troca representa outra capability do contexto, sem interferir no modal real.
  await page.evaluate(() => document.querySelector('button').click())
  await page.getByRole('button', { name: 'Registrar atraso', exact: true }).click()
  const campo = page.getByLabel('O que atrasou?', { exact: true }), texto = 'Forno indisponível. Atendimento deve conferir novo horário.'
  await campo.fill(texto)
  await page.getByRole('button', { name: 'Salvar ocorrência', exact: true }).click()
  await page.getByRole('alert').filter({ hasText: 'Confira o relato da cozinha.' }).waitFor()
  assert.equal(await campo.inputValue(), texto)
  await page.getByRole('button', { name: 'Salvar ocorrência', exact: true }).dblclick()
  await page.getByRole('button', { name: 'Registrando…', exact: true }).waitFor()
  assert.equal(posts.length, 2); assert.equal(await campo.isDisabled(), true)
  liberar()
  await page.getByRole('status').filter({ hasText: 'Atraso registrado. Atendimento avisado na conversa vinculada.' }).waitFor()
  assert.deepEqual(posts.map(p => p.corpo), [{ pedidoId: 'pedido-exclusivo', conversaId: null, categoria: 'atraso', relato: texto }, { pedidoId: 'pedido-exclusivo', conversaId: null, categoria: 'atraso', relato: texto }])
  assert.match(posts[0].chave, /^[0-9a-f-]{36}$/i); assert.equal(posts[0].chave, posts[1].chave, 'retry preserva a intenção')
  await page.reload(); await page.getByRole('dialog').waitFor()
  await page.evaluate(() => document.querySelector('button').click())
  await page.getByRole('button', { name: 'Registrar atraso', exact: true }).click(); await campo.fill('Pedido sem conversa vinculada.')
  await page.getByRole('button', { name: 'Salvar ocorrência', exact: true }).click(); await page.getByRole('button', { name: 'Registrando…', exact: true }).waitFor(); liberar()
  await page.getByRole('status').filter({ hasText: 'Atraso registrado no histórico do pedido.' }).waitFor()
  assert.equal(await page.getByText('Atraso registrado. Atendimento avisado na conversa vinculada.', { exact: true }).count(), 0)
  assert.deepEqual(erros, [])
  console.log('Cozinha: capability, relato preservado após erro, um POST em voo e confirmação só após API verificados.')
} finally {
  liberar?.(); writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ posts, erros }, null, 2)); await browser.close()
}
