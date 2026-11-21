// Prova da #3004: a navegação do Console é a mesma em todas as telas.
//
// Regras puras (`dominio/trilhoDoConsole.js`): os itens do trilho têm a mesma lista e a mesma ordem no
// Início, no Balcão, em Entregas e na Cozinha; o que só vale no Balcão vira link para ele ("<rótulo>, abre
// no Balcão"), e não some nem vira parada de Tab morta. Por leitura do fonte, os pontos que só existem no JSX e no CSS: o tema
// mora só no menu da conta (que existe também no Início, com Ajuda e Sair), os rótulos do trilho não
// hifenizam e nenhum destaque usa barra colorida de um lado só.
//
// Roda com: node ferramentas/prova-3004-navegacao-consistente.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const { itensDoTrilho, TETO_ITENS_DO_TRILHO } = await import('../src/dominio/trilhoDoConsole.js')

const TELAS = {
  inicio: 'hall',
  balcao: 'balcao',
  entregas: { moduloId: 'entregas', telaId: 'painel' },
  cozinha: { moduloId: 'cozinha', telaId: 'fila' },
}
const ids = (itens) => itens.map((item) => item.id)

for (const fonteApi of [false, true]) {
  const modo = fonteApi ? 'modo API' : 'demonstração'
  const base = { permite: () => true, acoes: null, fonteApi, simulacoes: !fonteApi }

  confere(`${modo}: Início, Balcão, Entregas e Cozinha têm os mesmos itens, na mesma ordem`, () => {
    const referencia = ids(itensDoTrilho({ ...base, onde: TELAS.inicio }))
    // #3004: teto do trilho (medido: 16 itens em modo API e 17 com Simular na demonstração; a revisão estimou 13, abaixo do real): o trilho não pode
    // crescer sem a prova dizer, porque ele entra na contagem de controles da primeira vista do Início.
    assert.ok(referencia.length >= 8 && referencia.length <= TETO_ITENS_DO_TRILHO, `trilho fora de 8 a ${TETO_ITENS_DO_TRILHO} itens: ${referencia.length} (${referencia})`)
    for (const [nome, onde] of Object.entries(TELAS)) {
      assert.deepEqual(ids(itensDoTrilho({ ...base, onde })), referencia, nome)
    }
  })

  confere(`${modo}: cada tela marca exatamente um item como atual`, () => {
    const atual = (onde) => itensDoTrilho({ ...base, onde }).filter((item) => item.atual).map((item) => item.id)
    assert.deepEqual(atual(TELAS.inicio), ['inicio'])
    assert.deepEqual(atual(TELAS.balcao), ['atendimento'])
    assert.deepEqual(atual(TELAS.entregas), ['entregas'])
    assert.deepEqual(atual(TELAS.cozinha), ['cozinha'])
    assert.deepEqual(atual({ moduloId: 'atendimento', telaId: 'respostas' }), ['respostas'])
  })
}

confere('o que é do Balcão vira link para o Balcão fora dele, sem item desabilitado', () => {
  const base = { permite: () => true, fonteApi: false, simulacoes: true }
  const doBalcao = ['loja', 'gestao', 'lembretes', 'disposicao', 'simular']
  for (const onde of [TELAS.inicio, TELAS.entregas, TELAS.cozinha]) {
    const itens = itensDoTrilho({ ...base, onde })
    assert.ok(!itens.some((item) => item.desabilitado), 'sobrou item desabilitado')
    for (const id of doBalcao) {
      const item = itens.find((outro) => outro.id === id)
      assert.ok(item, `${id} sumiu`)
      assert.equal(item.href, '#/m/atendimento/balcao', id)
      assert.ok(item.nome.startsWith(item.rotulo) && item.nome.endsWith('abre no Balcão'), `${id}: nome ${item.nome}`)
    }
  }
  const noBalcao = itensDoTrilho({ ...base, onde: TELAS.balcao })
  for (const id of doBalcao) assert.ok(!noBalcao.find((item) => item.id === id).soNoBalcao, `${id} aponta ao Balcão dentro dele`)
  const jsx = fonte('features/hall/TrilhoDoConsole.jsx')
  assert.doesNotMatch(jsx, /aria-disabled/)
  assert.doesNotMatch(jsx, /loja: 'lock'/, 'lock no Balcão significa loja fechada')
  const valores = [...jsx.matchAll(/aria-current(?:=(\{[^}]*\}|"[^"]*"))?/g)].map((m) => m[1] ?? '(booleano)')
  assert.ok(valores.length > 0, 'nenhum aria-current no trilho')
  for (const valor of valores) {
    const textos = valor.startsWith('{') ? [...valor.matchAll(/'([^']*)'/g)].map((m) => m[1]).filter((v) => v !== '') : [valor.replaceAll('"', '')]
    assert.ok(textos.length > 0 && textos.every((v) => v === 'page'), `aria-current deve renderizar "page", não ${valor}`)
  }
})

confere('os itens que levam a uma tela são links iguais em todas as rotas', () => {
  const base = { permite: () => true, fonteApi: false }
  const links = (onde) => itensDoTrilho({ ...base, onde }).filter((item) => item.href).map((item) => `${item.id}=${item.href}`)
  assert.deepEqual(links(TELAS.entregas), links(TELAS.inicio))
  assert.ok(links(TELAS.inicio).includes('inicio=#/'))
})

confere('Ajuda e Tema não são itens do trilho: moram no menu da conta', () => {
  const todos = ids(itensDoTrilho({ permite: () => true, fonteApi: false, simulacoes: true, onde: TELAS.inicio }))
  assert.ok(!todos.includes('ajuda') && !todos.includes('tema'))
})

confere('área que o perfil não abre some em todas as telas, igual', () => {
  const base = { permite: (id) => id !== 'financeiro', fonteApi: false }
  const a = ids(itensDoTrilho({ ...base, onde: TELAS.inicio }))
  assert.ok(!a.includes('financeiro'))
  assert.deepEqual(ids(itensDoTrilho({ ...base, onde: TELAS.cozinha })), a)
})

confere('o trilho único é o dos três lugares: Início, Balcão e módulos', () => {
  assert.match(fonte('features/hall/HallDeModulos.jsx'), /<TrilhoDoConsole/)
  assert.match(fonte('features/hall/MenuDosModulos.jsx'), /<TrilhoDoConsole/)
  assert.match(fonte('app/Moldura.jsx'), /<TrilhoDoConsole/)
})

confere('o tema mora só no menu da conta: o pé do Início não o repete', () => {
  assert.doesNotMatch(fonte('features/hall/HallDeModulos.jsx'), /InterruptorTema/)
  assert.doesNotMatch(fonte('features/hall/MenuDosModulos.jsx'), /InterruptorTema/)
  assert.match(fonte('componentes/MenuDaConta.jsx'), /InterruptorTema/)
})

confere('a conta existe mesmo sem sessão nomeada (demonstração) e leva a Ajuda', () => {
  const menu = fonte('componentes/MenuDaConta.jsx')
  assert.doesNotMatch(menu, /if \(!nome\) return null/)
  assert.match(menu, /aoAbrirAjuda/)
})

confere('rótulos do trilho sem hifenização e sem cortar a palavra', () => {
  const css = fonte('componentes/Trilho.module.css')
  assert.doesNotMatch(css, /hyphens:\s*auto/)
  assert.match(css, /hyphens:\s*none/)
  assert.doesNotMatch(css, /text-overflow:\s*ellipsis/)
})

confere('nenhum destaque do trilho usa barra ou borda de um lado só', () => {
  for (const arquivo of ['componentes/Trilho.module.css', 'features/hall/moldura.module.css', 'componentes/MenuDaConta.module.css']) {
    assert.doesNotMatch(fonte(arquivo), /border-(left|right|inline-start|inline-end)\s*:\s*(?!0|none)[^;]*(solid|var\(--b-)/, arquivo)
  }
})

console.log(`\n${passou} conferências passaram.`)// Foco do trilho: o anel de foco não pode sair da área de recorte do trilho (os itens ficam em `.itens`, com
// overflow-x escondido). Um outline de 3 px com offset positivo num item de 72 px dentro de 72 px de recorte perdia o anel
// dos lados (medido a 1440 px: 5 px de corte por lado). O anel nasce para dentro do item (offset negativo).
confere('o anel de foco do trilho nasce para dentro do item (offset <= 0), sem ser recortado', () => {
  const css = fonte('componentes/Trilho.module.css')
  const regra = css.match(/\.inicio:focus-visible, \.item:focus-visible\s*\{([^}]*)\}/)
  assert.ok(regra, 'regra de foco do trilho não encontrada')
  const deslocamento = regra[1].match(/outline-offset:\s*([^;]+);/)
  assert.ok(deslocamento && /^(0|-|calc\(-)/.test(deslocamento[1].trim()), `outline-offset do trilho deve ser <= 0, está ${deslocamento?.[1]}`)
})

// Medida na tela real (demonstração, sem login): CONSOLE_URL=http://localhost:5731 PLAYWRIGHT_MODULE=<pasta do playwright>.
if (!process.env.CONSOLE_URL || !process.env.PLAYWRIGHT_MODULE) {
  console.log('--  medida do foco na tela real pulada: defina CONSOLE_URL e PLAYWRIGHT_MODULE')
} else {
  const { createRequire } = await import('node:module')
  const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE)
  const navegador = await chromium.launch()
  try {
    const pagina = await navegador.newPage({ viewport: { width: 1440, height: 900 } })
    for (const rota of ['#/', '#/m/atendimento/balcao']) {
      await pagina.goto(process.env.CONSOLE_URL.replace(/\/$/, '') + '/' + rota)
      await pagina.waitForSelector('nav[aria-label="Navegação"] a')
      const links = await pagina.locator('nav[aria-label="Navegação"] a').count()
      for (const indice of [0, Math.min(2, links - 1)]) {
        await pagina.locator('nav[aria-label="Navegação"] a').nth(indice).focus()
        await pagina.keyboard.press('Shift+Tab')
        await pagina.keyboard.press('Tab')
        const corte = await pagina.evaluate(() => {
          const el = document.activeElement
          const css = getComputedStyle(el)
          const caixa = el.getBoundingClientRect()
          const anel = parseFloat(css.outlineOffset) + parseFloat(css.outlineWidth)
          let recorte = el.parentElement
          while (recorte && getComputedStyle(recorte).overflowX === 'visible') recorte = recorte.parentElement
          const area = recorte.getBoundingClientRect()
          return { nome: el.textContent.trim(), esquerda: area.left - (caixa.left - anel), direita: caixa.right + anel - area.right }
        })
        assert.ok(corte.esquerda <= 0 && corte.direita <= 0, `${rota} ${corte.nome}: anel de foco recortado (${corte.esquerda}px à esquerda, ${corte.direita}px à direita)`)
      }
    }
    passou += 1
    console.log('ok  foco do trilho inteiro dentro do recorte, a 1440 px, no Início e no Balcão')
  } finally { await navegador.close() }
}


