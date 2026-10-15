// Chromium e API reais no laboratório local; falhas transitórias só no navegador.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve, join } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const api = process.env.HOMOLOGACAO_API_URL ?? 'http://127.0.0.1:18741'
const consoleUrl = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5746'
for (const origem of [api, consoleUrl]) assert.equal(new URL(origem).hostname, '127.0.0.1', 'Somente laboratório local')
const pasta = resolve(process.env.EVIDENCIAS_UX ?? '../.build/1846')
mkdirSync(pasta, { recursive: true })
let token
async function chamar(path, options = {}) {
  const r = await fetch(api + path, { ...options, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...options.headers } })
  const texto = await r.text()
  assert.ok(r.ok, `${options.method ?? 'GET'} ${path}: ${r.status} ${texto.slice(0, 250)}`)
  return texto ? JSON.parse(texto).data : null
}
const post = (path, dados) => chamar(path, { method: 'POST', ...(dados ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(dados) } : {}) })
const login = await post('/api/auth/login', { email: 'admin@casadababa.demo', senha: 'admin123' })
token = login.token
const claims = JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString())
const cardapio = '/api/atendimento/comanda/cardapio'
const item = (await chamar(`${cardapio}/gestao`)).find(i => !i.nome.toLowerCase().includes('lasanha'))
assert.ok(item?.cardapioItemId)
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' })
await context.addInitScript(s => sessionStorage.setItem('easystok.sessao', JSON.stringify(s)), {
  token, usuario: login.usuario, empresa: { id: claims.empresaId, nome: 'Laboratório1846' }, expiraEm: Date.now() + 3600000,
})
const page = await context.newPage()
const provas = [], capturas = [], erros = []
page.on('pageerror', e => erros.push(e.message))
const passa = nome => { provas.push(nome); console.log('ok ' + nome) }
async function captura(nome) { await page.evaluate(() => document.fonts.ready); const arquivo = join(pasta, nome + '.png'); await page.screenshot({ path: arquivo, fullPage: true }); capturas.push(arquivo) }
const titulo = i => `1846 imagem de prova ${String(i).padStart(2, '0')}`
try {
  const existentes = [...await chamar('/api/acervo/imagens?busca=1846%20imagem%20de%20prova'), ...await chamar('/api/acervo/imagens?busca=1846%20imagem%20de%20prova&pagina=1')]
  for (let i = 0; i < 61; i++) {
    let imagem = existentes.find(f => f.titulo === titulo(i))
    if (!imagem) {
      const base64 = await page.evaluate(i => { const canvas = document.createElement('canvas'); canvas.width = 120; canvas.height = 90; const c = canvas.getContext('2d'); c.fillStyle = '#f5f1e8'; c.fillRect(0, 0, 120, 90); c.fillStyle = '#263b2d'; c.font = 'bold 24px sans-serif'; c.fillText('1846', 26, 35); c.fillText(String(i), 42, 65); return canvas.toDataURL('image/png').split(',')[1] }, i)
      const form = new FormData()
      form.append('file', new Blob([Buffer.from(base64, 'base64')], { type: 'image/png' }), `1846-${i}.png`)
      form.append('dados', JSON.stringify({ titulo: titulo(i), origem: 'Fotografia', formato: 'livre', natureza: 'Produto', itens: [], referencias: 'Fixture sintética numerada, exclusiva de laboratório1846. Não é fotografia de alimento.' }))
      imagem = await chamar('/api/acervo/imagens', { method: 'POST', body: form })
      existentes.push(imagem)
    }
    if (i !== 59 && !imagem.aprovadoEm) await post(`/api/acervo/imagens/${imagem.id}/aprovar`)
  }
  const inicial = existentes.find(f => f.titulo === titulo(60))
  const detalhe = await chamar(`${cardapio}/${item.cardapioItemId}`)
  if (!detalhe.fotoUrl) await post(`/api/acervo/imagens/${inicial.id}/publicar`, { itemId: item.cardapioItemId, galeria: false, capaEsperada: null, operacaoId: crypto.randomUUID() })
  await page.goto(consoleUrl + '/#/m/cardapio/imagens')
  const buscaExterna = page.getByLabel('Buscar em todo o acervo', { exact: true })
  await buscaExterna.fill('filtro-externo-sem-resultados-1846')
  await page.getByRole('combobox', { name: /^Origem da imagem/ }).selectOption('Canva')
  await page.getByText('Nenhuma imagem nos filtros selecionados.', { exact: true }).waitFor()
  const abrir = () => page.getByRole('button', { name: `Escolher imagem do acervo para ${item.nome}`, exact: true }).click()
  await abrir()
  const modal = () => page.getByRole('dialog', { name: `Imagem para ${item.nome}`, exact: true })
  if (process.argv.includes('--antes')) {
    await modal().getByText('Nenhuma imagem aprovada nesta página do acervo. Aprove uma imagem ou mude os filtros abaixo.', { exact: true }).waitFor()
    assert.equal(await modal().getByRole('textbox').count(), 0)
    assert.equal(await modal().getByRole('button', { name: 'Próxima', exact: true }).count(), 0)
    await captura('antes-seletor-1440')
    passa('Antes: filtro externo deixa modal vazio, sem busca/paginação para recuperar')
  } else {
    const busca = () => modal().getByLabel('Buscar imagem aprovada', { exact: true })
    await busca().fill('1846 imagem de prova')
    await modal().getByRole('button', { name: new RegExp(titulo(60)) }).waitFor()
    assert.equal(await modal().getByRole('button', { name: new RegExp(titulo(59)) }).count(), 0)
    await modal().getByRole('button', { name: 'Próxima', exact: true }).click()
    await modal().getByRole('button', { name: new RegExp(titulo(0)) }).waitFor()
    assert.equal(await modal().getByRole('button', { name: 'Próxima', exact: true }).isDisabled(), true)
    passa('API real: busca independente, somente aprovadas e segunda página acessível')
    for (const width of [1280, 1440, 1920, 390]) {
      await page.setViewportSize({ width, height: 1000 })
      await captura(`depois-seletor-${width}`)
      assert.equal(await modal().evaluate(el => el.scrollWidth > el.clientWidth + 1), false)
    }
    await page.keyboard.press('Escape')
    assert.equal(await buscaExterna.inputValue(), 'filtro-externo-sem-resultados-1846')
    assert.equal(await page.getByRole('combobox', { name: /^Origem da imagem/ }).inputValue(), 'Canva')
    assert.equal(await page.getByRole('button', { name: `Escolher imagem do acervo para ${item.nome}`, exact: true }).evaluate(el => el === document.activeElement), true)
    passa('Desktop/celular: sem overflow; Escape restaura foco e mantém filtros externos')
    await page.setViewportSize({ width: 1440, height: 1000 })
    await abrir()
    await busca().fill('nao-existe1846')
    await modal().getByText('Nenhuma imagem aprovada nesta página.', { exact: true }).waitFor()
    let falhar = true
    await page.route('**/api/acervo/imagens?*', async route => {
      if (falhar && new URL(route.request().url()).searchParams.get('busca') === 'falha1846') return route.fulfill({ status: 503, json: { error: { code: 'prova1846', message: 'Falha transitória da prova1846' } } })
      return route.continue()
    })
    await busca().fill('falha1846')
    await modal().getByRole('alert').filter({ hasText: 'Falha transitória' }).waitFor()
    assert.equal(await modal().locator('button').filter({ hasText: /1846 imagem/ }).count(), 0)
    falhar = false
    await modal().getByRole('button', { name: 'Tentar novamente', exact: true }).click()
    await modal().getByText('Nenhuma imagem aprovada nesta página.', { exact: true }).waitFor()
    await page.unroute('**/api/acervo/imagens?*')
    passa('Vazio e erro: nenhuma escolha obsoleta; recuperação no próprio modal')
    let liberarLenta, avisarChegada, avisarResposta
    const esperaLenta = new Promise(resolve => { liberarLenta = resolve })
    const chegouLenta = new Promise(resolve => { avisarChegada = resolve })
    const respondeuLenta = new Promise(resolve => { avisarResposta = resolve })
    await page.route('**/api/acervo/imagens?*', async route => {
      if (new URL(route.request().url()).searchParams.get('busca') !== titulo(0)) return route.continue()
      const resposta = await route.fetch()
      avisarChegada()
      await esperaLenta
      await route.fulfill({ response: resposta })
      avisarResposta()
    })
    await busca().fill(titulo(0))
    await chegouLenta
    await busca().fill(titulo(1))
    await modal().getByRole('button', { name: new RegExp(titulo(1)) }).waitFor()
    liberarLenta()
    await respondeuLenta
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    assert.equal(await modal().getByRole('button', { name: new RegExp(titulo(0)) }).count(), 0)
    await page.unroute('**/api/acervo/imagens?*')
    passa('Busca rápida: resposta antiga atrasada não substitui o resultado atual')
    await busca().fill(titulo(0))
    const escolher = modal().getByRole('button', { name: new RegExp(titulo(0)) })
    const anterior = await chamar(cardapio + '/' + item.cardapioItemId)
    const historicoAntes = await chamar('/api/acervo/imagens/' + existentes.find(f => f.titulo === titulo(0)).id + '/historico')
    await escolher.focus()
    await escolher.press('Enter')
    const confirmar = page.getByRole('dialog', { name: 'Conferir publicação', exact: true })
    await confirmar.getByRole('img', { name: titulo(0), exact: true }).waitFor()
    assert.equal((await chamar(`${cardapio}/${item.cardapioItemId}`)).fotoUrl, anterior.fotoUrl)
    await confirmar.getByRole('button', { name: 'Confirmar publicação', exact: true }).click()
    await confirmar.waitFor({ state: 'detached' })
    const posterior = await chamar(`${cardapio}/${item.cardapioItemId}`)
    const historico = await chamar(`/api/acervo/imagens/${existentes.find(f => f.titulo === titulo(0)).id}/historico`)
    assert.equal(historico.length, historicoAntes.length + 1)
    assert.ok(historico.some(h => !historicoAntes.some(a => a.id === h.id) && h.itemId === item.cardapioItemId && h.url === posterior.fotoUrl))
    assert.equal(await buscaExterna.inputValue(), 'filtro-externo-sem-resultados-1846')
    assert.equal(await page.getByRole('combobox', { name: /^Origem da imagem/ }).inputValue(), 'Canva')
    await captura('publicacao-confirmada-filtros-preservados')
    passa('API real: foto fora dos filtros externos tem prévia e publicação persistida após confirmar')
  }
  assert.deepEqual(erros, [])
  writeFileSync(join(pasta, process.argv.includes('--antes') ? 'antes.json' : 'resultado.json'), JSON.stringify({ provas, capturas, erros, item: item.cardapioItemId }, null, 2))
  console.log(`${provas.length} cenários aprovados`)
} catch (e) { await page.screenshot({ path: join(pasta, 'falha.png'), fullPage: true }).catch(() => {}); throw e }
finally { await browser.close() }

