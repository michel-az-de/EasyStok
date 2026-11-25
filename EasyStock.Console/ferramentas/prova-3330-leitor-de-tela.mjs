// #3330: leitor de tela, teclado e forced-colors no Atendimento do desktop, medidos no Chromium em modo demonstração (1440x900).
// a ao trocar de conversa o fio não é anunciado de novo: nada do que entra no `role=log` na troca cai numa região
//   viva ligada (antes: 964 caracteres anunciados de uma vez);
// f a regra pura do silêncio (`silencioDoFio.js`) também cala o histórico que chega depois da troca (modo API, conversa
//   que abre vazia) e deixa a mensagem ao vivo passar;
// b depois da troca assentar, o fio volta a anunciar mensagem nova (a correção não cala o log para sempre);
// c a área de rolagem do fio entra na ordem do Tab, tem papel e nome, mostra o foco e rola pelo teclado
//   (antes: sem tabindex nem nome, o axe `scrollable-region-focusable`);
// d com `forced-colors: active` (Alto Contraste do Windows) a conversa aberta e o filtro ligado seguem distinguíveis
//   dos outros por contorno (antes: só diferiam por `background`, que o modo remove);
// e a demonstração tem o "Pular para o conteúdo" que o modo API já tinha (o primeiro Tab cai nele e o Enter leva
//   o foco ao conteúdo).
// Fora desta prova: o nome acessível do cartão (axe `label-content-name-mismatch`) e o Ctrl+K na demonstração
// (a Busca na Casa só existe com a API). PLAYWRIGHT_MODULE aponta para o runtime externo; sem ele usa o `playwright`
// do node_modules. O Vite do runner está no modo API; esta prova sobe o seu, em demonstração (como a prova-3317).
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
const capturas = process.env.CAPTURAS_3330 ?? null
if (capturas) mkdirSync(capturas, { recursive: true })

const falhas = []
let passou = 0
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok ' + descricao) } catch (erro) {
    falhas.push(descricao); console.log(`FALHA ${descricao}\n ${String(erro.message).split('\n').slice(0, 4).join(' ')}`)
  }
}

const navegador = await chromium.launch({ headless: true })

async function abrir(opcoes = {}, { indice = 2, altura = 900 } = {}) {
  const contexto = await navegador.newContext({ viewport: { width: 1440, height: altura }, colorScheme: 'light', reducedMotion: 'reduce', ...opcoes })
  const pagina = await contexto.newPage()
  await pagina.goto(`${origem}/#/m/atendimento/balcao`, { waitUntil: 'networkidle', timeout: 120_000 })
  await pagina.waitForSelector('[class*=contextoLinha]', { timeout: 30_000 })
  // A 3ª conversa da demonstração é atendida pelo Assistente: tem "Assumir conversa".
  await pagina.locator('[class*=contextoLinha]').nth(indice).click()
  await pagina.waitForSelector('[role=log]', { timeout: 10_000 })
  await pagina.waitForTimeout(1200)
  return { contexto, pagina }
}
const foto = async (pagina, nome) => { if (capturas) await pagina.screenshot({ path: join(capturas, `${process.env.FASE_3330 ?? 'depois'}-${nome}.png`) }) }

// Conta os caracteres que entram no `role=log` enquanto a região está viva. A leitura do atributo roda na
// microtarefa depois do commit do React, que é o estado que o navegador entrega ao leitor de tela.
const vigiarOFio = (pagina) => pagina.evaluate(() => {
  window.__anunciado = { chars: 0, ao_vivo: [] }
  new MutationObserver((registros) => {
    for (const registro of registros) {
      for (const no of registro.addedNodes) {
        const elemento = no.nodeType === 1 ? no : no.parentElement
        const fio = elemento?.closest?.('[role=log]') ?? (elemento?.querySelector?.('[role=log]') ?? null)
        if (!fio) continue
        const vivo = fio.closest('[aria-live]')?.getAttribute('aria-live') ?? 'off'
        window.__anunciado.ao_vivo.push(vivo)
        if (vivo !== 'off') window.__anunciado.chars += (no.textContent ?? '').length
      }
    }
  }).observe(document.body, { childList: true, subtree: true })
})
const anunciado = (pagina) => pagina.evaluate(() => window.__anunciado)
const areaDoFio = (pagina) => pagina.locator('[role=log]').locator('xpath=..')

// --- f. regra do silêncio do fio (pura): o histórico que chega depois da troca no modo API também cala ---------------------
{
  const { novoSilencio, proximoSilencio } = await import('../src/features/atendimento/silencioDoFio.js')
  const rodar = (chaveInicial, totalInicial, passos) => passos.reduce((estado, [chave, total]) => proximoSilencio(estado, chave, total), novoSilencio(chaveInicial, totalInicial))
  await confere('f. conversa que abre cheia: só a troca de conversa cala de novo', () => {
    const aberta = novoSilencio('c1', 40)
    assert.equal(proximoSilencio(aberta, 'c1', 41), aberta, 'mensagem ao vivo numa conversa cheia não pode calar o fio')
    const trocada = proximoSilencio(aberta, 'c2', 12)
    assert.equal(trocada.chave, 'c2'); assert.equal(trocada.calado, true); assert.equal(trocada.rodada, 1, 'a troca reinicia o silêncio')
  })
  await confere('f. conversa que abre vazia e recebe o histórico de uma vez (várias mensagens) cala de novo', () => {
    const depois = rodar('c1', 0, [['c1', 0], ['c1', 38]])
    assert.equal(depois.rodada, 1, 'o histórico de 38 mensagens chegou com o fio vivo e seria lido inteiro')
    assert.equal(depois.calado, true)
  })
  await confere('f. a primeira mensagem ao vivo e as seguintes seguem anunciadas numa conversa que abriu vazia', () => {
    const depois = rodar('c1', 0, [['c1', 1], ['c1', 2], ['c1', 3]])
    assert.equal(depois.rodada, 0, 'mensagem ao vivo cala o fio e deixa de ser anunciada')
    assert.equal(depois.vazio, false)
  })
}

// --- a e b. troca de conversa não re-anuncia; mensagem nova segue anunciada ---------------------------------------------
{
  const { contexto, pagina } = await abrir()
  await vigiarOFio(pagina)
  await pagina.locator('[class*=contextoLinha]').nth(0).click()
  await pagina.waitForTimeout(250)
  const troca = await anunciado(pagina)
  await foto(pagina, 'a-troca')
  await confere('a. ao trocar de conversa o fio não é anunciado de novo', () => {
    assert.equal(troca.chars, 0, `${troca.chars} caracteres entraram no fio com a região viva (${troca.ao_vivo.join(',') || 'sem registro'}); o leitor de tela leria a conversa inteira`)
  })
  await contexto.close()
}
{
  const { contexto, pagina } = await abrir({}, { indice: 0 })
  await pagina.locator('[class*=contextoLinha]').nth(2).click()
  await pagina.waitForTimeout(1500)
  await vigiarOFio(pagina)
  await pagina.getByRole('button', { name: 'Assumir conversa', exact: true }).click()
  await pagina.waitForTimeout(400)
  const campo = pagina.getByRole('textbox', { name: 'Mensagem para o cliente' })
  await campo.fill('oi, tudo bem?')
  await pagina.getByRole('button', { name: 'Enviar mensagem', exact: true }).click()
  await pagina.waitForTimeout(800)
  const depois = await anunciado(pagina)
  await confere('b. passada a troca, a mensagem nova entra no fio com a região viva e é anunciada', async () => {
    const vivo = await pagina.locator('[role=log]').evaluate((no) => no.closest('[aria-live]')?.getAttribute('aria-live'))
    assert.equal(vivo, 'polite', `a região ficou em "${vivo}" depois da troca; mensagem nova não seria anunciada`)
    assert.ok(depois.chars > 0, 'a mensagem enviada não entrou no fio com a região viva')
  })
  await contexto.close()
}

// --- c. área de rolagem do fio: Tab, papel, nome, foco visível e rolagem por teclado --------------------------------------
{
  const { contexto, pagina } = await abrir({}, { altura: 560 })
  const area = areaDoFio(pagina)
  const dados = await area.evaluate((no) => ({
    tabindex: no.getAttribute('tabindex'), nome: (no.getAttribute('aria-label') ?? '').trim(),
    sobra: no.scrollHeight - no.clientHeight,
  }))
  await confere('c. a área de rolagem do fio tem tabindex 0 e é uma região com nome', async () => {
    assert.equal(dados.tabindex, '0', `tabindex "${dados.tabindex}": quem usa só o teclado não alcança a conversa`)
    assert.ok(dados.nome.length > 0, 'a área não tem nome acessível')
    // Papel pela árvore de acessibilidade (o `section` com nome vira região), não pelo atributo.
    assert.equal(await pagina.getByRole('region', { name: dados.nome, exact: true }).count(), 1, 'a área não aparece como região com nome na árvore de acessibilidade')
  })
  await confere('c. o Tab alcança a área do fio, ela mostra o foco e a conversa rola pelo teclado', async () => {
    assert.ok(dados.sobra > 0, `o fio não rola nesta altura (sobra ${dados.sobra} px); a prova precisa de conversa maior que a tela`)
    await pagina.getByRole('textbox', { name: 'Mensagem para o cliente' }).focus()
    let achou = false
    for (let i = 0; i < 40 && !achou; i += 1) {
      await pagina.keyboard.press('Shift+Tab')
      achou = await area.evaluate((no) => document.activeElement === no)
    }
    assert.ok(achou, 'Shift+Tab desde o campo de mensagem não parou na área do fio em 40 passos')
    const anel = await area.evaluate((no) => { const e = getComputedStyle(no); return { estilo: e.outlineStyle, largura: parseFloat(e.outlineWidth) } })
    assert.ok(anel.estilo !== 'none' && anel.largura >= 2, `sem anel de foco na área do fio (${anel.estilo} ${anel.largura}px)`)
    const antes = await area.evaluate((no) => no.scrollTop)
    await pagina.keyboard.press('PageUp')
    await pagina.waitForTimeout(300)
    const depois = await area.evaluate((no) => no.scrollTop)
    assert.ok(depois < antes, `PageUp não rolou o fio (${antes} -> ${depois})`)
  })
  await contexto.close()
}

// --- d. forced-colors: a conversa aberta e o filtro ligado se distinguem por contorno ------------------------------------
{
  const { contexto, pagina } = await abrir({ forcedColors: 'active' })
  await foto(pagina, 'd-forced-colors')
  const contorno = (seletor) => pagina.locator(seletor).first().evaluate((no) => {
    const e = getComputedStyle(no); return { estilo: e.outlineStyle, largura: parseFloat(e.outlineWidth) }
  })
  const marcado = (c) => c.estilo !== 'none' && c.largura >= 2
  await confere('d. em forced-colors a conversa aberta tem contorno e as outras não', async () => {
    const aberta = await contorno('button[aria-current="true"]')
    const outra = await contorno('button[aria-current="false"]')
    assert.ok(marcado(aberta), `a conversa aberta ficou sem contorno (${aberta.estilo} ${aberta.largura}px): some no Alto Contraste`)
    assert.ok(!marcado(outra), `as outras conversas também têm contorno (${outra.estilo} ${outra.largura}px): não distingue a aberta`)
  })
  await confere('d. em forced-colors o filtro ligado tem contorno e o desligado não', async () => {
    const ligado = await contorno('fieldset button[aria-pressed="true"]')
    const desligado = await contorno('fieldset button[aria-pressed="false"]')
    assert.ok(marcado(ligado), `o filtro ligado ficou sem contorno (${ligado.estilo} ${ligado.largura}px): some no Alto Contraste`)
    assert.ok(!marcado(desligado), `o filtro desligado também tem contorno (${desligado.estilo} ${desligado.largura}px)`)
  })
  await contexto.close()
}

// --- e. "Pular para o conteúdo" também na demonstração ----------------------------------------------------------------
{
  // Página recém-aberta, sem clique antes: o ponto de partida do Tab é o início do documento.
  const contexto = await navegador.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: 'light', reducedMotion: 'reduce' })
  const pagina = await contexto.newPage()
  await pagina.goto(`${origem}/#/m/atendimento/balcao`, { waitUntil: 'networkidle', timeout: 120_000 })
  await pagina.waitForSelector('[class*=contextoLinha]', { timeout: 30_000 })
  await confere('e. o primeiro Tab cai no "Pular para o conteúdo" e o Enter leva o foco ao conteúdo', async () => {
    await pagina.keyboard.press('Tab')
    const primeiro = await pagina.evaluate(() => document.activeElement?.textContent?.trim() ?? '')
    assert.equal(primeiro, 'Pular para o conteúdo', `o primeiro Tab caiu em "${primeiro}"`)
    await pagina.keyboard.press('Enter')
    await pagina.waitForTimeout(200)
    const dentro = await pagina.evaluate(() => {
      const alvo = document.querySelector('[data-inicio-tela]')
      return Boolean(alvo) && (document.activeElement === alvo || alvo.contains(document.activeElement))
    })
    assert.ok(dentro, 'o Enter não levou o foco ao conteúdo principal')
  })
  await contexto.close()
}

await navegador.close()
if (servidor) await servidor.close()
console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) { console.log(falhas.map((f) => ' - ' + f).join('\n')); process.exit(1) }
