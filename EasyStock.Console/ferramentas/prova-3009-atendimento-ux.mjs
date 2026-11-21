// #3009: três defeitos de UX do Balcão, medidos no Chromium em modo demonstração (1440x900 e 390x844, 2 temas).
//   a  a prévia das respostas prontas aparece (antes: ~3,7 px, só o título), a lista rola e o teclado segue o item ativo;
//   b  o selo de dinheiro ("Falta R$ 68,00") cabe inteiro na fileira visível da `.contextoLinha` (#1761, L16);
//   c  o placeholder dos campos crus tem contraste >= 4,5:1 em repouso e com foco, nos dois temas.
// PLAYWRIGHT_MODULE aponta para o runtime externo; sem ele usa o `playwright` do node_modules.
// O Vite do runner está no modo API; esta prova sobe o seu, em demonstração (como a prova-1653).
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
const capturas = process.env.CAPTURAS_3009 ?? null
if (capturas) mkdirSync(capturas, { recursive: true })

const falhas = []
let passou = 0
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) {
    falhas.push(descricao); console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').slice(0, 4).join(' ')}`)
  }
}

const TELAS = [['1440', { width: 1440, height: 900 }], ['390', { width: 390, height: 844 }]]
const TEMAS = ['light', 'dark']
const navegador = await chromium.launch({ headless: true })

async function abrir(tela, tema) {
  const contexto = await navegador.newContext({ viewport: tela, colorScheme: tema, reducedMotion: 'reduce' })
  const pagina = await contexto.newPage()
  await pagina.goto(`${origem}/#/m/atendimento/balcao`, { waitUntil: 'networkidle', timeout: 120_000 })
  await pagina.waitForSelector('[class*=contextoLinha]', { timeout: 30_000 })
  await pagina.waitForTimeout(600)
  return { contexto, pagina }
}
const foto = async (pagina, nome) => { if (capturas) await pagina.screenshot({ path: join(capturas, `${process.env.FASE_3009 ?? 'depois'}-${nome}.png`) }) }

async function abrirSeletor(pagina) {
  await pagina.locator('[class*=contextoLinha]').nth(2).click()
  await pagina.waitForTimeout(600)
  await pagina.locator('button[aria-haspopup=listbox]').first().click()
  await pagina.waitForSelector('ul[role=listbox][aria-label="Respostas prontas"]', { timeout: 10_000 })
  return pagina.locator('input[aria-label="Buscar resposta pronta"]')
}

// --- b. selo de dinheiro dentro da caixa visível da .contextoLinha -------------------------------------------------
for (const [nome, tela] of TELAS) {
  const { contexto, pagina } = await abrir(tela, 'light')
  const medidas = await pagina.evaluate(() => [...document.querySelectorAll('[class*=contextoLinha]')].flatMap((linha) => {
    const caixa = linha.getBoundingClientRect()
    return [...linha.children].filter((pilula) => /R\$/.test(pilula.textContent)).map((pilula) => {
      const r = pilula.getBoundingClientRect()
      return { texto: pilula.textContent.trim(), dentro: r.top >= caixa.top - 0.5 && r.bottom <= caixa.bottom + 0.5 && r.left >= caixa.left - 0.5 && r.right <= caixa.right + 0.5 }
    })
  }))
  await foto(pagina, `selo-${nome}`)
  confere(`b ${nome}: há cartões com selo de dinheiro e todos cabem na fileira visível`, () => {
    assert.ok(medidas.length >= 3, `poucos selos de dinheiro na demonstração: ${medidas.length}`)
    const fora = medidas.filter((m) => !m.dentro).map((m) => m.texto)
    assert.deepEqual(fora, [], `selo fora da caixa visível da .contextoLinha: ${fora.join(', ')}`)
  })
  await contexto.close()
}

// --- a. prévia das respostas prontas -----------------------------------------------------------------------------
for (const [nome, tela] of TELAS) {
  for (const tema of TEMAS) {
    const { contexto, pagina } = await abrir(tela, tema)
    const campo = await abrirSeletor(pagina)
    const lido = () => pagina.evaluate(() => {
      const lista = document.querySelector('ul[role=listbox][aria-label="Respostas prontas"]')
      const r = lista.getBoundingClientRect()
      const ativa = lista.querySelector('[aria-selected=true]')
      const a = ativa?.getBoundingClientRect()
      return {
        itens: lista.children.length, alturaLista: r.height, rolagem: lista.scrollHeight, visivel: lista.clientHeight, teto: Math.min(innerHeight * 0.32, 240),
        previas: [...lista.children].map((li) => {
          const previa = li.querySelector('[class*=previa]'), e = getComputedStyle(previa)
          const linha = parseFloat(e.lineHeight) || parseFloat(e.fontSize) * 1.2
          return { altura: previa.getBoundingClientRect().height, linha, texto: previa.textContent.trim().length }
        }),
        ativaNaLista: Boolean(a && a.top >= r.top - 1 && a.bottom <= r.bottom + 1), ativaId: ativa?.id, descendente: document.activeElement.getAttribute('aria-activedescendant'),
      }
    })
    const antes = await lido()
    await foto(pagina, `respostas-${nome}-${tema}`)
    confere(`a ${nome} ${tema}: ${antes.itens} respostas, cada prévia com altura de uma linha e texto`, () => {
      assert.ok(antes.itens >= 6, `a demonstração tem só ${antes.itens} respostas; a prova precisa de 6 ou mais`)
      const curtas = antes.previas.filter((p) => p.altura < p.linha - 0.5 || p.texto === 0)
      assert.equal(curtas.length, 0, `${curtas.length} prévias cortadas, a primeira com ${curtas[0]?.altura?.toFixed(1)}px de altura (linha ${curtas[0]?.linha?.toFixed(1)}px)`)
    })
    confere(`a ${nome} ${tema}: a lista rola dentro do teto de altura`, () => {
      assert.ok(antes.rolagem > antes.visivel, `a lista não rola (${antes.rolagem} <= ${antes.visivel})`)
      assert.ok(antes.alturaLista <= antes.teto + 1, `a lista passa do teto: ${antes.alturaLista} > ${antes.teto}`)
    })
    for (let i = 0; i < antes.itens - 1; i++) await campo.press('ArrowDown')
    const fim = await lido()
    confere(`a ${nome} ${tema}: o teclado leva o item ativo ao fim e ele fica visível`, () => {
      assert.ok(fim.ativaId && fim.descendente === fim.ativaId, `aria-activedescendant (${fim.descendente}) difere do item ativo (${fim.ativaId})`)
      assert.ok(fim.ativaNaLista, 'o item ativo saiu da área visível da lista')
    })
    await contexto.close()
  }
}

// --- c. contraste do placeholder -----------------------------------------------------------------------------------
const luz = ([r, g, b]) => [r, g, b].map((v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4 }).reduce((s, v, i) => s + v * [0.2126, 0.7152, 0.0722][i], 0)
const contraste = (x, y) => { const [a, b] = [luz(x), luz(y)].sort((m, n) => n - m); return (a + 0.05) / (b + 0.05) }
const rgb = (css) => (css.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number)
for (const tema of TEMAS) {
  const { contexto, pagina } = await abrir({ width: 1440, height: 900 }, tema)
  await abrirSeletor(pagina)
  const alvos = [['busca do Balcão', '#balcao-busca'], ['busca de respostas', 'input[aria-label="Buscar resposta pronta"]']]
  for (const [rotulo, seletor] of alvos) {
    for (const estado of ['repouso', 'foco']) {
      const campoBusca = pagina.locator(seletor)
      if (estado === 'foco') await campoBusca.focus(); else await pagina.evaluate(() => document.activeElement?.blur())
      await pagina.waitForTimeout(450)
      const m = await campoBusca.evaluate((el) => {
        const p = getComputedStyle(el, '::placeholder')
        let fundo = el
        while (fundo && /rgba\(.*, 0\)|transparent/.test(getComputedStyle(fundo).backgroundColor)) fundo = fundo.parentElement
        return { cor: p.color, opacidade: p.opacity, fundo: getComputedStyle(fundo ?? document.body).backgroundColor }
      })
      confere(`c ${tema} ${rotulo} (${estado}): placeholder >= 4,5:1 (${m.cor} sobre ${m.fundo})`, () => {
        assert.equal(m.opacidade, '1', `opacidade do placeholder ${m.opacidade}`)
        const c = contraste(rgb(m.cor), rgb(m.fundo))
        assert.ok(c >= 4.5, `contraste ${c.toFixed(2)}:1 < 4,5:1`)
      })
    }
  }
  await foto(pagina, `placeholder-${tema}`)
  await contexto.close()
}

await navegador.close()
await servidor?.close()
console.log(`\n${passou} ok, ${falhas.length} falhas`)
process.exit(falhas.length ? 1 : 0)
