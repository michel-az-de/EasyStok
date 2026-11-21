// #3043: o passo a passo do aviso do aparelho aparece sob demanda, ao toque e ao teclado, sem mexer na faixa
// fechada (uma linha, 1 role=status, até 48 px / 56 no celular: teto da prova 3005, que não muda).
// O texto da faixa é o botão (sem terceiro controle na linha); o painel fica fora de `.aparelho`.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3043/faixa')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes,ContextoCatalogo} from '../../../src/aplicacao/contextos';
import {AvisosDoDispositivo} from '../../../src/app/AvisosDoDispositivo';
import '../../../src/estilos/base.css';
createRoot(document.getElementById('root')).render(<ContextoEstado.Provider value={{fonteApi:true,conversas:[],som:true,audioBloqueado:true,permissaoNotificacao:'denied'}}><ContextoAcoes.Provider value={{}}><ContextoCatalogo.Provider value={{entregadores:[],cardapio:[],meiosDePagamento:[]}}><AvisosDoDispositivo><main>Tela</main></AvisosDoDispositivo></ContextoCatalogo.Provider></ContextoAcoes.Provider></ContextoEstado.Provider>);
`)
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/3043/faixa/index.html`
const browser = await chromium.launch({ headless: true })
const resultados = []
try {
  for (const [largura, altura] of [[1440, 900], [390, 844]]) for (const tema of ['light', 'dark']) {
    const contexto = await browser.newContext({ viewport: { width: largura, height: altura }, colorScheme: tema, locale: 'pt-BR', reducedMotion: 'reduce' })
    const pagina = await contexto.newPage(), erros = []
    pagina.on('pageerror', e => erros.push(e.message))
    await pagina.goto(base)
    const dispensar = pagina.getByRole('button', { name: 'Dispensar avisos do aparelho' })
    await dispensar.waitFor()
    const faixa = dispensar.locator('..')
    const comoLigar = pagina.locator('button[aria-expanded]')
    assert.equal(await comoLigar.count(), 1, 'um botão abre o passo a passo (o texto da faixa)')
    assert.equal(await pagina.getByRole('button').count(), 3, 'a linha tem 3 botões: o texto, Ligar som e Dispensar (nenhum a mais)')
    assert.equal(await pagina.locator('[role=status]').count(), 1, 'continua um role=status só')
    assert.equal(await comoLigar.getAttribute('aria-expanded'), 'false')
    const fechada = (await faixa.boundingBox()).height
    const maximo = largura < 760 ? 56 : 48
    assert.ok(fechada <= maximo, `faixa fechada a ${largura}px tem ${fechada}px (máx ${maximo})`)
    const minimoToque = await pagina.evaluate(() => { const s = document.createElement('i'); s.style.height = 'var(--toque)'; document.body.append(s); const v = s.getBoundingClientRect().height; s.remove(); return v })
    const alvo = await comoLigar.boundingBox()
    assert.ok(alvo.height >= minimoToque - 0.5, `alvo do texto tem ${alvo.height}px (mín var(--toque) = ${minimoToque})`)
    // Teclado: abre, mostra o texto inteiro do detalhe, Esc fecha e devolve o foco.
    await comoLigar.focus()
    await pagina.keyboard.press('Enter')
    assert.equal(await comoLigar.getAttribute('aria-expanded'), 'true')
    const idPainel = await comoLigar.getAttribute('aria-controls')
    assert.ok(idPainel, 'aria-controls aponta o painel')
    const painel = pagina.locator(`[id="${idPainel}"]`)
    assert.match(await painel.innerText(), /cadeado/, 'o passo a passo fica visível')
    assert.ok(await painel.isVisible())
    assert.equal(await faixa.evaluate((f, id) => f.contains(document.getElementById(id)), idPainel), false, 'o painel fica fora da faixa')
    assert.equal((await faixa.boundingBox()).height, fechada, 'a altura da faixa não muda com o painel aberto')
    const caixaPainel = await painel.boundingBox()
    assert.ok(caixaPainel.x >= 0 && caixaPainel.x + caixaPainel.width <= largura, 'painel cabe na largura')
    await pagina.keyboard.press('Escape')
    assert.equal(await comoLigar.getAttribute('aria-expanded'), 'false')
    assert.equal(await painel.count(), 0, 'Esc fecha o painel')
    assert.equal(await pagina.evaluate(() => document.activeElement?.getAttribute('aria-expanded')), 'false', 'o foco volta ao botão')
    // Toque/clique abre e fecha.
    await comoLigar.click()
    assert.ok(await painel.isVisible())
    await comoLigar.click()
    assert.equal(await painel.count(), 0)
    // Foco visível no botão.
    await pagina.keyboard.press('Tab')
    await pagina.keyboard.press('Shift+Tab')
    const contorno = await comoLigar.evaluate(e => getComputedStyle(e).outlineStyle !== 'none' && parseFloat(getComputedStyle(e).outlineWidth) > 0)
    assert.ok(contorno, 'foco visível')
    assert.deepEqual(erros, [])
    resultados.push({ largura, tema, fechada, alvo: alvo.height, painel: caixaPainel.height })
    await contexto.close()
  }
  mkdirSync(pasta, { recursive: true })
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify(resultados, null, 2))
  console.log('Passo a passo do aparelho: texto vira botão, painel fora da faixa, Esc devolve o foco, faixa fechada intacta.')
} finally { await browser.close() }
