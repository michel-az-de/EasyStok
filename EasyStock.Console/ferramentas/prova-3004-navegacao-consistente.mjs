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

const { itensDoTrilho } = await import('../src/dominio/trilhoDoConsole.js')

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
    assert.ok(referencia.length >= 8 && referencia.length <= 17, `trilho fora de 8 a 17 itens: ${referencia.length} (${referencia})`)
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
  assert.equal([...jsx.matchAll(/aria-current=\{[^}]*\? '(\w+)'/g)].map((m) => m[1]).filter((v) => v !== 'page').length, 0, 'o item atual usa aria-current="page" em toda rota')
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

console.log(`\n${passou} conferências passaram.`)
