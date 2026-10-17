/* eslint-disable no-console */
// Prova da issue #1872 (rodada 3, frente A: dívida da rodada visual 2).
//
//   1  um CSS só para os dois trilhos (Balcão e módulos), com a medida em token;
//   2  a largura do trilho tem fonte única (`TRILHO` em paineis.js) e o item deriva dela;
//   3  a camada da conta sai do bloco de contenção do ancestral (popover), com o `fixed` de reserva.
//
// Leitura do fonte onde só existe composição. Com CONSOLE_URL (console em modo API contra o
// laboratório) e PLAYWRIGHT_MODULE a prova também mede na tela real: trilho 100, item 84 e 54 px
// de altura mínima nos dois trilhos em 1280/1440/1920, e a camada da conta colada ao avatar dentro
// do menu do tablet (1024 px), com a animação do Modal correndo e depois dela. Sem as variáveis,
// essa parte é pulada e dita.
//
//   node ferramentas/prova-1872-trilho.mjs
//   CONSOLE_URL=http://localhost:5731 CONSOLE_USUARIO=<e-mail> CONSOLE_SENHA=<senha> PLAYWRIGHT_MODULE=<pasta do playwright> node ferramentas/prova-1872-trilho.mjs
//
// O usuário e a senha são os de demonstração do laboratório (EasyStock.Api/Data/Tenants/CasaDaBabaSeed.cs).

import { createRequire } from 'node:module'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    try {
      return proximo(especificador, contexto)
    } catch (erro) {
      if (especificador.startsWith('.') && !/\.m?jsx?$/.test(especificador)) return proximo(especificador + '.js', contexto)
      throw erro
    }
  },
})

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

const COMPOE = "composes: (\\w+) from '(?:\\.\\./)+componentes/Trilho\\.module\\.css'"

// --- 1. Um CSS só ------------------------------------------------------------------------------

await confere('1  as duas molduras compõem trilho, início, item, ativo e espaço do mesmo módulo', () => {
  const trilho = fonte('componentes/Trilho.module.css')
  for (const classe of ['trilho', 'inicio', 'item', 'ativo', 'espaco', 'avatar']) {
    assert.match(trilho, new RegExp(`\\.${classe}[ ,:{]`), `Trilho.module.css sem .${classe}`)
  }
  const usos = (css) => [...css.matchAll(new RegExp(COMPOE, 'g'))].map((m) => m[1])
  const balcao = usos(fonte('app/moldura.module.css'))
  const modulos = usos(fonte('features/hall/moldura.module.css'))
  for (const classe of ['trilho', 'inicio', 'item', 'ativo', 'espaco', 'avatar']) {
    assert.ok(balcao.includes(classe), `Balcão não compõe ${classe}`)
    assert.ok(modulos.includes(classe), `módulos não compõem ${classe}`)
  }
})

await confere('1  a medida do trilho e do item só é declarada no módulo único', () => {
  for (const moldura of ['app/moldura.module.css', 'features/hall/moldura.module.css']) {
    const css = fonte(moldura)
    assert.doesNotMatch(css, /width: var\(--largura-item-trilho\)/, `${moldura} repete a largura do item`)
    assert.doesNotMatch(css, /min-height: var\(--altura-item-trilho\)/, `${moldura} repete a altura do item`)
    assert.doesNotMatch(css, /flex: 0 0 var\(--largura-trilho\)/, `${moldura} repete a largura do trilho`)
  }
})

// --- 2. Fonte única da largura -----------------------------------------------------------------

await confere('2  TRILHO é a fonte: o token nasce dele, tokens.css não tem segunda cópia e o item deriva do respiro', async () => {
  const { TRILHO, tokensDaGeometria } = await import('../src/dominio/paineis.js')
  assert.deepEqual(tokensDaGeometria(), { '--largura-trilho': `${TRILHO}px` })
  const tokens = fonte('estilos/tokens.css')
  assert.doesNotMatch(tokens, /--largura-trilho:/)
  const respiro = Number(tokens.match(/--respiro-trilho: (\d+)px;/)[1])
  assert.match(tokens, /--largura-item-trilho: calc\(var\(--largura-trilho\) - 2 \* var\(--respiro-trilho\)\);/)
  assert.equal(TRILHO - 2 * respiro, 84, 'item de 84 px com trilho de 100')
  assert.match(fonte('componentes/Trilho.module.css'), /padding: var\(--e-3\) var\(--respiro-trilho\)/, 'o respiro é o do padding do trilho')
  assert.match(fonte('main.jsx'), /Object\.entries\(tokensDaGeometria\(\)\)[^\n]*style\.setProperty\(nome, valor\)/)
})

// --- 3. Camada da conta ------------------------------------------------------------------------

await confere('3  a camada da conta é popover (camada de topo) quando o navegador tem, e fixed quando não tem', () => {
  const jsx = fonte('componentes/MenuDaConta.jsx')
  assert.match(jsx, /'showPopover' in HTMLElement\.prototype/)
  assert.match(jsx, /popover=\{COM_POPOVER \? 'manual' : undefined\}/)
  assert.match(jsx, /camada\.matches\(':popover-open'\)\) return\s*camada\.showPopover\(\)/)
  const css = fonte('componentes/MenuDaConta.module.css')
  assert.match(css, /position: fixed;/, 'o fixed de reserva fica')
  assert.match(css, /inset: auto; margin: 0; overflow: visible;/, 'o popover não centraliza nem rola a camada')
})

// --- 4. Na tela real (opcional) ----------------------------------------------------------------

const { CONSOLE_URL: origem, CONSOLE_USUARIO: usuario, CONSOLE_SENHA: senha } = process.env
if (!origem || !usuario || !senha || !process.env.PLAYWRIGHT_MODULE) {
  console.log('--  medida na tela real pulada: defina CONSOLE_URL, CONSOLE_USUARIO, CONSOLE_SENHA e PLAYWRIGHT_MODULE')
} else {
  const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE)
  const navegador = await chromium.launch()
  const abrir = async (largura, altura) => {
    const contexto = await navegador.newContext({ viewport: { width: largura, height: altura }, serviceWorkers: 'block', locale: 'pt-BR' })
    const pagina = await contexto.newPage()
    await pagina.goto(origem + '/', { waitUntil: 'networkidle' })
    await pagina.fill('input[type=email]', usuario)
    await pagina.fill('input[type=password]', senha)
    await pagina.keyboard.press('Enter')
    await pagina.getByText(/Bo(a|m) (tarde|noite|dia)/).waitFor({ timeout: 20000 })
    return { contexto, pagina }
  }
  const medirTrilho = (pagina, rotulo) => pagina.evaluate((nome) => {
    const nav = document.querySelector(`nav[aria-label="${nome}"]`)
    const itens = [...nav.children].filter((e) => e.matches('a, button, span') && e.getAttribute('aria-label') !== 'Ir para os módulos' && e.getBoundingClientRect().width > 60)
    return { trilho: nav.getBoundingClientRect().width, itens: itens.map((e) => ({ w: Math.round(e.getBoundingClientRect().width), h: e.getBoundingClientRect().height })) }
  }, rotulo)
  try {
    for (const [largura, altura] of [[1280, 720], [1440, 900], [1920, 1080]]) {
      await confere(`4  ${largura}: trilho 100, item 84 e altura mínima 54 px no Balcão e nos módulos`, async () => {
        const { contexto, pagina } = await abrir(largura, altura)
        try {
          await pagina.goto(origem + '/#/m/atendimento', { waitUntil: 'networkidle' })
          await pagina.waitForSelector('nav[aria-label="Navegação"]')
          const balcao = await medirTrilho(pagina, 'Navegação')
          await pagina.goto(origem + '/#/m/cardapio', { waitUntil: 'networkidle' })
          await pagina.waitForSelector('nav[aria-label="Módulos da loja"]')
          const modulos = await medirTrilho(pagina, 'Módulos da loja')
          for (const [nome, medida] of [['Balcão', balcao], ['módulos', modulos]]) {
            assert.equal(medida.trilho, 100, `${nome}: trilho ${medida.trilho}`)
            assert.ok(medida.itens.length >= 5, `${nome}: itens ${medida.itens.length}`)
            for (const item of medida.itens) {
              assert.equal(item.w, 84, `${nome}: item ${item.w} px`)
              assert.ok(item.h >= 54, `${nome}: item com ${item.h} px de altura`)
            }
          }
        } finally { await contexto.close() }
      })
    }
    for (const [nome, esperaMs] of [['com a animação do Modal correndo', 30], ['depois da animação', 900]]) {
      await confere(`4  1024: a camada da conta fica colada ao avatar dentro do menu do tablet, ${nome}`, async () => {
        const { contexto, pagina } = await abrir(1024, 768)
        try {
          await pagina.goto(origem + '/#/m/cardapio', { waitUntil: 'networkidle' })
          await pagina.getByRole('button', { name: /^Menu de / }).click()
          await pagina.waitForTimeout(esperaMs)
          await pagina.locator('dialog button[title$="conta e sair"]').click()
          await pagina.waitForTimeout(40)
          const m = await pagina.evaluate(() => {
            const gatilho = document.querySelector('dialog button[title$="conta e sair"]').getBoundingClientRect()
            const camada = document.querySelector('dialog [aria-label="Conta"]').getBoundingClientRect()
            return { avatarDireita: gatilho.right, avatarBaixo: gatilho.bottom, camadaEsquerda: camada.left, camadaBaixo: camada.bottom }
          })
          assert.ok(Math.abs(m.camadaEsquerda - (m.avatarDireita + 10)) <= 2, `camada ${m.camadaEsquerda} x avatar+10 ${m.avatarDireita + 10}`)
          assert.ok(Math.abs(m.camadaBaixo - m.avatarBaixo) <= 6, `camada embaixo ${m.camadaBaixo} x avatar embaixo ${m.avatarBaixo}`)
          await pagina.keyboard.press('Escape')
          await pagina.waitForTimeout(150)
          assert.equal(await pagina.locator('dialog [aria-label="Conta"]').count(), 0, 'Esc fecha só a camada')
          assert.equal(await pagina.locator('dialog[open]').count(), 1, 'o menu do tablet continua aberto')
        } finally { await contexto.close() }
      })
    }
  } finally { await navegador.close() }
}

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
