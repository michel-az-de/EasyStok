// #2792 (revisão): CampoComLimite do acervo, em Chromium. O valor preenchido por código (o "Atende uma
// solicitação" copia o brief para o prompt e dispara `input` no formulário) também mostra o aviso de limite,
// e a contagem visível não é região viva: o anúncio estável "Passou do limite" fica numa região própria.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2792/campo-com-limite')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {CampoComLimite} from '../../../src/features/gestao/acervo/CampoComLimite';
import '../../../src/estilos/base.css';
function Cena(){
  const preencher=(e)=>{const f=e.currentTarget.form;f.elements.prompt.value='abcdef';f.dispatchEvent(new Event('input',{bubbles:true}))};
  return <form><CampoComLimite rotulo="Prompt" limite={5} name="prompt"/><CampoComLimite rotulo="Título" como="input" limite={5} name="titulo" defaultValue="abcdefg"/><button type="button" onClick={preencher}>Preencher</button></form>
}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const pagina = await (await browser.newContext({ viewport: { width: 900, height: 700 }, locale: 'pt-BR', reducedMotion: 'reduce' })).newPage()
pagina.setDefaultTimeout(8000)
const erros = []
pagina.on('pageerror', e => erros.push(e.message))
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2792/campo-com-limite/index.html`
try {
  await pagina.goto(base)
  // Valor inicial acima do limite já avisa ao abrir.
  const titulo = pagina.getByRole('textbox', { name: 'Título' })
  const contagemTitulo = pagina.getByText('Use até 5 caracteres. Agora tem 7.', { exact: true })
  await contagemTitulo.waitFor()
  assert.equal(await titulo.getAttribute('aria-invalid'), 'true')
  assert.equal(await contagemTitulo.getAttribute('role'), null, 'a contagem visível não reanuncia a cada tecla')
  assert.equal(await pagina.getByRole('status').filter({ hasText: 'Passou do limite de 5 caracteres.' }).count(), 1)

  // Preenchido por código: o aviso aparece igual ao digitado.
  const prompt = pagina.getByRole('textbox', { name: 'Prompt' })
  assert.equal(await prompt.getAttribute('aria-invalid'), null)
  // A região viva do Prompt já existe, vazia, antes do estouro (senão o leitor de tela não anuncia a mudança).
  const regiaoPrompt = prompt.locator('xpath=ancestor::div[1]').locator('[role="status"]')
  assert.equal(await regiaoPrompt.count(), 1)
  assert.equal(await regiaoPrompt.textContent(), '')
  await regiaoPrompt.evaluate(e => { e.dataset.prova = 'anuncio-prompt' })
  await pagina.getByRole('button', { name: 'Preencher', exact: true }).click()
  await pagina.getByText('Use até 5 caracteres. Agora tem 6.', { exact: true }).waitFor()
  assert.equal(await prompt.getAttribute('aria-invalid'), 'true', 'o valor preenchido por código marca o campo')
  assert.equal(await pagina.getByRole('status').filter({ hasText: 'Passou do limite de 5 caracteres.' }).count(), 2)
  assert.equal(await pagina.locator('[data-prova="anuncio-prompt"]').textContent(), 'Passou do limite de 5 caracteres.', 'o mesmo nó anuncia')

  // Digitar de volta ao limite limpa o aviso.
  await prompt.fill('abc')
  assert.equal(await prompt.getAttribute('aria-invalid'), null)
  assert.equal(await pagina.getByText('Use até 5 caracteres. Agora tem 6.', { exact: true }).count(), 0)
  assert.deepEqual(erros, [])
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); await browser.close(); throw erro }

// AbaAcervo real: a versão do Canva abre com "<título> · Canva"; acima de 160, o Guardar já abre travado.
writeFileSync(resolve(pasta, 'acervo.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./acervo.jsx"></script></html>')
writeFileSync(resolve(pasta, 'acervo.jsx'), `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {AbaAcervo} from '../../../src/features/gestao/acervo/AbaAcervo';
import '../../../src/estilos/base.css';
createRoot(document.getElementById('root')).render(<main style={{height:'100%',overflowY:'auto',padding:24}}><AbaAcervo/></main>);
`)
const contextoAcervo = await browser.newContext({ viewport: { width: 1100, height: 800 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contextoAcervo.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-local', expiraEm: Date.now() + 3600000, empresa: { id: 'prova' }, usuario: { id: 'prova' } })))
const tituloLongo = 'Bolo de aniversário ' + 'x'.repeat(135) // 155 + " · Canva" = 163
await contextoAcervo.route(url => url.pathname.startsWith('/api/'), route => {
  const path = new URL(route.request().url()).pathname
  if (path.endsWith('/miniatura')) return route.fulfill({ contentType: 'image/png', body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=', 'base64') })
  const data = path === '/api/acervo/imagens' ? [{ id: 'L1', titulo: tituloLongo, origem: 'Fotografia', formato: '4:5', itensJson: '[]', sha256: 'fixture-local', canvaUrl: 'https://www.canva.com/design/prova' }] : []
  return route.fulfill({ json: { data } })
})
const acervo = await contextoAcervo.newPage()
acervo.setDefaultTimeout(10000)
acervo.on('pageerror', e => erros.push(e.message))
const travado = (botao) => botao.evaluate(b => b.disabled || b.getAttribute('aria-disabled') === 'true')
try {
  await acervo.goto(base.replace('index.html', 'acervo.html'))
  await acervo.locator('button[aria-pressed]').first().click()
  await acervo.getByRole('region', { name: 'Ficha da imagem' }).waitFor()
  await acervo.getByRole('button', { name: 'Adicionar versão do Canva', exact: true }).click()
  const dialogo = acervo.getByRole('dialog')
  await dialogo.getByText('Use até 160 caracteres. Agora tem 163.', { exact: true }).waitFor()
  const guardar = dialogo.getByRole('button', { name: 'Guardar versão', exact: true })
  await acervo.waitForFunction(() => { const b = [...document.querySelectorAll('dialog button')].find(x => x.textContent.includes('Guardar versão')); return b && (b.disabled || b.getAttribute('aria-disabled') === 'true') }, undefined, { timeout: 4000 })
  assert.equal(await travado(guardar), true, 'título herdado acima do limite trava o Guardar ao abrir')
  // Encurtar libera.
  await dialogo.getByRole('textbox', { name: 'Título' }).fill('Bolo de aniversário · Canva')
  await acervo.waitForFunction(() => { const b = [...document.querySelectorAll('dialog button')].find(x => x.textContent.includes('Guardar versão')); return b && !(b.disabled || b.getAttribute('aria-disabled') === 'true') }, undefined, { timeout: 4000 })
  assert.deepEqual(erros, [])
  console.log('#2792 CampoComLimite: aviso no valor inicial e no preenchido por código, anúncio estável; Acervo trava o Guardar com título herdado longo.')
} catch (erro) { await acervo.screenshot({ path: resolve(pasta, 'falha-acervo.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
