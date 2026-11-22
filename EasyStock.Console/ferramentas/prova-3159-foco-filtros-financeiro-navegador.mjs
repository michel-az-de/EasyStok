// #3159 (re-pontuação da #3076, item 6): Chromium, componentes reais e HTTP controlado, a 1440 (computador) e a 390
// (celular: isMobile e hasTouch). Na conta a pagar, fechado o bloco de nova categoria o foco não cai no body: Criar (botão
// ou Enter) leva ao campo Categoria com a nova escolhida, Cancelar volta ao "+ Nova categoria" e a falha volta ao nome.
// Mais filtros: os 4 controles têm nome na árvore de acessibilidade e, a 390, as datas medem ao menos 44 px.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3159/foco-filtros')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {PainelFinanceiro} from '../../../src/features/gestao/financeiro/PainelFinanceiro';
import '../../../src/estilos/base.css';
document.body.style.cssText='margin:0;padding:16px;box-sizing:border-box;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
const acoes={visualizarContasPagar:true,gerenciarContasPagar:true,gerenciarCategoriasFinanceiras:true};
createRoot(document.getElementById('root')).render(<ContextoAcessoModulos.Provider value={{acoes,permite:()=>true}}><PainelFinanceiro tela="pagar"/></ContextoAcessoModulos.Provider>);
`)
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/3159/foco-filtros/index.html`
const browser = await chromium.launch({ headless: true })
const erros = []

async function montar({ largura, movel }) {
  const categorias = [{ id: 'K1', nome: 'Aluguel', tipo: 'Despesa', ativa: true }]
  const contexto = await browser.newContext({ viewport: { width: largura, height: movel ? 844 : 1000 }, isMobile: movel, hasTouch: movel, locale: 'pt-BR', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
  await contexto.route('**/api/**', async route => {
    const req = route.request(), path = new URL(req.url()).pathname
    if (!path.startsWith('/api/')) return route.continue()
    if (req.method() === 'POST' && path === '/api/financeiro/categorias') {
      const corpo = req.postDataJSON()
      if (corpo.nome === 'Falha') return route.fulfill({ status: 400, json: { error: { message: 'Nome de categoria recusado.' } } })
      const nova = { id: `K${categorias.length + 1}`, nome: corpo.nome, tipo: corpo.tipo, ativa: true }
      categorias.push(nova)
      return route.fulfill({ status: 201, json: { data: nova } })
    }
    if (req.method() !== 'GET') return route.fulfill({ status: 500, json: { error: { message: `Inesperado: ${req.method()} ${path}` } } })
    if (path === '/api/financeiro/categorias') return route.fulfill({ json: { data: categorias } })
    return route.fulfill({ json: { data: [], meta: { total: 0 } } })
  })
  const pagina = await contexto.newPage()
  pagina.setDefaultTimeout(10000)
  pagina.on('pageerror', e => erros.push(e.message))
  await pagina.goto(base)
  return { contexto, pagina }
}
const focado = (locator) => locator.evaluate(el => el === document.activeElement)
const ondeEsta = (pagina) => pagina.evaluate(() => `${document.activeElement?.tagName} ${document.activeElement?.textContent?.slice(0, 30) ?? ''}`)

for (const { largura, movel } of [{ largura: 1440, movel: false }, { largura: 390, movel: true }]) {
  const { contexto, pagina } = await montar({ largura, movel })
  try {
    await pagina.getByRole('button', { name: 'Nova conta a pagar', exact: true }).first().click()
    const janela = pagina.getByRole('dialog', { name: 'Nova conta a pagar', exact: true }); await janela.waitFor()
    const nova = janela.getByRole('button', { name: '+ Nova categoria', exact: true })
    const nome = janela.getByRole('textbox', { name: /Nome da nova categoria/ })
    const categoria = janela.getByRole('combobox', { name: /Categoria financeira/ })
    const esperarFoco = async (alvo, caso) => {
      for (let i = 0; i < 30 && !(await focado(alvo)); i++) await pagina.waitForTimeout(50)
      assert.equal(await focado(alvo), true, `${caso} a ${largura}: o foco ficou em ${await ondeEsta(pagina)}`)
    }

    // 1. Criar pelo botão: o foco vai ao campo Categoria, com a nova escolhida.
    await nova.click(); await nome.fill('Energia')
    await janela.getByRole('button', { name: 'Criar categoria', exact: true }).click()
    await esperarFoco(categoria, 'Criar')
    assert.equal(await categoria.inputValue(), 'K2', 'A nova vem escolhida')
    await pagina.screenshot({ path: resolve(pasta, `categoria-${largura}.png`), fullPage: true })

    // 2. Enter no nome: o mesmo.
    await nova.click(); await nome.fill('Internet'); await nome.press('Enter')
    await esperarFoco(categoria, 'Enter')
    assert.equal(await categoria.inputValue(), 'K3')

    // 3. Cancelar: volta ao "+ Nova categoria" que abriu o bloco.
    await nova.click(); await nome.fill('Água')
    await janela.getByRole('button', { name: 'Não criar categoria', exact: true }).click()
    await esperarFoco(nova, 'Cancelar')

    // 4. Falha da API: o foco volta ao nome, onde o erro aparece.
    await nova.click(); await nome.fill('Falha')
    await janela.getByRole('button', { name: 'Criar categoria', exact: true }).click()
    await janela.getByText('Nome de categoria recusado.').waitFor()
    await esperarFoco(nome, 'Falha')
    await pagina.screenshot({ path: resolve(pasta, `falha-categoria-${largura}.png`), fullPage: true })
    await pagina.keyboard.press('Escape')
    const descartar = pagina.getByRole('button', { name: /Descartar/ })
    if (await descartar.count()) await descartar.first().click()
    await janela.waitFor({ state: 'detached' })

    // 5. Mais filtros, aberto: nome de cada controle na árvore de acessibilidade do Chromium (ariaSnapshot).
    const detalhes = pagina.locator('details').filter({ hasText: 'Mais filtros' })
    if (!(await detalhes.evaluate(el => el.open))) await detalhes.locator('summary').click()
    const arvore = await detalhes.locator('form').ariaSnapshot()
    writeFileSync(resolve(pasta, `arvore-${largura}.yaml`), arvore)
    const controles = arvore.split(String.fromCharCode(10)).filter(l => /^\s*- (combobox|textbox|spinbutton|button|group)(\s|:|$)/.test(l))
    assert.ok(controles.length >= 4, `Mais filtros tem os 4 controles: ${arvore}`)
    assert.deepEqual(controles.filter(l => !/"[^"]+"/.test(l)), [], `Controles sem nome a ${largura}: ${arvore}`)
    const datas = await detalhes.locator('input[type="date"]').evaluateAll(els => els.map(el => { const r = el.getBoundingClientRect(); return [Math.round(r.width), Math.round(r.height)] }))
    assert.equal(datas.length, 2)
    // O alvo de toque de 44 px vale no celular; no computador o campo segue a altura dos demais.
    if (movel) for (const [l, a] of datas) assert.ok(l >= 44 && a >= 44, `Data com ${l}x${a} px a ${largura}`)
    assert.ok(await pagina.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), `Sem rolagem lateral a ${largura}`)
    await pagina.screenshot({ path: resolve(pasta, `filtros-${largura}.png`), fullPage: true })
    writeFileSync(resolve(pasta, `medidas-${largura}.json`), JSON.stringify({ movel, datas }, null, 2))
  } catch (erro) { await pagina.screenshot({ path: resolve(pasta, `falha-${largura}.png`), fullPage: true }); console.error(erros.join('\n')); await browser.close(); throw erro }
  await contexto.close()
}
assert.deepEqual(erros, [])
await browser.close()
console.log('#3159 navegador: Criar (botão e Enter), Cancelar e falha devolvem o foco; Mais filtros com nome e datas de 44 px a 390 em celular; 1440 e 390.')
