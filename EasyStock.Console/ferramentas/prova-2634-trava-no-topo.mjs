/* eslint-disable no-console */
// Prova da #2634 (resto do item 17 da #1717): com o pedido já criado, o painel do cardápio diz por
// que os pratos estão travados ANTES da lista, logo abaixo do cabeçalho. No rodapé ninguém via, e o
// `title` de cada prato não aparece no toque. Ligação de tela: a prova lê o JSX, como a prova-1587.
//
//   node ferramentas/prova-2634-trava-no-topo.mjs

import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

const jsx = readFileSync(new URL('../src/features/cardapio/PainelCardapio.jsx', import.meta.url), 'utf8')

const linhaTrava = jsx.indexOf('<output className={css.comandaTravada}>{LINHA_COMANDA_TRAVADA}</output>')
const fimCabecalho = jsx.indexOf('</header>', jsx.indexOf('className={css.cabecalhoPainel}'))
const inicioLista = jsx.indexOf('aoAbrirNovo=')

assert.ok(linhaTrava > 0, 'o painel mostra a linha de trava')
assert.equal(jsx.split('{LINHA_COMANDA_TRAVADA}</output>').length - 1, 1, 'a linha de trava aparece uma vez só')
assert.ok(fimCabecalho > 0 && inicioLista > 0, 'cabeçalho e lista encontrados no JSX')
assert.ok(linhaTrava > fimCabecalho && linhaTrava < inicioLista,
  'a linha de trava vem logo depois do cabeçalho e antes da lista de pratos')

console.log('prova-2634: a trava do pedido criado aparece no topo do cardápio, antes dos pratos')
