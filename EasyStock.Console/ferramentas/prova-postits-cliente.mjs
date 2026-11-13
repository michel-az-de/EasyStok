import assert from 'node:assert/strict'
import { guardarPosicaoPostit, lerPosicaoPostit, limitarPostit, recolherPostit } from '../src/aplicacao/posicoesDosPostits.js'

const memoria = new Map()
const armazenamento = { getItem: (k) => memoria.get(k) ?? null, setItem: (k, v) => memoria.set(k, v), removeItem: (k) => memoria.delete(k) }
assert.equal(guardarPosicaoPostit('conversa-a', 'nota-1', { x: 120, y: 80, texto: 'Dado privado', autor: 'Pessoa' }, armazenamento), true)
assert.deepEqual(lerPosicaoPostit('conversa-a', 'nota-1', armazenamento), { x: 120, y: 80 })
assert.deepEqual([...memoria.values()].map(JSON.parse), [{ x: 120, y: 80 }], 'somente coordenadas são gravadas, sem texto ou autoria')
assert.doesNotMatch([...memoria.keys()].join(' '), /Dado privado|Pessoa/)
console.log('ok posição sobrevive à releitura sem armazenar conteúdo da nota')

assert.equal(lerPosicaoPostit('conversa-b', 'nota-1', armazenamento), null)
assert.equal(lerPosicaoPostit('conversa-a', 'nota-2', armazenamento), null)
guardarPosicaoPostit('conversa-b', 'nota-1', { x: 8, y: 8 }, armazenamento)
assert.equal(recolherPostit('conversa-a', 'nota-1', armazenamento), true)
assert.equal(lerPosicaoPostit('conversa-a', 'nota-1', armazenamento), null)
assert.deepEqual(lerPosicaoPostit('conversa-b', 'nota-1', armazenamento), { x: 8, y: 8 }, 'recolher uma nota não remove a preferência de outra conversa')
console.log('ok preferências isoladas por conversa e nota; recolher afeta só a nota escolhida')

for (const janela of [{ largura: 1440, altura: 900 }, { largura: 390, altura: 844 }, { largura: 320, altura: 568 }]) {
  const nota = { largura: 280, altura: 240 }
  for (const tentativa of [{ x: -500, y: -500 }, { x: 4000, y: 3000 }, { x: 100, y: 90 }]) {
    const p = limitarPostit(tentativa, nota, janela)
    assert.ok(p.x >= 0 && p.y >= 0, 'nota não sai pelo topo ou pela esquerda')
    assert.ok(p.x + nota.largura <= janela.largura && p.y + nota.altura <= janela.altura, 'nota permanece acessível inteira depois de mudar a janela')
  }
}
console.log('ok arraste e restauração ficam dentro das janelas desktop, 390 e 320')

assert.equal(guardarPosicaoPostit('a', 'b', { x: Infinity, y: 0 }, armazenamento), false)
assert.equal(guardarPosicaoPostit('a', 'b', { x: 0, y: NaN }, armazenamento), false)
assert.equal(guardarPosicaoPostit('a', null, { x: 0, y: 0 }, armazenamento), false)
const corrompido = { getItem: () => '{quebrado' }
assert.equal(lerPosicaoPostit('a', 'b', corrompido), null)
const bloqueado = { getItem() { throw Error('bloqueado') }, setItem() { throw Error('bloqueado') }, removeItem() { throw Error('bloqueado') } }
assert.equal(lerPosicaoPostit('a', 'b', bloqueado), null)
assert.equal(guardarPosicaoPostit('a', 'b', { x: 8, y: 8 }, bloqueado), false)
assert.equal(recolherPostit('a', 'b', bloqueado), false)
console.log('ok dados inválidos e armazenamento bloqueado não quebram a ficha')
console.log('4 grupos de comportamento dos post-its aprovados.')
