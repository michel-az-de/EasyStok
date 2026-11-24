// #3254 (S2 da spec 3237): a linha de Cadastros > Clientes com a relação do cliente, medida no navegador. Dados fictícios.
// 1920 e 1440: sem rolagem horizontal (página e lista) e sem sobreposição entre os blocos da linha. 390: sem rolagem da página.
// O estágio desenhado é o que a API mandou; "Em risco" só com emRisco; barra de gasto em 0 com carteira 0; tag interna oculta;
// vazio, carregando, erro e API antiga (sem os campos novos). Toda espera é por condição; cada largura roda em paralelo.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(process.env.PASTA_PRINTS_3254 ?? resolve(import.meta.dirname, '../.build/3254-linha'))
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const idDe = (n) => `a${String(n).padStart(7, '0')}-1111-4111-8111-111111111111`
const nomeDe = (n) => `Cliente Ficticio ${String(n).padStart(2, '0')}`
const base = { ocorrenciasAbertas: 0, ocorrenciasTotal: 0, notasTotal: 0, ultimaNota: null, tags: [], emRisco: false }
// 1 sem pedido; 2 primeira compra; 3 recorrente em risco com reclamação, notas e a tag interna; 4 FIEL COM 1 COMPRA (a API manda, a tela só traduz);
// 5 fiel com mais de 3 tags.
const REL = [
  { ...base, estagio: 'sem_pedido', gastoTotal: 0, compras: 0, ticketMedio: null, primeiraCompraEm: null, ultimaCompraEm: null },
  { ...base, estagio: 'primeira_compra', gastoTotal: 38, compras: 1, ticketMedio: 38, primeiraCompraEm: '2026-11-15T15:00:00Z', ultimaCompraEm: '2026-11-15T15:00:00Z', notasTotal: 1, ultimaNota: 'Pede entrega depois das 18h.', tags: ['Instagram'] },
  { ...base, estagio: 'recorrente', gastoTotal: 444, compras: 6, ticketMedio: 74, primeiraCompraEm: '2026-01-10T15:00:00Z', ultimaCompraEm: '2026-09-20T15:00:00Z', ocorrenciasAbertas: 1, ocorrenciasTotal: 2, notasTotal: 3, ultimaNota: 'Ligar antes de repor.', tags: ['WhatsApp', 'Restaurante', 'recompra_convidada'], emRisco: true },
  { ...base, estagio: 'fiel', gastoTotal: 500, compras: 1, ticketMedio: 500, primeiraCompraEm: '2026-11-18T15:00:00Z', ultimaCompraEm: '2026-11-18T15:00:00Z' },
  { ...base, estagio: 'fiel', gastoTotal: 1020, compras: 12, ticketMedio: 85, primeiraCompraEm: '2025-06-10T15:00:00Z', ultimaCompraEm: '2026-11-20T15:00:00Z', tags: ['Fiel', 'Instagram', 'Evento', 'Atacado'] },
]
const cliente = (n, extra = {}) => ({ id: idDe(n), revisao: 1, nome: nomeDe(n), telefone: `1191111${String(1000 + n)}`, email: '', documento: '', tipoPessoa: 'fisica', ativo: true, orderCount: 0, ...extra })
const comRelacao = REL.map((r, i) => cliente(i + 1, { ...r, orderCount: r.compras }))
const semRelacao = REL.map((_, i) => cliente(i + 1))
const carteiraZero = REL.map((_, i) => cliente(i + 1, { ...REL[0] }))
const browser = await chromium.launch({ headless: true })
const erros = []
const falhas = []
let ultima = null

// `resposta`: lista de clientes, 'erro' ou { portao, clientes }, que segura a resposta até o teste liberar (sem espera por tempo).
async function preparar(largura, resposta, meta = {}) {
  const movel = largura < 700
  const contexto = await browser.newContext({ viewport: { width: largura, height: movel ? 844 : 1080 }, isMobile: movel, hasTouch: movel, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja' }, usuario: { id: 'dona', nome: 'Dona de teste' } })))
  const acoes = Object.fromEntries(['verPedidos', 'atenderConversas', 'editarAtendimento', 'verCadastrosClientes', 'verCadastrosProdutos', 'verCadastrosFornecedores', 'verCadastrosFinanceiros'].map((n) => [n, true]))
  await contexto.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    if (!path.startsWith('/api/')) return route.continue() // o glob também casa módulos do Vite em src/infra/api
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros', 'cardapio', 'financeiro'].map((id) => ({ id, liberado: true })), acoes })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
    if (path === '/api/notificacoes/badge') return ok({ count: 0 })
    if (path === '/api/clientes') {
      if (resposta === 'erro') return route.fulfill({ status: 500, json: { error: { message: 'Falha de teste' } } })
      const lista = Array.isArray(resposta) ? resposta : (await resposta.portao, resposta.clientes)
      return ok(lista, { meta: { total: lista.length, totalCount: lista.length, page: 1, limit: 20, ...meta } })
    }
    return ok([], { meta: { totalCount: 0, total: 0, page: 1, limit: 20 } })
  })
  const page = await contexto.newPage()
  page.setDefaultTimeout(15000)
  page.on('pageerror', (e) => erros.push(e.message)); ultima = page
  await page.goto(`${URL_CONSOLE}/#/m/cadastros/clientes`)
  return { contexto, page }
}
const linha = (page, n) => page.locator('tbody tr').filter({ hasText: nomeDe(n) })
const item = async (nome, corpo) => {
  try { await corpo(); console.log(`ok    ${nome}`) } catch (e) { const motivo = e.message.split('\n').slice(0, 4).join(' | '); falhas.push(`${nome}: ${motivo}`); console.log(`FALHA ${nome}: ${motivo}`) }
}
// Rolagem horizontal da página e da lista (a tabela contra o contêiner dela e cada ancestral rolável).
const rolagem = (page) => page.evaluate(() => {
  const exc = (el) => el.scrollWidth - el.clientWidth
  const tabela = document.querySelector('tbody')?.closest('table')
  const ancestrais = []
  for (let el = tabela?.parentElement; el && el !== document.documentElement; el = el.parentElement) ancestrais.push(getComputedStyle(el).overflowX !== 'visible' ? exc(el) : 0)
  return { pagina: exc(document.documentElement), lista: Math.max(0, ...ancestrais), tabela: tabela ? tabela.getBoundingClientRect().width - tabela.parentElement.clientWidth : 0 }
})
// Sobreposição na linha: conteúdo saindo da célula, célula cobrindo a vizinha, ou dois blocos irmãos dentro da célula se cobrindo.
const sobreposicoes = (page) => page.evaluate(() => {
  const r = (el) => el.getBoundingClientRect()
  const problemas = []
  const cobre = (a, b) => Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1
  document.querySelectorAll('tbody tr').forEach((tr, i) => {
    const tds = [...tr.children].filter((td) => td.getClientRects().length)
    tds.forEach((td, j) => {
      const caixa = r(td)
      for (const el of td.querySelectorAll('*')) {
        const c = r(el)
        if (!c.width || !c.height) continue
        if (c.left < caixa.left - 1 || c.right > caixa.right + 1) { problemas.push(`linha ${i + 1} célula ${j + 1}: ${el.tagName.toLowerCase()} sai da célula`); break }
      }
      if (j > 0 && cobre(r(tds[j - 1]), caixa)) problemas.push(`linha ${i + 1}: célula ${j} cobre a ${j + 1}`)
      for (const pai of [td, ...td.querySelectorAll('div, ul')]) {
        const filhos = [...pai.children].filter((f) => f.getClientRects().length && r(f).width && getComputedStyle(f).position !== 'absolute')
        for (let a = 0; a < filhos.length; a++) for (let b = a + 1; b < filhos.length; b++) if (cobre(r(filhos[a]), r(filhos[b]))) problemas.push(`linha ${i + 1} célula ${j + 1}: blocos irmãos se cobrem`)
      }
    })
  })
  return problemas
})
const larguraDasBarras = (page) => page.evaluate(() => [...document.querySelectorAll('tbody [class*="trilhoGasto"] i')].map((i) => i.getBoundingClientRect().width))

await Promise.all([1920, 1440, 390].map(async (largura) => {
  const largo = largura > 700
  await item(`${largura}: estágio vem da API, Em risco só com emRisco, tag interna oculta, sem rolagem nem sobreposição`, async () => {
    const { contexto, page } = await preparar(largura, comRelacao, { gastoTotalDaCarteira: 2000 })
    await linha(page, 5).waitFor()
    // A API disse fiel com 1 compra: a tela diz Fiel, não recalcula.
    assert.equal(await linha(page, 4).getByRole('img', { name: 'Estágio 4 de 4: Fiel' }).count(), 1, 'fiel com 1 compra aparece como Fiel')
    assert.equal(await linha(page, 2).getByRole('img', { name: 'Estágio 2 de 4: Primeira compra' }).count(), 1)
    assert.equal(await linha(page, 3).getByRole('img', { name: 'Estágio 3 de 4: Recorrente' }).count(), 1)
    assert.equal(await page.getByText('Em risco', { exact: true }).count(), 1, 'Em risco só em quem a API marcou')
    assert.equal(await linha(page, 3).getByText('Em risco', { exact: true }).count(), 1)
    assert.equal(await linha(page, 4).getByText('Em risco', { exact: true }).count(), 0, 'fiel sem emRisco não ganha o alerta')
    assert.equal(await page.getByText('recompra_convidada').count(), 0, 'tag interna oculta')
    for (const botao of ['Abrir ficha de', 'Novo pedido de']) assert.equal(await page.getByRole('button', { name: `${botao} ${nomeDe(3)}` }).count(), 1, `ação ${botao} segue`)
    if (largo) {
      assert.equal(await linha(page, 1).getByText('Sem pedido ainda', { exact: true }).count(), 1, 'selo de sem pedido segue na linha')
      const tres = await linha(page, 3).innerText()
      assert.match(tres, /Reclamação/, 'sinal de reclamação aberta')
      assert.match(tres, /3 notas/, 'sinal de notas')
      const cinco = await linha(page, 5).innerText()
      assert.match(cinco, /R\$\s*1\.020,00/, 'gasto')
      assert.match(cinco, /R\$\s*85,00/, 'ticket')
      assert.match(cinco, /há \d+ dias?|hoje/, 'última compra')
      assert.match(await linha(page, 4).innerText(), /25% da carteira/, 'gasto 500 de 2000')
      assert.equal(await linha(page, 5).locator('ul li').count(), 3, 'até 3 tags')
      assert.equal(await linha(page, 3).locator('ul li').count(), 2, 'a tag interna não ocupa vaga')
    }
    const m = await rolagem(page)
    assert.ok(m.pagina <= 0, `página sem rolagem horizontal (${m.pagina}px)`)
    if (largo) assert.ok(m.lista <= 0 && m.tabela <= 1, `lista sem rolagem horizontal (${m.lista}px, tabela ${m.tabela}px)`)
    if (largo) assert.deepEqual(await sobreposicoes(page), [], 'blocos da linha sem sobreposição')
    if (largo) console.log(`      altura da linha Todos em ${largura}: ${Math.round((await linha(page, 3).boundingBox()).height)} px`)
    await page.screenshot({ path: `${pasta}/clientes-${largura}.png` })
    await contexto.close()
  })
  if (largo) await item(`${largura}: barra de gasto proporcional e em 0 quando a carteira é 0`, async () => {
    let c = await preparar(largura, comRelacao, { gastoTotalDaCarteira: 2000 })
    await linha(c.page, 5).waitFor()
    const barras = await larguraDasBarras(c.page)
    assert.ok(barras.length > 0 && barras.some((w) => w > 0), 'com carteira, há barra com largura')
    await c.contexto.close()
    c = await preparar(largura, carteiraZero, { gastoTotalDaCarteira: 0 })
    await linha(c.page, 1).waitFor()
    assert.ok((await larguraDasBarras(c.page)).every((w) => w === 0), 'carteira 0: barra de gasto em 0')
    assert.doesNotMatch(await c.page.locator('tbody').innerText(), /NaN|Infinity|undefined/, 'sem lixo numérico')
    assert.deepEqual(await sobreposicoes(c.page), [], 'sem sobreposição com carteira 0')
    await c.contexto.close()
  })
  for (const [aba, rotulo] of [['Sem pedido', 'Leads'], ['Recompra', 'Recompra']]) await item(`${largura}: aba ${rotulo} sem rolagem horizontal e com cada filho na própria célula`, async () => {
    const { contexto, page } = await preparar(largura, comRelacao, { gastoTotalDaCarteira: 2000 })
    await linha(page, 5).waitFor()
    await page.getByRole('tab', { name: aba, exact: true }).click()
    await page.getByRole('tab', { name: aba, exact: true, selected: true }).waitFor()
    await linha(page, 5).waitFor()
    if (aba === 'Recompra') assert.ok(await page.getByRole('link', { name: /^Chamar .* no WhatsApp$/ }).count() > 0, 'convite de recompra presente')
    const m = await rolagem(page)
    assert.ok(m.pagina <= 0, `página sem rolagem horizontal (${m.pagina}px)`)
    if (largo) {
      assert.ok(m.lista <= 0 && m.tabela <= 1, `lista sem rolagem horizontal (${m.lista}px, tabela ${m.tabela}px)`)
      assert.deepEqual(await sobreposicoes(page), [], 'filhos dentro da própria célula, sem sobreposição')
      console.log(`      altura da linha ${rotulo} em ${largura}: ${Math.round((await linha(page, 3).boundingBox()).height)} px`)
    } else {
      // Em 390 a célula de ações também não deixa o link sair.
      assert.deepEqual(await sobreposicoes(page), [], 'filhos dentro da própria célula em 390')
    }
    await page.screenshot({ path: `${pasta}/${rotulo.toLowerCase()}-${largura}.png` })
    await contexto.close()
  })
  await item(`${largura}: API antiga (sem os campos novos) mostra neutro, mantém as ações e não quebra`, async () => {
    const antes = erros.length
    const { contexto, page } = await preparar(largura, semRelacao)
    await linha(page, 5).waitFor()
    assert.equal(await page.getByText('Em risco', { exact: true }).count(), 0)
    assert.equal(await page.getByRole('img', { name: /^Estágio/ }).count(), 0, 'sem estágio inventado')
    assert.equal(await page.getByRole('button', { name: `Abrir ficha de ${nomeDe(3)}` }).count(), 1, 'ação segue')
    if (largo) assert.ok(await linha(page, 3).getByRole('img', { name: 'Sem dado' }).count() >= 5, 'traço no lugar do dado ausente')
    assert.doesNotMatch(await page.locator('tbody').innerText(), /NaN|Infinity|undefined|null/)
    assert.equal(erros.length, antes, 'sem erro de página')
    assert.ok((await rolagem(page)).pagina <= 0, 'página sem rolagem horizontal')
    if (largo) assert.deepEqual(await sobreposicoes(page), [], 'sem sobreposição')
    await contexto.close()
  })
  await item(`${largura}: carregando, vazio e erro`, async () => {
    let libera
    const portao = new Promise((r) => { libera = r })
    let c = await preparar(largura, { portao, clientes: comRelacao })
    await c.page.getByText('Carregando cadastros…').first().waitFor()
    assert.equal(await c.page.locator('tbody tr').count(), 0, 'carregando não mostra linha')
    libera()
    await linha(c.page, 1).waitFor()
    await c.contexto.close()
    c = await preparar(largura, [])
    await c.page.getByText('Nenhuma pessoa cadastrada', { exact: true }).waitFor()
    assert.ok((await rolagem(c.page)).pagina <= 0, 'vazio sem rolagem horizontal')
    await c.contexto.close()
    c = await preparar(largura, 'erro')
    await c.page.getByRole('alert').first().waitFor()
    assert.ok((await rolagem(c.page)).pagina <= 0, 'erro sem rolagem horizontal')
    await c.contexto.close()
  })
}))
await browser.close()
if (erros.length) falhas.push(`erros de página: ${erros.slice(0, 3).join(' | ')}`)
if (falhas.length) { console.log(`\n${falhas.length} falha(s)`); process.exit(1) }
console.log('\nLinha de relação do cliente aprovada em 1920, 1440 e 390.')
