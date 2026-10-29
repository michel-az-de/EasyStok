/* eslint-disable no-console */
// Prova da #2146 (Onda 1 do plano de UX do Balcão, #2130, seções 3.3 e 3.4): Tooltip, BotaoIcone,
// Aviso e Selo, com a primeira adoção no Composer.
//
//   1  fonte: os componentes reaproveitam o que existe (Botao, Pilula, Icone), o CSS deles não tem
//      duração nem curva literal (só os tokens --dur-* e --ease-*), e o Composer não tem mais
//      `title` nativo em controle nem <button> cru;
//   2  Composer: todo botão tem nome acessível e todo botão só-ícone é um BotaoIcone;
//   3  Tooltip: aparece no hover depois de 500 ms (não antes), também no foco por teclado, e some
//      no Escape e ao sair; em toque (hasTouch/isMobile) não aparece;
//   4  alvo do BotaoIcone >= 44 x 44 px a 390 px de largura;
//   5  Aviso: remover o anexo mostra "Desfazer"; um aviso por vez (o novo substitui o anterior);
//      o Desfazer devolve o anexo; fica no pé da tela, centralizado;
//   6  movimento: sem preferência a dica usa 150 ms (--dur-curta); com reducedMotion 'reduce' a
//      duração computada da dica e do aviso é zero (o bloco de base.css).
//
// A parte de tela precisa de Chromium: com PLAYWRIGHT_MODULE (a pasta do pacote playwright) a prova
// compila a demonstração numa pasta temporária e a serve pelo próprio roteiro, sem servidor de pé.
//
//   PLAYWRIGHT_MODULE=<pasta do playwright> node ferramentas/prova-2146-componentes-gramatica.mjs

import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'

const RAIZ = resolve(import.meta.dirname, '..')
const fonte = (relativo) => {
  const caminho = resolve(RAIZ, 'src', relativo)
  return existsSync(caminho) ? readFileSync(caminho, 'utf8') : ''
}

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

// --- 1. Fonte ---------------------------------------------------------------------------------
await confere('1. os quatro componentes existem e reaproveitam Botao, Pilula e Icone', () => {
  const botaoIcone = fonte('componentes/BotaoIcone.jsx')
  const selo = fonte('componentes/Selo.jsx')
  const aviso = fonte('componentes/Aviso.jsx')
  assert.ok(fonte('componentes/Tooltip.jsx'), 'Tooltip.jsx não existe')
  assert.match(botaoIcone, /from '\.\/Botao'/, 'o BotaoIcone precisa ser um Botao')
  assert.match(botaoIcone, /from '\.\/Tooltip'/, 'o BotaoIcone gera o Tooltip sozinho')
  assert.match(selo, /from '\.\/Pilula'/, 'o Selo precisa ser a Pilula')
  assert.equal(existsSync(resolve(RAIZ, 'src/componentes/Selo.module.css')), false, 'o Selo não tem CSS próprio')
  assert.match(aviso, /from '\.\/Botao'/)
  assert.match(aviso, /export function ProvedorDeAviso/)
  assert.match(aviso, /export function useAviso/)
})

await confere('1. o CSS dos componentes novos só usa os tokens de movimento (nenhuma duração ou curva literal)', () => {
  for (const nome of ['Tooltip', 'BotaoIcone', 'Aviso']) {
    const caminho = resolve(RAIZ, `src/componentes/${nome}.module.css`)
    assert.ok(existsSync(caminho), `${nome}.module.css não existe`)
    const css = readFileSync(caminho, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')
    const declaracoes = css.match(/(?:animation|transition)[\w-]*\s*:[^;]+/g) ?? []
    for (const linha of declaracoes) {
      const semTokens = linha.replace(/var\(--[\w-]+\)/g, '')
      assert.doesNotMatch(semTokens, /\d(?:ms|s)\b|cubic-bezier|linear\(|\b(?:ease|ease-in|ease-out|ease-in-out|linear)\b/, `${nome}: ${linha.trim()}`)
      assert.match(linha, /var\(--dur-[a-z]+\)/, `${nome}: ${linha.trim()} sem token de duração`)
    }
  }
})

await confere('1. o Composer não tem `title` nativo em controle nem <button> cru', () => {
  const composer = fonte('features/atendimento/Composer.jsx')
  const anexos = fonte('features/atendimento/ComposerAnexos.jsx')
  // O único `title` que fica é o da linha de restrição (um <p>, não controle), guardado pelas provas 1868 e 1870.
  const titulos = [...composer.matchAll(/<(\w+)[^<>]*?\stitle=/g)].map((m) => m[1])
  assert.deepEqual(titulos, ['p'], `title em: ${titulos.join(', ')}`)
  assert.equal((composer.match(/<button\b/g) ?? []).length, 0, '<button> cru no Composer')
  assert.doesNotMatch(anexos, /<label[^>]*\stitle=/, 'o Anexar ainda usa title nativo')
  assert.doesNotMatch(anexos, /aria-label=\{`Remover anexo/, 'o Remover anexo ainda é <button> cru')
})

// --- 2 a 6. Tela --------------------------------------------------------------------------------
const pastaDoPlaywright = process.env.PLAYWRIGHT_MODULE
if (!pastaDoPlaywright) {
  console.log('\npulado: sem PLAYWRIGHT_MODULE não há Chromium; Tooltip, alvo, Aviso e movimento ficam sem conferência neste roteiro.')
} else {
  const { chromium } = createRequire(import.meta.url)(pastaDoPlaywright)
  const { build } = await import('vite')
  const pasta = mkdtempSync(join(tmpdir(), 'prova-2146-'))
  await build({ root: RAIZ, logLevel: 'silent', build: { outDir: pasta, emptyOutDir: true } })
  const html = readFileSync(join(pasta, 'index.html'), 'utf8')
  const ORIGEM = 'http://localhost:5199'
  const navegador = await chromium.launch({ headless: true })
  // PNG de 1 x 1 px, só para a prévia do anexo existir.
  const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64')

  async function abrir({ largura = 1280, altura = 900, celular = false, reduzir = true } = {}) {
    const contexto = await navegador.newContext({
      viewport: { width: largura, height: altura }, serviceWorkers: 'block',
      reducedMotion: reduzir ? 'reduce' : 'no-preference',
      ...(celular ? { hasTouch: true, isMobile: true } : {}),
    })
    await contexto.route(`${ORIGEM}/**`, (rota) => {
      const url = new URL(rota.request().url())
      if (url.pathname.startsWith('/api/')) return rota.fulfill({ json: { data: [] } })
      return rota.fulfill({ body: html, contentType: 'text/html' })
    })
    const pagina = await contexto.newPage()
    await pagina.goto(`${ORIGEM}/#/m/atendimento`)
    const cartao = pagina.locator('button[aria-current]').filter({ has: pagina.locator('time') }).filter({ hasText: 'José Moretti' })
    if (celular) await cartao.tap()
    else await cartao.click()
    const campo = pagina.getByRole('textbox', { name: 'Mensagem para o cliente' })
    await campo.waitFor()
    const composer = pagina.locator('[class*="_composer_"]').filter({ has: campo }).last()
    return { contexto, pagina, campo, composer }
  }
  const dicaVisivel = (pagina) => pagina.locator('[role="tooltip"]').filter({ visible: true })
  const esperar = (pagina, ms) => pagina.waitForTimeout(ms)

  try {
    await confere('2. Composer: todo botão tem nome acessível e todo botão só-ícone é BotaoIcone', async () => {
      const { contexto, composer } = await abrir()
      const arvore = await composer.ariaSnapshot()
      const botoes = [...arvore.matchAll(/^\s*- button(?: "([^"]*)")?/gm)].map((m) => m[1] ?? '')
      assert.ok(botoes.length >= 4, `só ${botoes.length} botões no Composer`)
      assert.deepEqual(botoes.filter((nome) => !nome.trim()), [], 'botão sem nome')
      const soIcone = await composer.evaluate((raiz) => [...raiz.querySelectorAll('button')]
        .filter((b) => !b.innerText.trim() && b.querySelector('svg'))
        .map((b) => ({ nome: b.getAttribute('aria-label'), botaoIcone: b.hasAttribute('data-botao-icone') })))
      assert.ok(soIcone.length >= 1, 'nenhum botão só-ícone no Composer')
      assert.deepEqual(soIcone.filter((b) => !b.botaoIcone).map((b) => b.nome), [], 'botão só-ícone fora do BotaoIcone')
      const comTitle = await composer.evaluate((raiz) => raiz.querySelectorAll('button[title], label[title]').length)
      assert.equal(comTitle, 0, `${comTitle} controles com title nativo`)
      await contexto.close()
    })

    await confere('2. o Enviar se chama "Enviar mensagem" e o atalho Enter aparece na dica', async () => {
      const { contexto, pagina, campo } = await abrir()
      await campo.fill('Oi, tudo certo?')
      const enviar = pagina.getByRole('button', { name: 'Enviar mensagem', exact: true })
      await enviar.hover()
      await esperar(pagina, 700)
      assert.match(await dicaVisivel(pagina).innerText(), /Enviar mensagem\s*Enter/)
      await contexto.close()
    })

    await confere('3. hover: a dica não aparece antes de 500 ms, aparece depois e descreve o botão', async () => {
      const { contexto, pagina } = await abrir()
      const nota = pagina.getByRole('button', { name: 'Nota interna', exact: true })
      await nota.hover()
      await esperar(pagina, 250)
      assert.equal(await dicaVisivel(pagina).count(), 0, 'apareceu antes de 500 ms')
      await esperar(pagina, 450)
      const dica = dicaVisivel(pagina)
      assert.equal(await dica.count(), 1, 'não apareceu depois de 500 ms')
      assert.equal(await dica.innerText(), 'Nota interna: fica na ficha, o cliente nunca vê')
      const descrito = (await nota.getAttribute('aria-describedby')) ?? ''
      assert.ok(descrito.split(' ').includes(await dica.getAttribute('id')), 'o botão não aponta para a dica')
      // Acima do botão (cabe acima).
      const [caixaDica, caixaBotao] = [await dica.boundingBox(), await nota.boundingBox()]
      assert.ok(caixaDica.y + caixaDica.height <= caixaBotao.y, 'a dica não ficou acima do botão')
      await pagina.mouse.move(5, 5)
      await esperar(pagina, 100)
      assert.equal(await dicaVisivel(pagina).count(), 0, 'não sumiu ao sair')
      await contexto.close()
    })

    await confere('3. foco por teclado mostra a dica depois de 500 ms e o Escape a fecha', async () => {
      const { contexto, pagina, campo } = await abrir()
      await campo.click()
      await pagina.keyboard.press('Tab')
      const focado = await pagina.evaluate(() => document.activeElement?.innerText?.trim())
      assert.equal(focado, 'Respostas', `o Tab foi para "${focado}"`)
      await esperar(pagina, 700)
      assert.equal(await dicaVisivel(pagina).innerText(), 'Respostas prontas (atalho: / no campo)')
      await pagina.keyboard.press('Escape')
      await esperar(pagina, 50)
      assert.equal(await dicaVisivel(pagina).count(), 0, 'o Escape não fechou')
      await contexto.close()
    })

    await confere('3. toque (hasTouch, isMobile): nem hover nem foco mostram a dica', async () => {
      const { contexto, pagina, campo } = await abrir({ largura: 390, altura: 844, celular: true })
      await pagina.getByRole('button', { name: 'Nota interna', exact: true }).hover()
      await esperar(pagina, 700)
      assert.equal(await dicaVisivel(pagina).count(), 0, 'apareceu no hover em toque')
      await campo.focus()
      await pagina.keyboard.press('Tab')
      await esperar(pagina, 700)
      assert.equal(await dicaVisivel(pagina).count(), 0, 'apareceu no foco em toque')
      await contexto.close()
    })

    await confere('4. celular 390: todo BotaoIcone do Composer tem alvo de pelo menos 44 x 44 px', async () => {
      const { contexto, pagina, composer } = await abrir({ largura: 390, altura: 844, celular: true })
      await composer.locator('input[type="file"]').setInputFiles({ name: 'prova.png', mimeType: 'image/png', buffer: PNG })
      await pagina.getByRole('button', { name: 'Remover anexo prova.png', exact: true }).waitFor()
      const alvos = await composer.evaluate((raiz) => [...raiz.querySelectorAll('[data-botao-icone]')].map((b) => {
        const caixa = b.getBoundingClientRect()
        return { nome: b.getAttribute('aria-label'), largura: Math.round(caixa.width), altura: Math.round(caixa.height) }
      }))
      assert.ok(alvos.length >= 2, `só ${alvos.length} BotaoIcone (esperado Enviar e Remover anexo)`)
      const pequenos = alvos.filter((a) => a.largura < 44 || a.altura < 44)
      assert.deepEqual(pequenos, [], pequenos.map((a) => `${a.nome}: ${a.largura} x ${a.altura}`).join('; '))
      await contexto.close()
    })

    await confere('5. Aviso: um por vez, o novo substitui o anterior, e o Desfazer devolve o anexo', async () => {
      const { contexto, pagina, composer } = await abrir()
      const arquivo = composer.locator('input[type="file"]')
      const anexar = async (nome) => {
        await arquivo.setInputFiles({ name: nome, mimeType: 'image/png', buffer: PNG })
        await pagina.getByRole('button', { name: `Remover anexo ${nome}`, exact: true }).click()
      }
      await anexar('primeira.png')
      const avisos = pagina.locator('[data-aviso]')
      await avisos.first().waitFor({ timeout: 2000 })
      assert.match(await avisos.innerText(), /Anexo removido: primeira\.png/)
      await anexar('segunda.png')
      assert.equal(await avisos.count(), 1, 'mais de um aviso na tela')
      assert.match(await avisos.innerText(), /segunda\.png/)
      assert.ok(await pagina.locator('output').filter({ has: avisos }).count(), 'o aviso não está na região viva educada')
      // No pé da tela, centralizado.
      const caixa = await avisos.boundingBox()
      assert.ok(Math.abs(caixa.x + caixa.width / 2 - 640) <= 2, `fora do centro: ${Math.round(caixa.x + caixa.width / 2)}`)
      assert.ok(caixa.y + caixa.height >= 900 - 60 && caixa.y + caixa.height <= 900, `fora do pé: base em ${Math.round(caixa.y + caixa.height)}`)
      await pagina.getByRole('button', { name: 'Desfazer', exact: true }).click()
      await pagina.getByRole('button', { name: 'Remover anexo segunda.png', exact: true }).waitFor({ timeout: 2000 })
      assert.equal(await avisos.count(), 0, 'o aviso ficou depois do Desfazer')
      await contexto.close()
    })

    await confere('6. movimento: sem preferência a dica entra em 150 ms (--dur-curta)', async () => {
      const { contexto, pagina } = await abrir({ reduzir: false })
      await pagina.getByRole('button', { name: 'Nota interna', exact: true }).hover()
      await esperar(pagina, 700)
      const duracao = await dicaVisivel(pagina).evaluate((el) => getComputedStyle(el).animationDuration)
      assert.equal(duracao, '0.15s')
      await contexto.close()
    })

    await confere('6. movimento: com reducedMotion "reduce" a duração computada da dica e do aviso é zero', async () => {
      const { contexto, pagina, composer } = await abrir({ reduzir: true })
      await pagina.getByRole('button', { name: 'Nota interna', exact: true }).hover()
      await esperar(pagina, 700)
      const segundos = (texto) => parseFloat(texto) * (texto.endsWith('ms') ? 0.001 : 1)
      const dica = await dicaVisivel(pagina).evaluate((el) => getComputedStyle(el).animationDuration)
      assert.ok(segundos(dica) <= 0.00001, `dica com ${dica}`)
      await composer.locator('input[type="file"]').setInputFiles({ name: 'prova.png', mimeType: 'image/png', buffer: PNG })
      await pagina.getByRole('button', { name: 'Remover anexo prova.png', exact: true }).click()
      const aviso = await pagina.locator('[data-aviso]').evaluate((el) => getComputedStyle(el).animationDuration)
      assert.ok(segundos(aviso) <= 0.00001, `aviso com ${aviso}`)
      await contexto.close()
    })
  } finally {
    await navegador.close()
    rmSync(pasta, { recursive: true, force: true })
  }
}

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
