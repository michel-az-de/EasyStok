/* eslint-disable no-console */
// Prova da #1716: os blocos da Ficha são irmãos no mesmo pai, então cada um precisa de uma `key` só sua.
// Com duas chaves iguais o React duplica ou esquece blocos ao trocar de conversa, e a Ficha de uma cliente
// ficava com "Link do cliente" e "Ocorrências" da conversa anterior (204 erros de chave no console).
//
//   node ferramentas/prova-1716-ficha-chaves.mjs

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const fonte = readFileSync(new URL('../src/features/ficha-cliente/PainelFicha.jsx', import.meta.url), 'utf8')
// Chave de item de lista (`.map`) repete a expressão por desenho; as demais são de blocos fixos da Ficha.
const chaves = [...fonte.matchAll(/\bkey=\{([^}]+(?:\{[^}]*\}[^}]*)*)\}/g)].map((m) => m[1].trim()).filter((k) => k !== 'item.id')

assert.ok(chaves.length >= 5, 'a Ficha deixou de dar chave aos blocos: ' + chaves.length)
const repetidas = chaves.filter((k, i) => chaves.indexOf(k) !== i)
assert.deepEqual(repetidas, [], 'blocos irmãos da Ficha com a mesma key: ' + repetidas.join(', '))
console.log('ok  ' + chaves.length + ' blocos da Ficha, cada um com a sua chave')
