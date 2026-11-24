// #3076: roteiro do dia a dia pelos controles reais, contra a API local semeada (SEED_DEMO_DATA=true, vitrine ativa,
// cardápio com "Lasanha" e janelas cadastradas). Mede cliques e digitações de cada tarefa e confere:
//   T1 cadastrar cliente com endereço: sem CEP o campo recusa; com CEP vira endereço Padrão na Ficha (C1) e o
//      "Cadastro salvo." some sozinho (C2);
//   T2 registrar pedido desse cliente: o passo Entrega já traz o endereço do cadastro e o Revisar libera o
//      Registrar sem pedir endereço nem celular de novo (P8).
// Opt-in, grava dados de teste:
//   CONSOLE_TEST_URL=http://127.0.0.1:5876 E2E_USUARIO=... E2E_SENHA=... node ferramentas/prova-3076-cadastro-pedido-navegador.mjs
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_TEST_URL
assert.ok(origem && process.env.E2E_USUARIO && process.env.E2E_SENHA, 'CONSOLE_TEST_URL, E2E_USUARIO e E2E_SENHA')
const pasta = resolve(import.meta.dirname, '../.build/3076')
mkdirSync(pasta, { recursive: true })
const sufixo = String(Date.now()).slice(-4)
const nome = `Prova ${sufixo}`
const celular = `1198765${sufixo}`

const browser = await chromium.launch({ headless: true })
const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })).newPage()
page.setDefaultTimeout(15000)
const erros = []
page.on('pageerror', (e) => erros.push(e.message))
page.on('response', (r) => { if (r.status() >= 500) erros.push(`${r.status()} ${r.url()}`) })
const conta = { cliques: 0, digitacoes: 0 }
const clicar = async (alvo) => { conta.cliques += 1; await alvo.click() }
const digitar = async (alvo, texto) => { conta.digitacoes += 1; await alvo.fill(texto) }
const medir = () => { const m = { ...conta }; conta.cliques = 0; conta.digitacoes = 0; return m }

await page.goto(origem + '/')
await page.locator('input[type=email]').first().fill(process.env.E2E_USUARIO)
await page.locator('input[type=password]').first().fill(process.env.E2E_SENHA)
await page.locator('button[type=submit]').first().click()
await page.getByRole('navigation').first().waitFor()
await page.goto(origem + '/#/m/cadastros/clientes')

// T1
await clicar(page.getByRole('button', { name: 'Novo cliente' }).first())
await digitar(page.getByLabel('Nome'), nome)
await digitar(page.getByLabel('Telefone'), celular)
await digitar(page.getByLabel('Endereço', { exact: true }), 'Rua das Flores, 100')
await clicar(page.getByRole('button', { name: 'Salvar cadastro' }))
await page.getByText(/CEP no fim/).first().waitFor()
await digitar(page.getByLabel('Endereço', { exact: true }), 'Rua das Flores, 100, Pinheiros, 05422-010')
await clicar(page.getByRole('button', { name: 'Salvar cadastro' }))
await page.getByText('Cadastro salvo.', { exact: true }).waitFor()
const t1 = medir()
await page.getByText('Cadastro salvo.', { exact: true }).waitFor({ state: 'detached', timeout: 8000 })
await page.getByRole('button', { name: `Abrir ficha de ${nome}` }).first().click()
// #3249: os endereços ficam na aba Endereços da ficha.
await page.getByRole('dialog', { name: 'Ficha do cliente', exact: true }).getByRole('tab', { name: /^Endereços/ }).click()
await page.getByText('Rua das Flores, 100', { exact: false }).first().waitFor()
assert.equal(await page.getByText('Nenhum endereço cadastrado.').count(), 0, 'C1: o endereço do cadastro aparece na Ficha')
await page.screenshot({ path: resolve(pasta, 't1-ficha.png') })
await page.getByRole('button', { name: 'Fechar', exact: true }).click()

// T2
await clicar(page.getByRole('button', { name: `Novo pedido de ${nome}` }).first())
await clicar(page.getByRole('button', { name: /^Somar Lasanha/ }).first())
await clicar(page.getByRole('button', { name: 'Continuar' }))
await clicar(page.locator('button', { hasText: /\d{2}:\d{2} às \d{2}:\d{2}/ }).first())
await page.waitForFunction(() => [...document.querySelectorAll('input')].some((i) => /05422-010/.test(i.value)))
await clicar(page.getByText(/^Link de pagamento/).first())
const frete = page.getByLabel('Frete combinado')
if (await frete.count()) { await digitar(frete, '0'); await frete.blur() }
await page.screenshot({ path: resolve(pasta, 't2-entrega.png') })
await clicar(page.getByRole('button', { name: 'Continuar' }))
await page.getByRole('button', { name: 'Registrar pedido' }).waitFor()
assert.equal(await page.getByText(/Falta cadastrar o endereço/).count(), 0, 'P8: o Revisar já conhece o endereço Padrão')
assert.equal(await page.getByText(/falta o celular/).count(), 0, 'P8: o Revisar já conhece o celular do cadastro')
await page.screenshot({ path: resolve(pasta, 't2-revisar.png') })
const t2 = medir()

assert.deepEqual(erros, [], 'sem erro de página nem 5xx')
console.log(JSON.stringify({ t1, t2, evidencias: pasta }))
await browser.close()
