// Chromium real no Console em demonstração. PLAYWRIGHT_MODULE aponta para o runtime externo.
// Acervo é só API: o harness temporário monta o componente real com respostas HTTP controladas.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_DEMO_URL ?? 'http://127.0.0.1:5199'
const pastaConsole = resolve(import.meta.dirname, '..')
const evidencias = resolve(pastaConsole, '../.build/1653/acessibilidade')
const temporarios = resolve(pastaConsole, '.build/1653')
mkdirSync(evidencias, { recursive: true })
mkdirSync(temporarios, { recursive: true })
for (const nome of ['resultado.json', 'falha.json', 'falha.png']) rmSync(join(evidencias, nome), { force: true })

const resultados = []
const erros = []
const browser = await chromium.launch({ headless: true })
let ultimaPagina
const repousar = page => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
const foto = async (page, nome) => {
  await page.evaluate(() => document.fonts.ready)
  await repousar(page)
  await page.screenshot({ path: join(evidencias, `${nome}.png`), fullPage: true })
}
const registrar = (nome, medidas = {}) => { resultados.push({ nome, ...medidas }); console.log('ok ' + nome) }
const foco = page => page.evaluate(() => ({ id: document.activeElement.id, texto: document.activeElement.textContent, tag: document.activeElement.tagName }))
const esperarFoco = locator => locator.evaluate(el => document.activeElement === el)
async function menuInteiroVisivel(menu) {
  const medidas = await menu.evaluate(el => {
    const r = el.getBoundingClientRect()
    return { x: r.x, y: r.y, right: r.right, bottom: r.bottom, largura: innerWidth, altura: innerHeight }
  })
  assert.ok(medidas.x >= 0 && medidas.y >= 0 && medidas.right <= medidas.largura && medidas.bottom <= medidas.altura, 'o menu inteiro deve caber na viewport: ' + JSON.stringify(medidas))
  return medidas
}
async function novaPagina(opcoes = {}) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce', hasTouch: true, ...opcoes })
  const page = await context.newPage()
  page.on('pageerror', erro => erros.push(erro.message))
  ultimaPagina = page
  return { context, page }
}
async function opcaoVisivel(page, campo) {
  const id = await campo.getAttribute('aria-activedescendant')
  assert.ok(id, 'o campo deve anunciar a opção ativa')
  const medida = await page.evaluate(id => {
    const opcao = document.getElementById(id)
    if (!opcao) return null
    const caixa = opcao.closest('[role="group"]')
    const item = opcao.getBoundingClientRect(), janela = caixa.getBoundingClientRect()
    return { id, selecionada: opcao.getAttribute('aria-selected'), top: item.top, bottom: item.bottom, limiteTop: janela.top, limiteBottom: janela.bottom, scrollTop: caixa.scrollTop }
  }, id)
  assert.ok(medida, 'aria-activedescendant precisa referenciar um elemento existente')
  assert.equal(medida.selecionada, 'true')
  assert.ok(medida.top >= medida.limiteTop - 1 && medida.bottom <= medida.limiteBottom + 1, JSON.stringify(medida))
  assert.equal(await esperarFoco(campo), true, 'o foco deve permanecer no campo')
  return medida
}

try {
  const { context, page } = await novaPagina()
  await context.route(url => url.pathname.startsWith('/api/'), route => route.fulfill({ json: { data: [] } }))
  await page.goto(origem + '/#/m/atendimento')
  // #2135: a tela abre sem conversa escolhida; a atendente abre a José Moretti, que está no automático.
  await page.locator('button[aria-current]', { hasText: 'José Moretti' }).click()
  await page.getByRole('button', { name: 'Assumir', exact: true }).click()
  const campo = page.getByRole('textbox', { name: 'Mensagem para o cliente', exact: true })
  await campo.fill('/')
  const lista = page.getByRole('listbox', { name: 'Respostas prontas' })
  await lista.waitFor()
  assert.equal(await lista.getAttribute('id'), await campo.getAttribute('aria-controls'))
  assert.equal(await campo.evaluate(el => el.tagName), 'TEXTAREA')
  const quantidade = await lista.getByRole('option').count()
  assert.equal(quantidade, 8, 'a demonstração precisa oferecer uma lista maior que a área visível')
  const percurso = [await opcaoVisivel(page, campo)]
  for (let i = 1; i < quantidade; i++) {
    await campo.press('ArrowDown')
    await repousar(page)
    percurso.push(await opcaoVisivel(page, campo))
  }
  assert.ok(percurso.at(-1).scrollTop > percurso[0].scrollTop, 'percorrer a lista deve revelar o último item')
  await foto(page, '01-respostas-ultima-opcao')
  await campo.press('Escape')
  await lista.waitFor({ state: 'detached' })
  assert.equal(await esperarFoco(campo), true)
  registrar('App: oito respostas pelo atalho, opção ativa visível e Escape conserva foco', { quantidade, scrollInicial: percurso[0].scrollTop, scrollFinal: percurso.at(-1).scrollTop })

  await campo.fill('Rascunho antes.')
  const respostas = page.getByRole('button', { name: 'Respostas', exact: true })
  await respostas.click()
  const busca = page.getByRole('combobox', { name: 'Buscar resposta pronta' })
  await busca.waitFor()
  assert.equal(await esperarFoco(busca), true)
  await busca.press('ArrowDown')
  await opcaoVisivel(page, busca)
  await busca.press('Escape')
  await lista.waitFor({ state: 'detached' })
  assert.equal(await esperarFoco(respostas), true)
  assert.equal(await campo.inputValue(), 'Rascunho antes.')
  await respostas.click()
  await busca.press('Enter')
  await lista.waitFor({ state: 'detached' })
  assert.match(await campo.inputValue(), /^Rascunho antes\. Obrigada pela preferência!/)
  assert.equal(await esperarFoco(campo), true)
  registrar('App: botão Respostas abre busca acessível; Escape volta ao botão e Enter insere sem enviar')

  await campo.fill('Mensagem de composição 1653')
  await campo.dispatchEvent('keydown', { key: 'Enter', code: 'Enter', isComposing: true, keyCode: 229, bubbles: true, cancelable: true })
  await repousar(page)
  assert.equal(await campo.inputValue(), 'Mensagem de composição 1653')
  assert.equal(await page.getByRole('log', { name: 'Mensagens da conversa' }).getByText('Mensagem de composição 1653', { exact: true }).count(), 0, 'a composição não pode gerar mensagem no fio')
  await campo.fill('/')
  await lista.waitFor()
  const ativaAntes = await campo.getAttribute('aria-activedescendant')
  for (const key of ['ArrowDown', 'Enter', 'Escape']) {
    await campo.dispatchEvent('keydown', { key, isComposing: true, keyCode: 229, bubbles: true, cancelable: true })
    await repousar(page)
    assert.equal(await campo.inputValue(), '/')
    assert.equal(await campo.getAttribute('aria-activedescendant'), ativaAntes)
    assert.equal(await lista.isVisible(), true)
  }
  await foto(page, '02-ime-preserva-rascunho')
  await campo.press('Escape')
  registrar('App: eventos IME de Enter/setas/Escape não enviam, inserem nem fecham sugestões')

  const gatilho = page.getByRole('button', { name: 'Mais ações do pedido', exact: true })
  await gatilho.click()
  const menu = page.getByRole('menu', { name: 'Mais ações do pedido', exact: true })
  await menu.waitFor()
  await page.keyboard.press('ArrowDown')
  await menuInteiroVisivel(menu)
  const focoAntes = await foco(page)
  const posicao = async () => ({
    ancora: await gatilho.evaluate(el => { const r = el.parentElement.getBoundingClientRect(); return { x: r.x, y: r.y, width: r.width } }),
    portal: await menu.evaluate(el => { const r = el.parentElement.getBoundingClientRect(); return { x: r.x, y: r.y, width: r.width } }),
  })
  const antes = await posicao()
  await page.setViewportSize({ width: 1500, height: 900 })
  await repousar(page)
  const redimensionado = await posicao()
  assert.ok(Math.abs(redimensionado.ancora.x - redimensionado.portal.x) < 1)
  assert.ok(Math.abs(redimensionado.ancora.y - redimensionado.portal.y) < 1)
  assert.deepEqual(await foco(page), focoAntes)
  const menuVisivel = await menuInteiroVisivel(menu)
  await foto(page, '03-popover-reposicionado')
  await page.keyboard.press('Escape')
  assert.equal(await esperarFoco(gatilho), true)
  registrar('App: Popover do rodapé abre inteiro na viewport e acompanha resize sem trocar foco', { antes, redimensionado, menuVisivel })

  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('tab', { name: 'Ficha', exact: true }).click()
  const blocos = await page.locator('h3 > button[aria-controls]').evaluateAll(ns => ns.filter(n => n.getClientRects().length).map(n => ({ nome: n.textContent, altura: n.getBoundingClientRect().height })))
  assert.ok(blocos.length >= 4)
  assert.equal(await page.evaluate(() => matchMedia('(pointer: coarse)').matches), true)
  assert.ok(blocos.every(bloco => bloco.altura >= 44), JSON.stringify(blocos))
  await foto(page, '04-ficha-toque')
  registrar('App: cabeçalhos da Ficha têm pelo menos 44 px no celular com toque', { blocos })
  await context.close()

  const htmlHarness = arquivo => `<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head><body><div id="root"></div><script type="module" src="/.build/1653/${arquivo}.jsx"></script></body></html>`
  writeFileSync(join(temporarios, 'popover.html'), htmlHarness('popover'))
  writeFileSync(join(temporarios, 'popover.jsx'), `import React, { useState } from 'react'; import { createRoot } from 'react-dom/client'; import { Popover } from '/src/componentes/Popover.jsx'; import '/src/estilos/base.css'; function Prova() { const [aberto, setAberto] = useState(false); return <main><h1>Popover real em ancestral rolável</h1><div data-area-rolagem style={{ height: 340, overflowY: 'auto', margin: 40 }}><div style={{ height: 1200, paddingBlock: 100 }}><span style={{ position: 'relative', display: 'inline-block', marginLeft: '50%' }}><button onClick={() => setAberto(!aberto)}>Abrir menu de prova</button>{aberto && <Popover portal posicao="abaixo" rotulo="Menu de prova" aoFechar={() => setAberto(false)}>{['Primeira opção', 'Segunda opção', 'Terceira opção'].map(texto => <button key={texto} role="menuitem" style={{ display: 'block', minHeight: 44, width: '100%' }}>{texto}</button>)}</Popover>}</span></div></div></main> } createRoot(document.getElementById('root')).render(<Prova />);`)
  const { context: popoverContext, page: popoverPage } = await novaPagina()
  await popoverPage.goto(origem + '/.build/1653/popover.html')
  const acionar = popoverPage.getByRole('button', { name: 'Abrir menu de prova' })
  const menuDeProva = popoverPage.getByRole('menu', { name: 'Menu de prova' })
  await acionar.click()
  await menuDeProva.waitFor()
  await popoverPage.keyboard.press('ArrowDown')
  const itemFocado = await foco(popoverPage)
  assert.equal(itemFocado.texto, 'Segunda opção')
  await popoverPage.evaluate(() => visualViewport?.dispatchEvent(new Event('scroll')))
  await repousar(popoverPage)
  const medirPopover = async () => ({
    ancora: await acionar.evaluate(el => { const r = el.parentElement.getBoundingClientRect(); return { x: r.x, y: r.y } }),
    portal: await menuDeProva.evaluate(el => { const r = el.parentElement.getBoundingClientRect(); return { x: r.x, y: r.y } }),
  })
  const inicial = await medirPopover()
  await popoverPage.locator('[data-area-rolagem]').evaluate(el => { el.scrollTop = 60 })
  await repousar(popoverPage)
  const rolado = await medirPopover()
  assert.ok(Math.abs(inicial.ancora.y - rolado.ancora.y - 60) < 1)
  assert.ok(Math.abs(rolado.ancora.y - rolado.portal.y) < 1, JSON.stringify(rolado))
  assert.deepEqual(await foco(popoverPage), itemFocado)
  await menuInteiroVisivel(menuDeProva)
  await popoverPage.setViewportSize({ width: 1200, height: 850 })
  await repousar(popoverPage)
  const menor = await medirPopover()
  assert.ok(Math.abs(menor.ancora.x - rolado.ancora.x) > 50)
  assert.ok(Math.abs(menor.ancora.x - menor.portal.x) < 1)
  assert.ok(Math.abs(menor.ancora.y - menor.portal.y) < 1)
  assert.deepEqual(await foco(popoverPage), itemFocado)
  await foto(popoverPage, '05-harness-popover-scroll')
  await popoverPage.setViewportSize({ width: 600, height: 800 })
  await repousar(popoverPage)
  const folha = await menuInteiroVisivel(menuDeProva)
  assert.equal(await menuDeProva.evaluate(el => getComputedStyle(el).position), 'fixed')
  assert.deepEqual(await foco(popoverPage), itemFocado)
  await popoverPage.keyboard.press('Escape')
  assert.equal(await esperarFoco(acionar), true)
  registrar('Harness de componente real: Popover acompanha 60 px de scroll, resize e folha móvel sem roubar foco', { inicial, rolado, menor, folha })
  await popoverContext.close()

  writeFileSync(join(temporarios, 'acervo.html'), htmlHarness('acervo'))
  writeFileSync(join(temporarios, 'acervo.jsx'), `import React from 'react'; import { createRoot } from 'react-dom/client'; import { AbaAcervo } from '/src/features/gestao/acervo/AbaAcervo.jsx'; import '/src/estilos/base.css'; createRoot(document.getElementById('root')).render(<main style={{ height: '100%', overflowY: 'auto', padding: 24 }}><AbaAcervo /></main>);`)
  for (const reducedMotion of ['reduce', 'no-preference']) {
    const { context: acervoContext, page: acervoPage } = await novaPagina({ reducedMotion, viewport: { width: 1100, height: 650 } })
    await acervoContext.addInitScript(() => {
      sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-local', expiraEm: Date.now() + 3600000, empresa: { id: 'prova' }, usuario: { id: 'prova' } }))
      window.__rolagensAcervo = []
      const original = Element.prototype.scrollIntoView
      Element.prototype.scrollIntoView = function (opcoes) { window.__rolagensAcervo.push({ rotulo: this.getAttribute('aria-label'), ...opcoes }); return original.call(this, opcoes) }
    })
    await acervoContext.route(url => url.pathname.startsWith('/api/'), route => {
      assert.equal(route.request().method(), 'GET')
      const path = new URL(route.request().url()).pathname
      if (path.endsWith('/miniatura')) return route.fulfill({ contentType: 'image/png', body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=', 'base64') })
      const data = path === '/api/acervo/imagens' ? Array.from({ length: 12 }, (_, i) => ({ id: `prova-${i}`, titulo: `Imagem de prova ${String(i + 1).padStart(2, '0')}`, origem: 'Fotografia', formato: '4:5', itensJson: '[]', sha256: 'fixture-local' })) : []
      return route.fulfill({ json: { data } })
    })
    await acervoPage.goto(origem + '/.build/1653/acervo.html')
    await acervoPage.getByRole('button', { name: /Imagem de prova 01/ }).click()
    await acervoPage.getByRole('region', { name: 'Ficha da imagem' }).waitFor()
    const medidas = await acervoPage.evaluate(() => ({ rolagens: window.__rolagensAcervo, foco: document.activeElement.getAttribute('aria-label'), prefere: matchMedia('(prefers-reduced-motion: reduce)').matches }))
    const rolagem = medidas.rolagens.find(item => item.rotulo === 'Ficha da imagem')
    assert.ok(rolagem)
    assert.equal(rolagem.behavior, reducedMotion === 'reduce' ? 'auto' : 'smooth')
    assert.equal(medidas.foco, 'Ficha da imagem')
    await foto(acervoPage, `06-acervo-${reducedMotion}`)
    registrar(`Harness de componente real: Acervo respeita ${reducedMotion}`, medidas)
    await acervoContext.close()
  }
  assert.deepEqual(erros, [], 'nenhum erro JavaScript deve surgir durante os fluxos')
  writeFileSync(join(evidencias, 'resultado.json'), JSON.stringify({ origem, resultados, erros, leitoresDeTela: 'Não executados; inspeção de atributos e foco no Chromium.' }, null, 2))
  console.log(JSON.stringify({ aprovados: resultados.length, erros: erros.length, evidencias }))
} catch (erro) {
  if (ultimaPagina && !ultimaPagina.isClosed()) await foto(ultimaPagina, 'falha').catch(() => {})
  writeFileSync(join(evidencias, 'falha.json'), JSON.stringify({ mensagem: erro.message, resultados, erros }, null, 2))
  throw erro
} finally {
  await browser.close()
}
