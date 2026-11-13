/* eslint-disable no-console */
// #2495 (S3, I2): o GET /api/categorias devolve as raízes com as filhas de um nível. O editor de produto
// oferece só as filhas da categoria escolhida, e "Sem subcategoria" grava null.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})
const { categoriasRaiz, subcategoriasDe, mudarDadosCadastro } = await import('../src/dominio/cadastros.js')

const arvore = [
  { id: 'R1', nome: 'Bolos', categoriaPaiId: null, subCategorias: [{ id: 'S1', nome: 'Bolos de pote', categoriaPaiId: 'R1' }, { id: 'S2', nome: 'Bolos de festa', categoriaPaiId: 'R1' }] },
  { id: 'R2', nome: 'Salgados', categoriaPaiId: null, subCategorias: [] },
  { id: 'R3', nome: 'Doces' },
]
assert.deepEqual(categoriasRaiz(arvore).map(o => o.valor), ['R1', 'R2', 'R3'])
assert.deepEqual(subcategoriasDe(arvore, 'R1'), [
  { valor: '', rotulo: 'Sem subcategoria' },
  { valor: 'S1', rotulo: 'Bolos de pote' },
  { valor: 'S2', rotulo: 'Bolos de festa' },
])
assert.deepEqual(subcategoriasDe(arvore, 'R2'), [], 'Sem filhas: o seletor some')
assert.deepEqual(subcategoriasDe(arvore, 'R3'), [], 'Sem a lista de filhas: o seletor some')
assert.deepEqual(subcategoriasDe(arvore, null), [], 'Sem categoria: o seletor some')
assert.deepEqual(subcategoriasDe(undefined, 'R1'), [])

// "Sem subcategoria" vira null no corpo; trocar a categoria zera a sub.
const comSub = mudarDadosCadastro('produtos', { categoriaId: 'R1', subcategoriaId: null }, 'subcategoriaId', 'S1')
assert.equal(comSub.subcategoriaId, 'S1')
assert.equal(mudarDadosCadastro('produtos', comSub, 'subcategoriaId', '').subcategoriaId, null)
assert.equal(mudarDadosCadastro('produtos', comSub, 'categoriaId', 'R2').subcategoriaId, null)
console.log('#2495 S3 domínio: subcategoria só das filhas da categoria escolhida; vazio grava null.')
