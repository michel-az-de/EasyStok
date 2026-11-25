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
// Tag longa como a API manda (normalizada): na coluna estreita ela é cortada com reticências e o title traz o rótulo inteiro.
const TAG_LONGA = 'cliente_indicado_pela_feira_de_artesanato_do_bairro'
const REL = [
  { ...base, estagio: 'sem_pedido', criadoEm: '2026-10-05T15:00:00Z', gastoTotal: 0, compras: 0, ticketMedio: null, primeiraCompraEm: null, ultimaCompraEm: null },
  { ...base, estagio: 'primeira_compra', gastoTotal: 0.4, compras: 1, ticketMedio: 0.4, primeiraCompraEm: '2026-11-15T15:00:00Z', ultimaCompraEm: '2026-11-15T15:00:00Z', notasTotal: 1, ultimaNota: 'Pede entrega depois das 18h.', tags: ['instagram'] },
  { ...base, estagio: 'recorrente', criadoEm: '2025-12-20T15:00:00Z', gastoTotal: 444, compras: 6, ticketMedio: 74, primeiraCompraEm: '2026-01-10T15:00:00Z', ultimaCompraEm: '2026-09-20T15:00:00Z', ocorrenciasAbertas: 1, ocorrenciasTotal: 2, notasTotal: 3, ultimaNota: 'Ligar antes de repor.', tags: ['whatsapp', 'restaurante', 'recompra_convidada'], emRisco: true },
  { ...base, estagio: 'fiel', gastoTotal: 500, compras: 1, ticketMedio: 500, primeiraCompraEm: '2026-11-18T15:00:00Z', ultimaCompraEm: '2026-11-18T15:00:00Z' },
  { ...base, estagio: 'fiel', gastoTotal: 1020, compras: 12, ticketMedio: 85, primeiraCompraEm: '2025-06-10T15:00:00Z', ultimaCompraEm: '2026-11-20T15:00:00Z', tags: ['cliente_vip', TAG_LONGA, 'evento', 'atacado'] },
  // 6: ticket de 4 dígitos e gasto de 5 dígitos (o texto não pode sair da célula). 7: inativo, com relação.
  { ...base, estagio: 'fiel', gastoTotal: 12345.67, compras: 12, ticketMedio: 1028.81, primeiraCompraEm: '2025-03-10T15:00:00Z', ultimaCompraEm: '2026-11-21T15:00:00Z' },
  { ...base, estagio: 'recorrente', gastoTotal: 300, compras: 3, ticketMedio: 100, primeiraCompraEm: '2026-02-10T15:00:00Z', ultimaCompraEm: '2026-08-21T15:00:00Z', ativo: false },
]
const cliente = (n, extra = {}) => ({ id: idDe(n), revisao: 1, nome: nomeDe(n), telefone: `1191111${String(1000 + n)}`, email: '', documento: '', tipoPessoa: 'fisica', ativo: true, orderCount: 0, ...extra })
// orderCount conta todo pedido criado (aqui 2 a mais que as compras): com a relação, a linha escreve as compras.
// Cliente 1 sem telefone e e-mail; cliente 5 com os dois.
const comRelacao = REL.map((r, i) => cliente(i + 1, { ...r, orderCount: r.compras + 2, ...(i === 0 ? { telefone: '' } : {}), ...(i === 4 ? { email: 'ficticio05@exemplo.test' } : {}) }))
const semRelacao = REL.map((_, i) => cliente(i + 1, i === 2 ? { orderCount: 3 } : i === 6 ? { ativo: false } : {}))
// Carteira 0 com clientes que têm compras (dado incoerente, mas a tela não pode quebrar nem inventar percentual).
const carteiraZero = REL.map((r, i) => cliente(i + 1, { ...r, orderCount: r.compras }))
const browser = await chromium.launch({ headless: true })
const erros = []
const falhas = []
const CARTEIRA = 50000

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
  // Como na 3241: a subida do app no Vite frio, com o CI carregado, pode passar do teto das asserções.
  page.setDefaultNavigationTimeout(60000)
  page.on('pageerror', (e) => erros.push(e.message))
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
// Texto que sai da célula: o conteúdo de cada td (e de cada filho direto) não pode ser mais largo que a célula.
const transbordoDeTexto = (page) => page.evaluate(() => {
  const problemas = []
  document.querySelectorAll('tbody tr').forEach((tr, i) => [...tr.children].filter((td) => td.getClientRects().length).forEach((td, j) => {
    if (td.scrollWidth > td.clientWidth) problemas.push(`linha ${i + 1} célula ${j + 1}: texto ${td.scrollWidth - td.clientWidth}px além da célula (${td.textContent.trim().slice(0, 30)})`)
  }))
  return problemas
})
// A tag longa da linha 5: reticência computada, de fato cortada, com o rótulo inteiro no title, e a célula sem transbordo.
const tagLonga = (page) => linha(page, 5).locator('ul li').filter({ hasText: /^cliente indicado/ }).evaluate((li) => {
  const td = li.closest('td')
  return { reticencia: getComputedStyle(li).textOverflow, cortada: li.scrollWidth > li.clientWidth, title: li.title, transbordoDaCelula: td.scrollWidth - td.clientWidth }
})
const confereTagLonga = async (page) => {
  const t = await tagLonga(page)
  assert.equal(t.reticencia, 'ellipsis', 'tag longa com text-overflow ellipsis')
  assert.ok(t.cortada, 'tag longa de fato cortada (scrollWidth > clientWidth)')
  assert.equal(t.title, TAG_LONGA.replace(/_/g, ' '), 'title com o rótulo inteiro')
  assert.ok(t.transbordoDaCelula <= 0, `célula da tag sem transbordo (${t.transbordoDaCelula}px)`)
}
const larguraDasBarras = (page) => page.evaluate(() => [...document.querySelectorAll('tbody [class*="trilhoGasto"] i')].map((i) => i.getBoundingClientRect().width))

// As larguras rodam em paralelo; os itens de cada largura em série (em paralelo, a carga fria do servidor de desenvolvimento estoura o tempo da página).
await Promise.all([1920, 1440, 390].map(async (largura) => {
  const largo = largura > 700
  await item(`${largura}: estágio vem da API, Em risco só com emRisco, tag interna oculta, sem rolagem nem sobreposição`, async () => {
    const { contexto, page } = await preparar(largura, comRelacao, { gastoTotalDaCarteira: CARTEIRA })
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
      // Mudou de propósito: com a relação, o selo "Sem pedido ainda" repetia o estágio e saiu da linha.
      assert.equal(await linha(page, 1).getByText('Sem pedido ainda', { exact: true }).count(), 0, 'sem selo de situação com a relação')
      assert.equal(await linha(page, 3).getByText('Cliente', { exact: true }).count(), 0, 'sem selo Cliente com a relação')
      assert.equal(await linha(page, 1).getByText('Sem pedido', { exact: true }).count(), 1, 'o estágio Sem pedido segue')
      assert.match(await linha(page, 3).innerText(), /6 compras/, 'compras, não pedidos criados')
      assert.match(await linha(page, 4).innerText(), /\b1 compra\b/, 'singular')
      assert.doesNotMatch(await linha(page, 3).innerText(), /[0-9]+ pedidos?/, 'sem "N pedidos" com a relação')
      assert.match(await linha(page, 3).innerText(), /dezembro de 2025/, 'cliente desde = cadastro')
      assert.doesNotMatch(await linha(page, 3).innerText(), /janeiro de 2026/, 'não usa a primeira compra')
      assert.match(await linha(page, 1).innerText(), /outubro de 2026/, 'lead também tem cliente desde')
      assert.match(await linha(page, 1).innerText(), /Contato não informado/, 'sem telefone nem e-mail')
      assert.match(await linha(page, 5).innerText(), /ficticio05@exemplo\.test/, 'e-mail secundário na célula do cliente')
      assert.equal(await page.getByTitle('Comprou 2 vezes ou mais e está há 30 dias sem pedir.').count(), 1, 'regra do Em risco')
      assert.equal(await linha(page, 3).getByTitle('Ligar antes de repor.').count(), 1, 'última nota no title')
      assert.doesNotMatch(await page.locator('tbody').innerText(), /\u2014/, 'sem travessão')
      assert.match(await linha(page, 5).innerText(), /cliente vip/, 'tag legível')
      assert.equal(await page.getByText('cliente_vip').count(), 0, 'sem tag crua')
      assert.match(await linha(page, 3).innerText(), /whatsapp/, 'tag normalizada')
      const tres = await linha(page, 3).innerText()
      assert.match(tres, /Reclamação/, 'sinal de reclamação aberta')
      assert.match(tres, /3 notas/, 'sinal de notas')
      const cinco = await linha(page, 5).innerText()
      assert.match(cinco, /R\$\s*1\.020(?!,)/, 'gasto')
      assert.match(cinco, /R\$\s*85(?!,)/, 'ticket')
      assert.match(cinco, /R\$\u00a0\d/, 'espaço não separável entre R$ e o número')
      const dois = await linha(page, 2).innerText()
      assert.match(dois, /R\$\u00a00,40/, 'ticket de R$ 0,40 com centavos, não R$ 0')
      assert.doesNotMatch(dois, /R\$[\s\u00a0]*0(?!,)/, 'sem R$ 0 seco')
      if (largura === 1440) await confereTagLonga(page)
      assert.match(cinco, /há \d+ dias?|hoje/, 'última compra')
      assert.match(await linha(page, 4).innerText(), /1% da carteira/, 'gasto 500 de 50000')
      assert.equal(await linha(page, 5).locator('ul li').count(), 3, 'até 3 tags')
      assert.equal(await linha(page, 3).locator('ul li').count(), 2, 'a tag interna não aparece e as 2 tags restantes ficam (a API corta em 3 antes do filtro)')
    }
    const m = await rolagem(page)
    assert.ok(m.pagina <= 0, `página sem rolagem horizontal (${m.pagina}px)`)
    if (largo) assert.ok(m.lista <= 0 && m.tabela <= 1, `lista sem rolagem horizontal (${m.lista}px, tabela ${m.tabela}px)`)
    if (largo) assert.deepEqual(await sobreposicoes(page), [], 'blocos da linha sem sobreposição')
    if (largo) assert.deepEqual(await transbordoDeTexto(page), [], 'texto de cada célula cabe nela')
    if (largo) {
      const quatro = await linha(page, 6).innerText()
      assert.match(quatro, /R\$\s*12\.346/, 'gasto de 5 dígitos em reais inteiros')
      assert.match(quatro, /R\$\s*1\.029/, 'ticket de 4 dígitos em reais inteiros')
      assert.match(await linha(page, 7).innerText(), /Inativo/, 'inativo com relação mostra o selo Inativo')
      assert.equal(await linha(page, 3).getByText('Inativo', { exact: true }).count(), 0, 'ativo não mostra Inativo')
    }
    if (largo) console.log(`      altura da linha Todos em ${largura}: ${Math.round((await linha(page, 3).boundingBox()).height)} px`)
    await page.screenshot({ path: `${pasta}/clientes-${largura}.png` })
    await contexto.close()
  })
  if (largo) await item(`${largura}: barra de gasto sempre desenhada com a relação, em 0 sem gasto ou com carteira 0`, async () => {
    let c = await preparar(largura, comRelacao, { gastoTotalDaCarteira: CARTEIRA })
    await linha(c.page, 5).waitFor()
    const barras = await larguraDasBarras(c.page)
    assert.equal(barras.length, REL.length, 'uma barra por linha')
    assert.equal(barras[0], 0, 'cliente sem gasto: barra em 0')
    assert.ok(barras.slice(1).every((w) => w > 0), 'quem gastou tem barra com largura')
    assert.match(await linha(c.page, 1).innerText(), /Sem gasto ainda/, 'gasto 0 escreve Sem gasto ainda')
    assert.doesNotMatch(await linha(c.page, 1).innerText(), /R\$\s*0,00|0%/, 'gasto 0 sem R$ 0,00 nem 0%')
    await c.contexto.close()
    c = await preparar(largura, carteiraZero, { gastoTotalDaCarteira: 0 })
    await linha(c.page, 1).waitFor()
    const zero = await larguraDasBarras(c.page)
    assert.equal(zero.length, REL.length, 'carteira 0: a barra continua desenhada, uma por linha')
    assert.ok(zero.length > 0 && zero.every((w) => w === 0), 'carteira 0: barra de gasto em 0')
    const corpo = await c.page.locator('tbody').innerText()
    assert.doesNotMatch(corpo, /da carteira/, 'carteira 0: sem percentual inventado')
    assert.match(await linha(c.page, 5).innerText(), /R\$\s*1\.020(?!,)/, 'carteira 0: o gasto de quem comprou segue na linha')
    assert.doesNotMatch(corpo, /NaN|Infinity|undefined/, 'sem lixo numérico')
    assert.deepEqual(await sobreposicoes(c.page), [], 'sem sobreposição com carteira 0')
    await c.contexto.close()
    c = await preparar(largura, semRelacao)
    await linha(c.page, 5).waitFor()
    assert.equal((await larguraDasBarras(c.page)).length, 0, 'API antiga (carteira null): sem barra')
    await c.contexto.close()
  })
  for (const [aba, rotulo] of [['Sem pedido', 'Leads'], ['Recompra', 'Recompra']]) await item(`${largura}: aba ${rotulo} sem rolagem horizontal e com cada filho na própria célula`, async () => {
    const { contexto, page } = await preparar(largura, comRelacao, { gastoTotalDaCarteira: CARTEIRA })
    await linha(page, 5).waitFor()
    await page.getByRole('tab', { name: aba, exact: true }).click()
    await page.getByRole('tab', { name: aba, exact: true, selected: true }).waitFor()
    await linha(page, 5).waitFor()
    if (aba === 'Recompra') {
      assert.ok(await page.getByRole('link', { name: /^Chamar .* no WhatsApp$/ }).count() > 0, 'convite de recompra presente')
      if (largura === 1440) await confereTagLonga(page)
      const tres = await linha(page, 3).innerText()
      assert.match(tres, /(?<![0-9])6 compras(?![a-z])/, 'Recompra escreve as compras da relação')
      assert.doesNotMatch(tres, /[0-9]+ pedidos?|último há/, 'Recompra sem "N pedidos" nem "último há" ao lado de ticket e última compra')
    }
    const m = await rolagem(page)
    assert.ok(m.pagina <= 0, `página sem rolagem horizontal (${m.pagina}px)`)
    if (largo) {
      assert.ok(m.lista <= 0 && m.tabela <= 1, `lista sem rolagem horizontal (${m.lista}px, tabela ${m.tabela}px)`)
      assert.deepEqual(await sobreposicoes(page), [], 'filhos dentro da própria célula, sem sobreposição')
      assert.deepEqual(await transbordoDeTexto(page), [], 'texto de cada célula cabe nela')
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
    // #3281: 4 células de relação (estágio, gasto, última compra, sinais), um traço em cada.
    if (largo) assert.equal(await linha(page, 3).getByRole('img', { name: 'Sem dado' }).count(), 4, 'traço no lugar do dado ausente')
    if (largo) assert.equal(await linha(page, 3).getByRole('img', { name: 'Sem dado' }).first().textContent(), '', 'o traço é desenhado em CSS')
    assert.match(await linha(page, 3).innerText(), /3 pedidos/, 'API antiga segue com orderCount')
    assert.match(await linha(page, 7).innerText(), /Inativo/, 'API antiga segue com Inativo')
    assert.equal(await linha(page, 1).getByText('Sem pedido ainda', { exact: true }).count(), 1, 'API antiga segue com o selo de situação')
    assert.doesNotMatch(await page.locator('tbody').innerText(), /NaN|Infinity|undefined|null/)
    assert.equal(erros.length, antes, 'sem erro de página')
    assert.ok((await rolagem(page)).pagina <= 0, 'página sem rolagem horizontal')
    if (largo) assert.deepEqual(await sobreposicoes(page), [], 'sem sobreposição')
    await contexto.close()
  })
  await item(`${largura}: API antiga na aba Recompra segue com pedidos e último`, async () => {
    const { contexto, page } = await preparar(largura, semRelacao.map((c, i) => (i === 2 ? { ...c, lastOrderAt: '2026-09-20T15:00:00Z' } : c)))
    await linha(page, 3).waitFor()
    await page.getByRole('tab', { name: 'Recompra', exact: true }).click()
    await page.getByRole('tab', { name: 'Recompra', exact: true, selected: true }).waitFor()
    await linha(page, 3).waitFor()
    assert.match(await linha(page, 3).innerText(), /3 pedidos · último há \d+ dias/, 'sem relação, o resumo de hoje')
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
