// #3317: o foco do teclado no Atendimento, medido no Chromium em modo demonstração (1440x900, 2 temas).
// a depois de "Assumir conversa" o foco termina no campo de mensagem (antes: no body, e a digitação se perdia);
// b depois de enviar por clique o foco segue no campo (antes: no "Gravar áudio" que toma o lugar do Enviar);
// c depois de salvar uma nota (da ficha e do botão "Nota interna") o foco termina no campo (antes: no body);
// d no modal "Enviar e assumir esta conversa?" o foco começa no botão de confirmar, Enter confirma e o tooltip
//   "Fechar" do X não reaparece (#2857);
// e o anel de foco do campo tem contraste >= 3:1 sobre o fundo nos dois temas (WCAG 1.4.11; antes: 1,3:1 e 1,7:1);
// f Tab fecha o popover de filtros (papel dialog), aria-expanded volta a false e o foco não se perde;
// g com ponteiro de toque (tablet) o foco não é levado ao campo depois de Assumir (o teclado não abre sem ela pedir).
// PLAYWRIGHT_MODULE aponta para o runtime externo; sem ele usa o `playwright` do node_modules.
// O Vite do runner está no modo API; esta prova sobe o seu, em demonstração (como prova-3009).
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { join, resolve } from 'node:path'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pastaConsole = resolve(import.meta.dirname, '..')
let servidor = null
if (!process.env.CONSOLE_DEMO_URL) {
  delete process.env.VITE_FONTE_DADOS
  const { createServer } = await import('vite')
  servidor = await createServer({ root: pastaConsole, logLevel: 'silent', server: { host: '127.0.0.1', port: 0, strictPort: false } })
  await servidor.listen()
}
const origem = process.env.CONSOLE_DEMO_URL ?? servidor.resolvedUrls.local[0].replace(/\/$/, '')
const capturas = process.env.CAPTURAS_3317 ?? null
if (capturas) mkdirSync(capturas, { recursive: true })

const falhas = []
let passou = 0
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok ' + descricao) } catch (erro) {
    falhas.push(descricao); console.log(`FALHA ${descricao}\n ${String(erro.message).split('\n').slice(0, 4).join(' ')}`)
  }
}

const navegador = await chromium.launch({ headless: true })
const TEMAS = ['light', 'dark']

async function abrir(tema = 'light', largura = 1440, altura = 900, opcoes = {}) {
  const contexto = await navegador.newContext({ viewport: { width: largura, height: altura }, colorScheme: tema, reducedMotion: 'reduce', ...opcoes })
  const pagina = await contexto.newPage()
  await pagina.goto(`${origem}/#/m/atendimento/balcao`, { waitUntil: 'networkidle', timeout: 120_000 })
  await pagina.waitForSelector('[class*=contextoLinha]', { timeout: 30_000 })
  // A 3ª conversa da demonstração é atendida pelo Assistente: tem "Assumir conversa" e o modal de "Enviar e assumir".
  await pagina.locator('[class*=contextoLinha]').nth(2).click()
  await pagina.waitForSelector('textarea[aria-label], label:has-text("Mensagem para o cliente")', { state: 'attached', timeout: 10_000 }).catch(() => {})
  await pagina.waitForTimeout(500)
  return { contexto, pagina }
}
const foto = async (pagina, nome) => { if (capturas) await pagina.screenshot({ path: join(capturas, `${process.env.FASE_3317 ?? 'depois'}-${nome}.png`) }) }

// O que tem o foco agora, em uma frase para a mensagem de falha.
const quemTemOFoco = (pagina) => pagina.evaluate(() => {
  const a = document.activeElement
  if (!a) return 'nada'
  if (a === document.body) return 'o body (o foco se perdeu)'
  const nome = a.getAttribute('aria-label') || a.labels?.[0]?.textContent || (a.textContent ?? '').trim().slice(0, 30)
  return `${a.tagName.toLowerCase()} "${nome}"`
})
const focoNoCampo = (pagina) => pagina.evaluate(() => {
  const a = document.activeElement
  return a?.tagName === 'TEXTAREA' && a.labels?.[0]?.textContent === 'Mensagem para o cliente'
})
const campoDe = (pagina) => pagina.getByRole('textbox', { name: 'Mensagem para o cliente' })
const exigeFocoNoCampo = async (pagina, depoisDe) => {
  assert.ok(await focoNoCampo(pagina), `depois de ${depoisDe} o foco está em ${await quemTemOFoco(pagina)}, não no campo de mensagem`)
}

// --- a. Assumir conversa -----------------------------------------------------------------------------------------------
{
  const { contexto, pagina } = await abrir()
  await pagina.getByRole('button', { name: 'Assumir conversa', exact: true }).click()
  await pagina.waitForTimeout(500)
  await foto(pagina, 'a-assumir')
  await confere('a. depois de Assumir conversa o foco termina no campo de mensagem', () => exigeFocoNoCampo(pagina, 'Assumir conversa'))
  await contexto.close()
}

// --- b. enviar por clique ----------------------------------------------------------------------------------------------
{
  const { contexto, pagina } = await abrir()
  await pagina.getByRole('button', { name: 'Assumir conversa', exact: true }).click()
  await pagina.waitForTimeout(400)
  const campo = campoDe(pagina)
  await campo.click()
  await campo.fill('oi, tudo bem?')
  await pagina.getByRole('button', { name: 'Enviar mensagem', exact: true }).click()
  await pagina.waitForTimeout(500)
  await foto(pagina, 'b-enviar')
  await confere('b. depois de enviar por clique o foco segue no campo de mensagem', () => exigeFocoNoCampo(pagina, 'enviar por clique'))
  await contexto.close()
}

// --- c. salvar nota ----------------------------------------------------------------------------------------------------
{
  const { contexto, pagina } = await abrir()
  await pagina.getByRole('button', { name: 'Adicionar nota' }).first().click()
  await pagina.locator('dialog[open] textarea').first().fill('nota da ficha')
  await pagina.getByRole('button', { name: 'Fixar nota' }).first().click()
  await pagina.waitForTimeout(600)
  await foto(pagina, 'c-nota-da-ficha')
  await confere('c1. depois de Fixar nota (da ficha) o foco termina no campo de mensagem', () => exigeFocoNoCampo(pagina, 'Fixar nota'))
  await contexto.close()
}
{
  const { contexto, pagina } = await abrir()
  await pagina.getByRole('button', { name: 'Nota interna' }).first().click()
  await pagina.locator('dialog[open] textarea').first().fill('nota do campo')
  await pagina.locator('dialog[open]').getByRole('button', { name: /^(Salvar|Fixar nota)/ }).first().click()
  await pagina.waitForTimeout(600)
  await confere('c2. depois de salvar a Nota interna o foco termina no campo de mensagem', () => exigeFocoNoCampo(pagina, 'salvar a nota interna'))
  await contexto.close()
}

// --- d. modal "Enviar e assumir" ---------------------------------------------------------------------------------------
{
  const { contexto, pagina } = await abrir()
  const campo = campoDe(pagina)
  await campo.fill('oi!')
  await campo.press('Enter')
  const dialogo = pagina.getByRole('dialog', { name: 'Enviar e assumir esta conversa?' })
  await dialogo.waitFor({ timeout: 10_000 })
  await pagina.waitForTimeout(400)
  await foto(pagina, 'd-modal')
  await confere('d1. o foco começa no botão "Enviar e assumir" do modal', async () => {
    const nome = await pagina.evaluate(() => document.activeElement?.closest('dialog')?.querySelector('[data-foco-inicial]') === document.activeElement ? document.activeElement.textContent.trim() : null)
    assert.equal(nome, 'Enviar e assumir', `o foco está em ${await quemTemOFoco(pagina)}`)
  })
  await confere('d2. o tooltip "Fechar" do X não aparece com o modal aberto (#2857)', async () => {
    const dicas = await pagina.evaluate(() => [...document.querySelectorAll('[role=tooltip]')].filter((d) => d.matches(':popover-open') || d.offsetParent !== null).map((d) => d.textContent.trim()))
    assert.deepEqual(dicas.filter((t) => /Fechar/.test(t)), [], `tooltip visível: ${dicas.join(' | ')}`)
  })
  await pagina.keyboard.press('Enter')
  await pagina.waitForTimeout(700)
  await confere('d3. Enter confirma: o modal fecha e a mensagem sai', async () => {
    assert.equal(await dialogo.count(), 0, 'o modal continua aberto depois do Enter')
    const saiu = await pagina.getByText('oi!', { exact: true }).count()
    assert.ok(saiu >= 1, 'a mensagem "oi!" não apareceu na conversa')
  })
  await contexto.close()
}

// --- e. contraste do anel de foco do campo -----------------------------------------------------------------------------
const luz = ([r, g, b]) => [r, g, b].map((v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4 }).reduce((s, v, i) => s + v * [0.2126, 0.7152, 0.0722][i], 0)
const contraste = (x, y) => { const [a, b] = [luz(x), luz(y)].sort((m, n) => n - m); return (a + 0.05) / (b + 0.05) }
const lerCor = (css) => {
  const n = (css.match(/[\d.]+/g) ?? []).map(Number)
  if (!/^rgba?\(/.test(css) || n.length < 3) throw new Error(`cor em formato inesperado: ${css}`)
  return { rgb: n.slice(0, 3), alfa: n.length > 3 ? n[3] : 1 }
}
const sobre = (frente, fundo) => frente.rgb.map((v, i) => v * frente.alfa + fundo[i] * (1 - frente.alfa))
for (const tema of TEMAS) {
  const { contexto, pagina } = await abrir(tema)
  await campoDe(pagina).focus()
  await pagina.waitForTimeout(300)
  const medida = await pagina.evaluate(() => {
    const campo = document.activeElement
    const caixa = campo.closest('[class*=caixa]')
    const anel = getComputedStyle(caixa)
    // O fundo que fica atrás do anel: o primeiro ancestral com cor opaca, a partir do pai da caixa.
    let no = caixa.parentElement, fundo = null
    while (no && !fundo) {
      const cor = getComputedStyle(no).backgroundColor
      const m = cor.match(/[\d.]+/g)?.map(Number) ?? []
      if (m.length >= 3 && (m.length === 3 || m[3] === 1)) fundo = cor
      no = no.parentElement
    }
    return { cor: anel.outlineColor, largura: parseFloat(anel.outlineWidth), estilo: anel.outlineStyle, fundo: fundo ?? getComputedStyle(document.body).backgroundColor }
  })
  await foto(pagina, `e-anel-${tema}`)
  await confere(`e. ${tema}: o anel de foco do campo tem contraste de 3:1 ou mais`, () => {
    assert.ok(medida.largura > 0 && medida.estilo !== 'none', 'o campo focado não desenha anel')
    const fundo = lerCor(medida.fundo).rgb
    const razao = contraste(sobre(lerCor(medida.cor), fundo), fundo)
    assert.ok(razao >= 3, `contraste do anel ${razao.toFixed(2)}:1 (${medida.cor} sobre ${medida.fundo})`)
  })
  await contexto.close()
}

// --- f. Tab fecha o popover de filtros ---------------------------------------------------------------------------------
{
  const { contexto, pagina } = await abrir()
  const gatilho = pagina.getByRole('button', { name: /^Filtrar conversas/ })
  await gatilho.click()
  await pagina.getByRole('dialog', { name: 'Filtros das conversas' }).waitFor({ timeout: 10_000 })
  await confere('f. Tab que sai do popover de filtros o fecha e aria-expanded volta a false', async () => {
    assert.equal(await gatilho.getAttribute('aria-expanded'), 'true', 'o gatilho não abriu o popover')
    for (let i = 0; i < 25 && await pagina.getByRole('dialog', { name: 'Filtros das conversas' }).count() > 0; i++) await pagina.keyboard.press('Tab')
    assert.equal(await pagina.getByRole('dialog', { name: 'Filtros das conversas' }).count(), 0, 'o popover seguiu aberto depois de 25 Tab')
    assert.equal(await gatilho.getAttribute('aria-expanded'), 'false', 'aria-expanded ficou true com o popover fechado')
    assert.notEqual(await quemTemOFoco(pagina), 'o body (o foco se perdeu)', 'o foco se perdeu ao fechar o popover')
  })
  await contexto.close()
}

// --- g. toque (tablet, 1000 px): o foco não vai ao campo sem ela pedir; o teclado não abre -----------------------------
{
  // hasTouch + isMobile fazem o Chromium declarar (pointer: coarse), como num tablet.
  const { contexto, pagina } = await abrir('light', 1000, 800, { hasTouch: true, isMobile: true })
  await pagina.getByRole('button', { name: 'Assumir conversa', exact: true }).click()
  await pagina.waitForTimeout(600)
  await confere('g. com ponteiro de toque o foco não é levado ao campo depois de Assumir', async () => {
    assert.equal(await pagina.evaluate(() => matchMedia('(pointer: fine)').matches), false, 'a emulação de toque não pegou')
    assert.equal(await focoNoCampo(pagina), false, 'o foco foi ao campo de mensagem num aparelho de toque')
  })
  await contexto.close()
}

await navegador.close()
await servidor?.close()
console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) { console.log(falhas.map((f) => ' - ' + f).join('\n')); process.exit(1) }
