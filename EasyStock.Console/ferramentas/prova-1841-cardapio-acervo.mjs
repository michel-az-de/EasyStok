// API real de laboratório descartável. As falhas de rede são injetadas somente no navegador.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve, join } from 'node:path'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const api = process.env.HOMOLOGACAO_API_URL ?? 'http://127.0.0.1:18741'
const consoleUrl = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5741'
const baseline = process.env.CONSOLE_BASELINE_URL
for (const origem of [api, consoleUrl, baseline].filter(Boolean)) assert.equal(new URL(origem).hostname, '127.0.0.1', 'Somente laboratório local')
const pasta = resolve(process.env.EVIDENCIAS_UX ?? '../.build/1841')
mkdirSync(pasta, { recursive: true })
const cardapio = '/api/atendimento/comanda/cardapio'
let token
async function chamar(path, options = {}) {
  const r = await fetch(api + path, { ...options, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...options.headers } })
  const texto = await r.text()
  assert.ok(r.ok, `${options.method ?? 'GET'} ${path}: ${r.status} ${texto.slice(0, 300)}`)
  return texto ? JSON.parse(texto).data : null
}
const post = (path, data) => chamar(path, { method: 'POST', ...(data ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) } : {}) })
const login = await post('/api/auth/login', { email: 'admin@casadababa.demo', senha: 'admin123' })
token = login.token
const claims = JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString())
const itens = await chamar(`${cardapio}/gestao`)
const escolhido = itens.find((i) => i.nome.toLowerCase().includes('lasanha')) ?? itens[0]
const item = { ...escolhido, id: escolhido.cardapioItemId }
assert.ok(item?.id)
const imagens = await chamar('/api/acervo/imagens')
async function prepararImagem(titulo, arquivo, aprovar) {
  let imagem = imagens.find((i) => i.titulo === titulo)
  if (!imagem) {
    const form = new FormData()
    form.append('file', new Blob([readFileSync(new URL(arquivo, import.meta.url))], { type: 'image/png' }), 'prova.png')
    form.append('dados', JSON.stringify({ titulo, origem: 'Fotografia', formato: 'livre', itens: [], referencias: 'Arquivo do repositório usado exclusivamente em laboratório descartável.' }))
    imagem = await chamar('/api/acervo/imagens', { method: 'POST', body: form })
  }
  if (aprovar && !imagem.aprovadoEm) await post(`/api/acervo/imagens/${imagem.id}/aprovar`)
  return imagem
}
const foto = await prepararImagem('Foto aprovada da prova 1841', '../src/assets/hero.png', true)
await prepararImagem('Foto ainda não aprovada 1841', '../src/assets/casa-da-baba.png', false)
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' })
await context.addInitScript((sessao) => sessionStorage.setItem('easystok.sessao', JSON.stringify(sessao)), {
  token, usuario: login.usuario, empresa: { id: claims.empresaId, nome: 'Laboratório 1841' }, expiraEm: Date.now() + 3600000,
})
const page = await context.newPage()
const provas = [], capturas = [], erros = []
page.on('pageerror', (e) => erros.push(e.message))
const passa = (nome) => { provas.push(nome); console.log('ok ' + nome) }
const editor = () => page.getByRole('dialog', { name: 'Editar prato', exact: true })
async function captura(nome) {
  await page.evaluate(() => document.fonts.ready)
  const arquivo = join(pasta, nome + '.png')
  await page.screenshot({ path: arquivo, fullPage: true })
  capturas.push({ arquivo, viewport: page.viewportSize() })
}
async function abrir(origem, duplo = true) {
  const destino = origem + '/#/m/cardapio'
  if (page.url() === destino) await page.reload()
  else await page.goto(destino)
  const editar = page.getByRole('button', { name: `Editar prato ${item.nome}`, exact: true }).first()
  await editar.waitFor()
  if (duplo) await page.getByRole('heading', { name: item.nome, exact: true }).first().dblclick()
  else await editar.click()
  await editor().getByRole('button', { name: 'Salvar prato', exact: true }).waitFor()
  await page.waitForFunction(() => !document.body.innerText.includes('Carregando a ficha do prato'))
}
async function escolher() {
  await editor().getByRole('button', { name: 'Escolher do banco de imagens', exact: true }).click()
  const banco = page.getByRole('dialog', { name: 'Escolher foto do banco de imagens', exact: true })
  await banco.getByLabel('Buscar foto').fill('1841')
  const usar = banco.getByRole('button', { name: `Usar foto ${foto.titulo}`, exact: true })
  await usar.waitFor()
  await page.waitForFunction(() => [...document.querySelectorAll('button')].some((b) => b.textContent.includes('Foto aprovada da prova 1841') && !b.disabled))
  assert.equal(await banco.getByRole('button', { name: 'Usar foto Foto ainda não aprovada 1841', exact: true }).count(), 0)
  await usar.click()
}
try {
  for (const largura of [1280, 1440, 1920]) {
    await page.setViewportSize({ width: largura, height: 1000 })
    if (baseline) {
      await abrir(baseline, false)
      assert.equal(await editor().getByRole('button', { name: 'Escolher do banco de imagens', exact: true }).count(), 0)
      await captura(`antes-editor-${largura}`)
    }
    await abrir(consoleUrl)
    await captura(`depois-editor-${largura}`)
    await editor().getByRole('button', { name: 'Escolher do banco de imagens', exact: true }).click()
    await page.getByRole('dialog', { name: 'Escolher foto do banco de imagens', exact: true }).getByLabel('Buscar foto').fill('1841')
    await page.getByRole('button', { name: `Usar foto ${foto.titulo}`, exact: true }).waitFor()
    await page.waitForFunction(() => [...document.querySelectorAll('button')].some((b) => b.textContent.includes('Foto aprovada da prova 1841') && !b.disabled))
    await captura(`banco-${largura}`)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
    await page.keyboard.press('Tab')
    await page.keyboard.press('Escape')
    await page.getByRole('dialog', { name: 'Escolher foto do banco de imagens', exact: true }).waitFor({ state: 'hidden' })
    assert.equal(await editor().isVisible(), true)
    assert.equal(await editor().getByRole('button', { name: 'Escolher do banco de imagens', exact: true }).evaluate((b) => b === document.activeElement), true)
  }
  passa('API real: editor em 1280, 1440 e 1920; duplo clique, Escape e foco preservados')
  const nome = editor().getByLabel('Nome do prato', { exact: true })
  const rascunho = item.nome + ' (rascunho preservado)'
  const anterior = await chamar(`${cardapio}/${item.id}`)
  await nome.fill(rascunho)
  await escolher()
  assert.equal(await nome.inputValue(), rascunho)
  passa('Seleção só mostra fotos aprovadas e preserva os campos do editor')
  assert.equal((await chamar(`${cardapio}/${item.id}`)).fotoUrl, anterior.fotoUrl)
  let falhar = true
  const chamadas = []
  const publicar = `**/api/acervo/imagens/${foto.id}/publicar`
  await page.route(publicar, async (route) => {
    chamadas.push(route.request().postDataJSON())
    if (falhar) return route.fulfill({ status: 503, json: { error: { code: 'PROVA_1841', message: 'Falha controlada da prova 1841' } } })
    await route.continue()
  })
  await editor().getByRole('button', { name: 'Salvar foto', exact: true }).click()
  await editor().getByRole('alert').filter({ hasText: /Falha controlada|503|indisponível/ }).waitFor()
  assert.equal((await chamar(`${cardapio}/${item.id}`)).fotoUrl, anterior.fotoUrl)
  assert.equal(await nome.inputValue(), rascunho)
  passa('503 preserva a capa anterior, a escolha pendente e o rascunho')
  falhar = false
  await editor().getByRole('button', { name: 'Salvar foto', exact: true }).click()
  await editor().getByText('Foto salva. Ela já está no cardápio.', { exact: true }).waitFor()
  const salvo = await chamar(`${cardapio}/${item.id}`)
  assert.notEqual(salvo.fotoUrl, anterior.fotoUrl)
  assert.equal(salvo.nome, item.nome, 'Salvar foto não grava o rascunho dos demais campos')
  assert.equal(await nome.inputValue(), rascunho)
  assert.deepEqual(chamadas[0], chamadas[1], 'Retentativa mantém operação e capa esperada')
  assert.equal(chamadas[0].capaEsperada, anterior.fotoUrl ?? null)
  passa('Foto publicada e relida na API; retentativa mantém operação; demais campos não são salvos')
  await page.unroute(publicar)
  await captura('foto-publicada-rascunho-preservado')
  await nome.fill(item.nome)
  await editor().getByRole('button', { name: 'Salvar prato', exact: true }).click()
  await editor().waitFor({ state: 'hidden' })
  await abrir(consoleUrl)
  const imagemSalva = editor().getByRole('img', { name: `Foto de ${item.nome}`, exact: true })
  const src = await imagemSalva.getAttribute('src')
  assert.equal(new URL(src, consoleUrl).pathname.split('/').pop(), new URL(salvo.fotoUrl, api).pathname.split('/').pop())
  await imagemSalva.evaluate((img) => img.decode())
  assert.ok(await imagemSalva.evaluate((img) => img.naturalWidth > 0))
  passa('Reabrir o prato recupera a foto persistida')
  await escolher()
  await editor().getByLabel('Adicionar à galeria', { exact: true }).check()
  await editor().getByRole('button', { name: 'Salvar foto', exact: true }).click()
  await editor().getByText('Foto salva. Ela já está no cardápio.', { exact: true }).waitFor()
  const galeria = await chamar(`${cardapio}/${item.id}`)
  assert.equal(galeria.fotoUrl, salvo.fotoUrl)
  assert.ok(galeria.fotos.length > salvo.fotos.length)
  passa('Adicionar à galeria conserva a capa existente')
  await nome.fill(rascunho)
  await escolher()
  await post(`/api/acervo/imagens/${foto.id}/publicar`, {
    itemId: item.id, galeria: false, capaEsperada: galeria.fotoUrl, operacaoId: crypto.randomUUID(),
  })
  const concorrente = await chamar(`${cardapio}/${item.id}`)
  assert.notEqual(concorrente.fotoUrl, galeria.fotoUrl)
  const tentativasConflito = []
  await page.route(publicar, async (route) => { tentativasConflito.push(route.request().postDataJSON()); await route.continue() })
  await editor().getByRole('button', { name: 'Salvar foto', exact: true }).click()
  await editor().getByRole('alert').filter({ hasText: 'A capa mudou' }).waitFor()
  assert.equal((await chamar(`${cardapio}/${item.id}`)).fotoUrl, concorrente.fotoUrl)
  assert.equal(await nome.inputValue(), rascunho)
  await captura('conflito-capa-preserva-rascunho')
  const detalheUrl = `**${cardapio}/${item.id}`
  await page.route(detalheUrl, (route) => route.fulfill({ status: 503, json: { error: { code: 'PROVA_1841', message: 'Falha ao conferir foto atual' } } }))
  await editor().getByRole('button', { name: 'Conferir foto atual', exact: true }).click()
  await editor().getByRole('alert').filter({ hasText: 'Falha ao conferir foto atual' }).waitFor()
  assert.equal(await editor().getByRole('button', { name: 'Salvar foto', exact: true }).isVisible(), true)
  assert.equal(await nome.inputValue(), rascunho)
  await page.unroute(detalheUrl)
  await editor().getByRole('button', { name: 'Conferir foto atual', exact: true }).click()
  await editor().getByRole('button', { name: 'Salvar foto', exact: true }).waitFor({ state: 'hidden' })
  assert.equal(await nome.inputValue(), rascunho)
  const capaRelida = editor().getByRole('img', { name: `Foto de ${rascunho}`, exact: true })
  assert.equal(new URL(await capaRelida.getAttribute('src'), consoleUrl).pathname.split('/').pop(), new URL(concorrente.fotoUrl, api).pathname.split('/').pop())
  await escolher()
  await editor().getByRole('button', { name: 'Salvar foto', exact: true }).click()
  await editor().getByText('Foto salva. Ela já está no cardápio.', { exact: true }).waitFor()
  assert.equal(tentativasConflito[1].capaEsperada, concorrente.fotoUrl)
  assert.notEqual(tentativasConflito[1].operacaoId, tentativasConflito[0].operacaoId)
  assert.equal((await chamar(`${cardapio}/${item.id}`)).nome, item.nome)
  assert.equal(await nome.inputValue(), rascunho)
  await page.unroute(publicar)
  passa('Conflito de capa permite conferir foto atual e escolher novamente sem perder rascunho; falha da releitura preserva escolha')
  await nome.fill(item.nome)
  await editor().getByRole('button', { name: 'Salvar prato', exact: true }).click()
  await editor().waitFor({ state: 'hidden' })
  await abrir(consoleUrl)
  await editor().getByRole('button', { name: 'Cancelar', exact: true }).click()
  await page.getByRole('button', { name: `Editar prato ${item.nome}`, exact: true }).first().focus()
  await page.keyboard.press('Enter')
  await editor().waitFor()
  passa('Edição continua acessível pelo botão e teclado')
  await page.setViewportSize({ width: 390, height: 844 })
  await escolher()
  await captura('editor-mobile-390')
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
  passa('Editor utilizável em 390 px sem rolagem horizontal')
  assert.deepEqual(erros, [])
  writeFileSync(join(pasta, 'resultado.json'), JSON.stringify({ provas, capturas, erros, api, consoleUrl, baseline, itemId: item.id, imagemId: foto.id }, null, 2))
  console.log(`${provas.length} cenários aprovados; evidências em ${pasta}`)
} catch (e) {
  await captura('falha')
  throw e
} finally { await browser.close() }
