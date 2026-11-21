// #3005: a faixa de aviso do aparelho (som e notificação bloqueados) é uma linha neutra, uma só para os dois
// avisos, e a dispensa vale por aparelho (localStorage), voltando só se o estado do aparelho mudar.
// Componente real montado numa cena mínima (mesmo desenho da prova 2395); sem API, pedido ou mensagem.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3005/faixa')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes,ContextoCatalogo} from '../../../src/aplicacao/contextos';
import {AvisosDoDispositivo} from '../../../src/app/AvisosDoDispositivo';
import '../../../src/estilos/base.css';
const q=new URLSearchParams(location.search);
createRoot(document.getElementById('root')).render(<ContextoEstado.Provider value={{fonteApi:true,conversas:[],som:true,audioBloqueado:q.get('audio')!=='livre',permissaoNotificacao:q.get('perm')??'denied'}}><ContextoAcoes.Provider value={{}}><ContextoCatalogo.Provider value={{entregadores:[],cardapio:[],meiosDePagamento:[]}}><AvisosDoDispositivo><main>Tela</main></AvisosDoDispositivo></ContextoCatalogo.Provider></ContextoAcoes.Provider></ContextoEstado.Provider>);
`)
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/3005/faixa/index.html`
const browser = await chromium.launch({ headless: true })
const resultados = []
try {
  for (const [largura, altura] of [[1440, 900], [390, 844]]) for (const tema of ['light', 'dark']) {
    const contexto = await browser.newContext({ viewport: { width: largura, height: altura }, colorScheme: tema, locale: 'pt-BR', reducedMotion: 'reduce' })
    const pagina = await contexto.newPage(), erros = []
    pagina.on('pageerror', e => erros.push(e.message))
    const botao = pagina.getByRole('button', { name: 'Dispensar avisos do aparelho' })
    const faixa = botao.locator('..')
    await pagina.goto(base)
    await botao.waitFor()
    // Uma linha só, mesmo com os dois avisos, até 48 px.
    assert.equal(await pagina.locator('[role=status]').count(), 1, 'os dois avisos viram uma faixa só')
    const caixa = await faixa.boundingBox()
    assert.ok(caixa.height <= 48, `faixa a ${largura}px tem ${caixa.height}px (máx 48)`)
    // Tom neutro: nem a cor nem o fundo nem a borda do tom de erro.
    const tons = await pagina.evaluate(() => {
      const lerToken = (nome, prop) => { const s = document.createElement('i'); s.style[prop] = `var(${nome})`; document.body.append(s); const v = getComputedStyle(s)[prop]; s.remove(); return v }
      const f = document.querySelector('[aria-label="Dispensar avisos do aparelho"]').parentElement, c = getComputedStyle(f)
      return { cor: c.color, fundo: c.backgroundColor, borda: c.borderTopColor, erro: lerToken('--erro', 'color'), erroPastel: lerToken('--erro-pastel', 'color') }
    })
    assert.notEqual(tons.cor, tons.erro, 'texto não usa --erro')
    assert.notEqual(tons.borda, tons.erro, 'borda não usa --erro')
    assert.notEqual(tons.fundo, tons.erroPastel, 'fundo não usa --erro-pastel')
    // Ação que resolve, ao lado de Dispensar.
    assert.equal(await pagina.getByRole('button', { name: 'Ligar som' }).count(), 1, 'ação primária Ligar som')
    // Dispensa lembrada por aparelho: sobrevive a nova montagem (recarga).
    await botao.click()
    assert.equal(await botao.count(), 0)
    await pagina.reload()
    await pagina.getByText('Tela', { exact: true }).waitFor()
    assert.equal(await pagina.getByRole('button', { name: 'Dispensar avisos do aparelho' }).count(), 0, 'dispensa persiste depois de recarregar')
    // Estado do aparelho mudou (só o som continua bloqueado): o aviso volta.
    await pagina.goto(`${base}?perm=granted`)
    await botao.waitFor()
    assert.deepEqual(erros, [])
    resultados.push({ largura, tema, altura: caixa.height })
    await contexto.close()
  }
  // Sem storage (modo privado, cota): a tela funciona e a dispensa vale na visita.
  const semStorage = await browser.newContext({ viewport: { width: 390, height: 844 }, locale: 'pt-BR', reducedMotion: 'reduce' })
  await semStorage.addInitScript(() => {
    Storage.prototype.getItem = () => { throw new Error('sem storage') }
    Storage.prototype.setItem = () => { throw new Error('sem storage') }
  })
  const p = await semStorage.newPage(), errosSemStorage = []
  p.on('pageerror', e => errosSemStorage.push(e.message))
  await p.goto(base)
  const b = p.getByRole('button', { name: 'Dispensar avisos do aparelho' })
  await b.waitFor()
  await b.click()
  assert.equal(await b.count(), 0, 'dispensa vale na visita sem storage')
  assert.deepEqual(errosSemStorage, [])
  await semStorage.close()
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify(resultados, null, 2))
  console.log('Faixa do aparelho: uma linha até 48 px, tom neutro, dois avisos juntos, dispensa por aparelho e sem storage.')
} finally { await browser.close() }
