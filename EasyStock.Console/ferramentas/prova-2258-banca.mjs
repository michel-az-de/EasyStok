// Prova da #2258: pendências apontadas pela banca de revisão (F3, T3, H1, T20, A9/G4).
//
// Lê as fontes de `src` (ou de `PROVA_SRC`, para provar o Red contra o master) e chama as funções
// puras. O desenho é conferido no navegador.
//
// Roda com: node ferramentas/prova-2258-banca.mjs
//   Red:    PROVA_SRC=<pasta src do master> node ferramentas/prova-2258-banca.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { pathToFileURL } from 'node:url'
import { join } from 'node:path'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const RAIZ = process.env.PROVA_SRC ?? new URL('../src', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')
const ler = (caminho) => readFileSync(join(RAIZ, caminho), 'utf8')
const importar = (caminho) => import(pathToFileURL(join(RAIZ, caminho)).href)
const regra = (texto, seletor) => {
  const inicio = texto.indexOf(`${seletor} {`)
  assert.notEqual(inicio, -1, `regra ${seletor} não encontrada`)
  return texto.slice(inicio, texto.indexOf('}', inicio))
}
let ok = 0
const falhas = []
const caso = async (nome, fn) => {
  try { await fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

// ---- F3: valor que não chegou é esqueleto, nunca linha vazia
await caso('F3: o resumo do fechamento e o do dia usam valorDoResumo, sem moeda() solta num <dd>', () => {
  const modal = ler('features/gestao/caixa/ModalFecharCaixa.jsx')
  const aba = ler('features/gestao/caixa/AbaCaixaApi.jsx')
  assert.match(modal, /valorDoResumo\(gaveta\.pixECartao\)/, 'Fora da gaveta sem esqueleto')
  assert.doesNotMatch(modal, /<dd>\{moeda\(/, 'campo do modal ainda mostra moeda(null) como "—"')
  assert.doesNotMatch(aba, /<dd>\{moeda\(/, 'resumo do dia ainda mostra moeda(null) como "—"')
})

await caso('F3: o esqueleto aparece só sem valor e respeita movimento reduzido', () => {
  const valor = ler('features/gestao/caixa/ValorDoResumo.jsx')
  assert.match(valor, /valor != null\) return moeda\(valor\)/)
  assert.match(valor, /css\.esqueletoValor/)
  const css = ler('features/gestao/caixa/abaCaixa.module.css')
  assert.match(regra(css, '.esqueletoValor'), /background:\s*var\(--preenchimento-forte\)/)
  assert.match(css, /@media \(prefers-reduced-motion: reduce\)[^}]*\{[^}]*\.esqueletoValor/s)
})

// ---- T3: a faixa de modo fica fixa acima da conversa, fora do contêiner que rola
await caso('T3: BarraModo vem antes da área que rola e não é sticky nem absoluta', () => {
  const painel = ler('features/atendimento/PainelAtendimento.jsx')
  const barra = painel.indexOf('<BarraModo')
  const rolagem = painel.indexOf('<div className={css.rolagemCentro}>')
  assert.ok(barra !== -1 && rolagem !== -1 && barra < rolagem, 'BarraModo não está antes de rolagemCentro')
  const css = ler('features/atendimento/atendimento.module.css')
  assert.match(regra(css, '.modo'), /flex-shrink:\s*0/)
  assert.doesNotMatch(regra(css, '.modo'), /position:\s*(sticky|absolute|fixed)/)
  assert.match(regra(css, '.rolagemCentro'), /overflow-y:\s*auto/)
})

// ---- H1: o resumo não afirma mais do que mede
await caso('H1: "precisam de você" é só conversa; fila e prontos são "em andamento"', async () => {
  const estado = await importar('dominio/estadoDoHall.js')
  const conversa = { situacao: 'Assumida', aguardaResposta: true }
  const lido = (dados) => ({ estado: 'ok', dados, em: 1 })
  const fontes = {
    atendimento: lido({ itens: [conversa, conversa, conversa, { situacao: 'Encerrada' }] }),
    pedidos: lido({ itens: [{ status: 'aguardando' }, { status: 'preparando' }, { status: 'pronto' }, { status: 'entregue' }] }),
  }
  assert.equal(estado.resumoDoDia(fontes).texto, 'Hoje · 3 precisam de você · 3 em andamento')
  assert.equal(estado.resumoDoDia(fontes, ['atendimento']).texto, 'Hoje · 3 precisam de você')
  assert.equal(estado.resumoDoDia(fontes, ['cozinha', 'entregas']).texto, 'Hoje · nada precisa de você · 3 em andamento')
  assert.equal(estado.resumoDoDia({ atendimento: lido({ itens: [] }), pedidos: lido({ itens: [] }) }).texto, 'Hoje · nada precisa de você')
})

// ---- T20: comanda aberta sem totalApi usa o total calculado
await caso('T20: totalDaComanda soma as linhas e não chuta sem preço gravado', async () => {
  const pedido = await importar('dominio/pedido.js')
  assert.equal(pedido.totalDaComanda?.({ itens: [{ qtd: 2, precoUnitario: 10 }, { qtd: 1, precoUnitario: 5.5 }] }), 25.5)
  assert.equal(pedido.totalDaComanda?.({ itens: [{ qtd: 1 }] }), null)
  assert.equal(pedido.totalDaComanda?.({ itens: [] }), null)
})

await caso('T20: a Ficha mostra "1 comanda aberta · R$ X" com o total calculado', () => {
  const ficha = ler('features/ficha-cliente/ModalHistorico.jsx')
  assert.match(ficha, /totalApi : totalDaComanda\(pedido\)/)
  assert.match(ficha, /` · \$\{moeda\(aberta\.total\)\}`/)
})

// ---- A9/G4: o menu ⋮ compacto usa as mesmas opções e a mesma ordem do menu do avatar
await caso('A9/G4: Tema e Sair do compacto vêm da mesma fonte do MenuDaConta', async () => {
  const fonteMenu = ler('app/menuMais.js')
  assert.match(fonteMenu, /contaDoMenu/, 'menuMais não usa a fonte compartilhada')
  assert.match(ler('componentes/InterruptorTema.jsx'), /contaDoMenu/, 'InterruptorTema não usa a fonte compartilhada')
  const compartilhada = await importar('componentes/contaDoMenu.js')
  const menu = await importar('app/menuMais.js')
  const grupos = menu.gruposDoMenuMais({ tema: 'dark', permite: () => true, aoSair: () => {}, sessaoPersistente: true, atendente: 'Ana', casa: 'Loja 1' })
  const tema = grupos.find((g) => g.titulo === 'Tema')
  assert.deepEqual(tema.itens.map((i) => i.titulo), compartilhada.OPCOES_DE_TEMA.map((o) => o.rotulo))
  const posTema = grupos.indexOf(tema)
  const sair = grupos.flatMap((g) => g.itens).find((i) => i.chave === '__sair')
  assert.equal(sair.titulo, compartilhada.rotuloDoSair(true))
  assert.ok(posTema < grupos.findIndex((g) => g.itens.includes(sair)), 'Sair antes do Tema')
})

console.log(`\n${ok} casos ok${falhas.length ? `, ${falhas.length} falharam` : ''}`)
if (falhas.length) process.exitCode = 1
