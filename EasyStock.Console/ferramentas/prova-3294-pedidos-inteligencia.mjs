// #3294: Cadastros > Pedidos com inteligência de backoffice. App real com HTTP controlado e dados fictícios.
// Faixa de indicadores (GET api/pedidos/resumo com os filtros da lista), linha com itens, selo da compra do cliente,
// tempo na situação, atraso e saldo. Prints em 1920, 1440 e 390; sem rolagem horizontal na mesa, página sem rolar de lado no celular.
// Uso: CONSOLE_TEST_URL=http://127.0.0.1:5880 node ferramentas/prova-3294-pedidos-inteligencia.mjs [rotulo]
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const rotulo = process.argv[2] ?? 'depois'
const pasta = resolve(process.env.PASTA_PRINTS_3294 ?? resolve(import.meta.dirname, '../.build/3294-pedidos'))
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const SO_PRINTS = rotulo === 'antes'

const agora = Date.now()
const min = 60000, hora = 60 * min, dia = 24 * hora
const iso = (ms) => new Date(ms).toISOString()
const NOMES = ['Ana Paula Ribeiro', 'Bruno Lima', 'Carla Souza', 'Diego Alves', 'Elisa Prado', 'Fábio Nunes', 'Gabriela Torres']
const ITENS = ['Bolo de cenoura com chocolate', 'Torta de limão', 'Quiche de alho-poró', 'Pão de mel', 'Lasanha de berinjela', 'Brownie', 'Cuca de banana']
const ORIGENS = ['whatsapp', 'storefront', 'balcao', 'whatsapp', 'console']
// [status, minutos atrás (criado), alterado há (min), agendado em relação a agora (min, null = para agora), total, pago, compra do cliente]
const ROTEIRO = [
  ['aguardando_pagamento', 12, 12, null, 86, 0, null],
  ['aguardando_aprovacao_baba', 25, 25, 240, 142, 0, null],
  ['preparando', 48, 30, null, 64.5, 64.5, 3],
  ['aguardando', 70, 70, -40, 120, 120, 1],
  ['pronto', 95, 15, null, 38, 0, 7],
  ['saiu_para_entrega', 130, 20, null, 210, 210, 2],
  ['entregue', 300, 200, null, 75, 75, 5],
  ['entregue', 1500, 1400, null, 54, 54, 1],
  ['cancelado', 1700, 1650, null, 60, 0, null],
  ['entregue', 2900, 2800, null, 180, 180, 12],
  ['aguardando', 2 * 1440 + 30, 2 * 1440, -90, 48, 0, 4],
  ['entregue', 3 * 1440, 3 * 1440 - 60, null, 99.9, 99.9, 2],
]
const pedidos = ROTEIRO.map(([status, criado, alterado, agendado, total, pago, compra], i) => ({
  id: `pedido-${i + 1}`, numeroDoDia: i + 1, clienteId: `cli-${i % NOMES.length}`, clienteNome: NOMES[i % NOMES.length], clienteTelefone: `1199999${String(1000 + i)}`,
  status, total, totalPago: pago, aReceber: ['cancelado', 'rascunho'].includes(status) ? 0 : Math.max(0, total - pago), origem: ORIGENS[i % ORIGENS.length],
  criadoEm: iso(agora - criado * min), alteradoEm: iso(agora - alterado * min), itensCount: 1 + (i % 4),
  ...(agendado !== null ? { agendadoParaEm: iso(agora + agendado * min) } : {}),
  ...(status === 'entregue' ? { entreguEm: iso(agora - alterado * min) } : {}),
  ...(status === 'cancelado' ? { canceladoEm: iso(agora - alterado * min) } : {}),
  itensResumo: [ITENS[i % ITENS.length], ...(i % 4 ? [ITENS[(i + 3) % ITENS.length]] : [])].map((nome, j) => ({ nome, quantidade: j + 1 + (i % 2), unidade: 'un' })),
  compraDoCliente: compra, situacaoDesde: iso(agora - alterado * min),
  atrasado: agendado !== null && agendado < 0 && !['entregue', 'cancelado', 'rascunho'].includes(status),
}))
const resumo = {
  pedidos: 134, vendas: 118, vendido: 9842.5, ticketMedio: 83.41,
  aReceber: { valor: 312.5, pedidos: 4 }, aguardandoPagamento: 2, aguardandoAprovacao: 1,
  emAndamento: 6, atrasados: 2, cancelados: { pedidos: 7, valor: 486 },
  primeiraCompra: 31, recompra: 87,
  maisVendidos: [{ nome: 'Quiche de alho-poró', quantidade: 41 }, { nome: 'Torta de limão', quantidade: 33 }, { nome: 'Brownie', quantidade: 2.5 }],
  anterior: { desde: iso(agora - 14 * dia), ate: iso(agora - 7 * dia), pedidos: 112, vendido: 8120 },
}

const browser = await chromium.launch({ headless: true })
const erros = []
const falhas = []
const item = async (nome, corpo) => {
  try { await corpo(); console.log(`ok    ${nome}`) } catch (e) { const motivo = e.message.split('\n').slice(0, 4).join(' | '); falhas.push(`${nome}: ${motivo}`); console.log(`FALHA ${nome}: ${motivo}`) }
}

async function preparar(largura, { semResumo = false, escuro = false } = {}) {
  const movel = largura < 700
  const consultas = { lista: [], resumo: [] }
  const contexto = await browser.newContext({ viewport: { width: largura, height: movel ? 844 : 1080 }, isMobile: movel, hasTouch: movel, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce', colorScheme: escuro ? 'dark' : 'light' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja' }, usuario: { id: 'dona', nome: 'Dona de teste' } })))
  const acoes = Object.fromEntries(['verPedidos', 'cancelarPedido', 'atenderConversas', 'verCadastrosClientes', 'verCadastrosProdutos'].map((n) => [n, true]))
  await contexto.route('**/api/**', async (route) => {
    const url = new URL(route.request().url()), path = url.pathname
    if (!path.startsWith('/api/')) return route.continue()
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros'].map((id) => ({ id, liberado: true })), acoes })
    if (path === '/api/pedidos/resumo') {
      consultas.resumo.push(Object.fromEntries(url.searchParams))
      return semResumo ? route.fulfill({ status: 503, json: { error: { message: 'Resumo indisponível no teste.' } } }) : ok(resumo)
    }
    if (path === '/api/pedidos') {
      const q = Object.fromEntries(url.searchParams)
      consultas.lista.push(q)
      const situacoes = q.status ? q.status.split(',') : null
      const filtrados = pedidos.filter((p) => !situacoes || situacoes.includes(p.status))
      return ok(filtrados, { meta: { totalCount: situacoes ? filtrados.length : 134 } })
    }
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
    if (path === '/api/notificacoes/badge') return ok({ count: 0 })
    return ok([], { meta: { totalCount: 0 } })
  })
  const page = await contexto.newPage()
  page.setDefaultTimeout(15000)
  page.on('pageerror', (e) => erros.push(e.message))
  await page.goto(`${URL_CONSOLE}/#/m/cadastros/pedidos`)
  await page.locator('tbody tr').first().waitFor()
  return { contexto, page, consultas }
}
const rolagem = (page) => page.evaluate(() => {
  const tabela = document.querySelector('tbody')?.closest('table')
  return { pagina: document.documentElement.scrollWidth - document.documentElement.clientWidth, tabela: tabela ? Math.round(tabela.getBoundingClientRect().width - tabela.parentElement.clientWidth) : 0 }
})

for (const largura of [1920, 1440, 390]) {
  const { contexto, page, consultas } = await preparar(largura)
  await page.waitForTimeout(400)
  await page.screenshot({ path: `${pasta}/${rotulo}-${largura}.png`, fullPage: true })
  if (!SO_PRINTS) {
    const painel = page.getByRole('region', { name: 'Pedidos', exact: true })
    await item(`${largura}: sem rolagem horizontal`, async () => {
      const r = await rolagem(page)
      assert.ok(r.pagina <= 0, `página rola ${r.pagina}px de lado`)
      if (largura >= 1440) assert.ok(r.tabela <= 0, `tabela passa ${r.tabela}px`)
    })
    await item(`${largura}: indicadores com os números do resumo`, async () => {
      const faixa = painel.getByRole('region', { name: 'Indicadores dos pedidos' })
      if (largura < 700) {
        // No celular a faixa começa recolhida numa linha e a lista aparece na primeira tela.
        await faixa.getByText(/^Indicadores: R\$\s9\.842,50 vendido/).waitFor()
        const topoDaLista = await page.locator('tbody tr').first().boundingBox()
        assert.ok(topoDaLista && topoDaLista.y < 844, `primeira linha em y=${topoDaLista?.y}, fora da primeira tela`)
        await page.screenshot({ path: `${pasta}/${rotulo}-390-recolhida.png` })
        await faixa.locator('summary').click()
      }
      await faixa.getByText('R$ 9.842,50', { exact: true }).waitFor()
      await faixa.getByText('R$ 83,41').first().waitFor()
      await faixa.getByText('R$ 312,50', { exact: true }).waitFor()
      await faixa.getByText('74%').waitFor()
      await faixa.getByText(/Mais pedidos: 41× Quiche de alho-poró, 33× Torta de limão, 2,5× Brownie/).waitFor()
      await faixa.getByText(/Vendido e ticket contam só compras/).waitFor()
      assert.equal(await faixa.getByText(/sobre os 7 dias anteriores/).count(), 0, 'sem período fechado não compara')
    })
    await item(`${largura}: o resumo segue os filtros da lista`, async () => {
      const ultimaLista = consultas.lista.at(-1), ultimoResumo = consultas.resumo.at(-1)
      for (const campo of ['empresaId', 'desde', 'search', 'entregaDesde', 'entregaAte']) assert.equal(ultimoResumo?.[campo], ultimaLista?.[campo], campo)
      for (const campo of ['page', 'pageSize', 'status', 'sort', 'order']) assert.equal(ultimoResumo[campo], undefined, `resumo sem ${campo}`)
    })
    if (largura === 1920) {
      await item('linha: itens, selo da compra, tempo na situação e atraso', async () => {
        const linhas = page.locator('tbody tr')
        await linhas.filter({ hasText: 'Nº 004' }).getByText('Atrasado').locator('visible=true').waitFor()
        await linhas.filter({ hasText: 'Nº 004' }).getByText('1ª compra').locator('visible=true').waitFor()
        await linhas.filter({ hasText: 'Nº 010' }).getByText('12ª compra').locator('visible=true').waitFor()
        await linhas.filter({ hasText: 'Nº 001' }).getByText('1× Bolo de cenoura com chocolate').waitFor()
        await linhas.filter({ hasText: 'Nº 004' }).getByText('+2', { exact: true }).waitFor()
        await linhas.filter({ hasText: 'Nº 003' }).getByText('há 30 min').waitFor()
        await linhas.filter({ hasText: 'Nº 011' }).getByText('Atrasado').locator('visible=true').waitFor()
        assert.equal(await linhas.filter({ hasText: 'Nº 007' }).getByText(/^há /).count(), 0, 'entregue não conta tempo')
      })
      await item('cancelar mora no menu do pedido, com o motivo só ao abrir', async () => {
        assert.equal(await page.getByText('Já foi entregue').count(), 0, 'motivo não se repete nas linhas')
        await page.getByRole('button', { name: /^Mais opções do Pedido Nº 007/ }).click()
        const cancelar = page.getByRole('menuitem', { name: 'Cancelar pedido', exact: true })
        assert.equal(await cancelar.isDisabled(), true)
        await page.getByText('Já foi entregue').click()
        await page.waitForTimeout(300)
        assert.equal(await page.getByRole('dialog').count(), 0, 'clicar no motivo do menu não abre o detalhe')
        await page.keyboard.press('Escape')
      })
      await item('indicador "aguardando pagamento" filtra a lista e o resumo não muda', async () => {
        const antes = consultas.resumo.length
        const faixa = painel.getByRole('region', { name: 'Indicadores dos pedidos' })
        await faixa.getByRole('button', { name: /aguardando pagamento/ }).click()
        await page.waitForFunction(() => document.querySelectorAll('tbody tr').length === 1)
        assert.equal(consultas.lista.at(-1).status, 'aguardando_pagamento')
        assert.equal(await faixa.getByRole('button', { name: /aguardando pagamento/ }).getAttribute('aria-pressed'), 'true')
        assert.equal(consultas.resumo.length, antes, 'situação não reconsulta o resumo')
        await faixa.getByRole('button', { name: /aguardando pagamento/ }).click()
        await page.waitForFunction(() => document.querySelectorAll('tbody tr').length > 1)
      })
      await item('período fechado compara com a janela anterior', async () => {
        await painel.getByRole('combobox', { name: 'Feito em' }).click()
        await page.getByRole('option', { name: 'Últimos 7 dias', exact: true }).click()
        const faixa = painel.getByRole('region', { name: 'Indicadores dos pedidos' })
        await faixa.getByText(/\+21% sobre os 7 dias anteriores/).waitFor()
        assert.ok(consultas.resumo.at(-1).desde, 'resumo com desde')
        await page.screenshot({ path: `${pasta}/${rotulo}-1920-7dias.png`, fullPage: true })
      })
    }
  }
  await contexto.close()
}
if (!SO_PRINTS) {
  const { contexto, page } = await preparar(1440, { semResumo: true })
  await item('resumo fora do ar não derruba a lista', async () => {
    await page.getByText('Os indicadores não carregaram.').waitFor()
    assert.ok(await page.locator('tbody tr').count() > 0)
  })
  await contexto.close()
  const escuro = await preparar(1440, { escuro: true })
  await escuro.page.waitForTimeout(300)
  await escuro.page.screenshot({ path: `${pasta}/${rotulo}-1440-escuro.png`, fullPage: true })
  await escuro.contexto.close()
}
await browser.close()
if (erros.length) falhas.push(`erros da página: ${erros.join(' | ')}`)
console.log(falhas.length ? `\n${falhas.length} falha(s)` : '\nTudo certo')
process.exit(falhas.length ? 1 : 0)
