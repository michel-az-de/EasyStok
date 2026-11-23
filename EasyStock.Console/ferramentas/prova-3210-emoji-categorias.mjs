/* eslint-disable no-console */
// Prova da #3210 (S85): o seletor de emoji por categoria, com busca em todas elas. Regras puras (dominio/emojis).
//
//   node ferramentas/prova-3210-emoji-categorias.mjs
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const { CATEGORIAS_DE_EMOJI, buscarEmTodasAsCategorias } = await import('../src/dominio/emojis.js')
const { EMOJIS_DA_LOJA, buscarEmojis } = await import('../src/dominio/acoesNaMensagem.js')

assert.ok(CATEGORIAS_DE_EMOJI.length >= 6 && CATEGORIAS_DE_EMOJI.length <= 8, 'cerca de 7 categorias')
assert.equal(CATEGORIAS_DE_EMOJI[0].nome, 'Mais usados')
assert.deepEqual(CATEGORIAS_DE_EMOJI[0].emojis, EMOJIS_DA_LOJA, 'a primeira categoria é a lista da loja')
assert.equal(new Set(CATEGORIAS_DE_EMOJI.map((c) => c.id)).size, CATEGORIAS_DE_EMOJI.length, 'ids únicos')
for (const { id, nome, emojis } of CATEGORIAS_DE_EMOJI) {
  assert.ok(id && nome, 'categoria com id e nome')
  assert.ok(emojis.length >= 20 && emojis.length <= 40, `${nome}: 20 a 40 emojis (${emojis.length})`)
  assert.equal(new Set(emojis.map((e) => e.emoji)).size, emojis.length, `${nome}: sem emoji duplicado`)
  for (const e of emojis) assert.ok(e.emoji && e.nome && e.nome === e.nome.toLowerCase(), `${nome}: ${e.emoji} com nome em minúsculas`)
}

// Busca em todas as categorias, sem acento, com ":", sem repetir.
const emojis = (termo) => buscarEmTodasAsCategorias(termo).map((e) => e.emoji)
assert.ok(emojis('coracao').includes('❤️'), 'sem acento')
assert.ok(emojis('CORAÇÃO').includes('❤️'), 'sem caixa')
assert.deepEqual(emojis(':coracao'), emojis('coração'), 'os dois-pontos valem')
assert.ok(emojis('pizza').length >= 1, 'acha emoji de outra categoria')
const todos = CATEGORIAS_DE_EMOJI.flatMap((c) => c.emojis.map((e) => e.emoji))
const repetidoEntreCategorias = todos.length - new Set(todos).size
assert.ok(repetidoEntreCategorias > 0, 'o catálogo repete emoji entre categorias (Mais usados); a busca não pode')
const tudo = emojis('')
assert.equal(tudo.length, new Set(todos).size, 'sem termo, todos, uma vez cada')
for (const t of ['a', 'o', 'e', ':a']) assert.equal(new Set(emojis(t)).size, emojis(t).length, `"${t}" sem repetição`)
assert.deepEqual(buscarEmTodasAsCategorias('xyzxyz'), [], 'nada casa, nada volta')
// A busca da loja não mudou.
assert.equal(EMOJIS_DA_LOJA.length, 24)
assert.deepEqual(buscarEmojis(':mas').map((e) => e.emoji), ['🍝'])
console.log('ok #3210: catálogo por categoria, busca em todas, sem acento, com ":" e sem repetição')
