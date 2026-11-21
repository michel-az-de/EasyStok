/* eslint-disable no-console */
// Prova da #1716: os blocos da Ficha são irmãos no mesmo pai, então cada um precisa de uma `key` só sua.
// Com duas chaves iguais o React duplica ou esquece blocos ao trocar de conversa, e a Ficha de uma cliente
// ficava com "Link do cliente" e "Ocorrências" da conversa anterior (204 erros de chave no console).
//
//   node ferramentas/prova-1716-ficha-chaves.mjs

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parseSync } from 'vite'

const fonte = readFileSync(new URL('../src/features/ficha-cliente/PainelFicha.jsx', import.meta.url), 'utf8')
// React exige unicidade entre irmãos. Notas, endereço e reconferência pertencem a pais diferentes.
function conferir(texto) {
  const arvore = parseSync('Ficha.jsx', texto), porPai = new Map(), repetidas = []
  assert.deepEqual(arvore.errors, [])
  let quantidade = 0
  function visitar(no, pai = null) {
    if (!no || typeof no !== 'object') return
    if (no.type === 'JSXElement' || no.type === 'JSXFragment') {
      const atributo = no.openingElement?.attributes.find(a => a.type === 'JSXAttribute' && a.name.name === 'key')
      if (atributo) {
        const valor = atributo.value.expression ?? atributo.value
        const chave = texto.slice(valor.start, valor.end)
        if (chave !== 'item.id') {
          quantidade++
          if (pai) {
            const chaves = porPai.get(pai) ?? new Set()
            if (chaves.has(chave)) repetidas.push(chave)
            chaves.add(chave); porPai.set(pai, chaves)
          }
        }
      }
      pai = no
    }
    for (const valor of Object.values(no)) {
      if (Array.isArray(valor)) valor.forEach(filho => visitar(filho, pai))
      else visitar(valor, pai)
    }
  }
  visitar(arvore.program)
  return { quantidade, repetidas }
}
assert.deepEqual(conferir('const ficha = <div><A key={id}/><B key={id}/></div>').repetidas, ['id'])
assert.deepEqual(conferir('const ficha = <div><aside><A key={id}/></aside><main><B key={id}/></main></div>').repetidas, [])
const { quantidade, repetidas } = conferir(fonte)
assert.ok(quantidade >= 5, 'a Ficha deixou de dar chave aos blocos: ' + quantidade)
assert.deepEqual(repetidas, [], 'blocos irmãos da Ficha com a mesma key: ' + repetidas.join(', '))
console.log('ok  ' + quantidade + ' blocos da Ficha, sem chave repetida entre irmãos')
