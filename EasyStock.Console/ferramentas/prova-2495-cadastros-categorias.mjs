/* eslint-disable no-console */
// #2495 (S4, I3): regras da tela Cadastros › Categorias de produto. O GET /api/categorias devolve as raízes
// com as filhas de um nível, e a API não impede ciclo nem terceiro nível: a tela guarda isso.
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
const {
  arvoreDeCategorias, destinosParaMover, bloqueioDeExclusao, corpoDaCategoria, corpoDaNovaCategoria,
  mensagemDaCategoria, sucessoDaCategoria, erroDoNomeDaCategoria,
} = await import('../src/dominio/categoriasProduto.js')

const api = [
  { id: 'R2', nome: 'salgados', categoriaPaiId: null, descricao: 'Assados', subCategorias: [] },
  { id: 'R1', nome: 'Bolos', categoriaPaiId: null, subCategorias: [
    { id: 'S2', nome: 'Bolos de pote', categoriaPaiId: 'R1', descricao: 'Pote 300 ml' },
    { id: 'S1', nome: 'Bolos de festa', categoriaPaiId: 'R1' },
  ] },
  { id: 'R3', nome: 'Doces', categoriaPaiId: null },
  { id: 'S9', nome: 'Solta', categoriaPaiId: 'R1' },
]

// Árvore: só raízes no topo, por nome sem diferenciar maiúscula; filhas também por nome.
const arvore = arvoreDeCategorias(api)
assert.deepEqual(arvore.map(c => c.id), ['R1', 'R3', 'R2'])
assert.deepEqual(arvore[0].subCategorias.map(c => c.id), ['S1', 'S2'])
assert.deepEqual(arvore[1].subCategorias, [], 'Sem a lista de filhas vira lista vazia')
assert.deepEqual(arvoreDeCategorias(undefined), [])

// Mover: destino é sempre raiz, nunca a própria nem a atual; raiz com filhas não se move.
const [bolos, doces, salgados] = arvore
const pote = bolos.subCategorias[1]
assert.deepEqual(destinosParaMover(arvore, pote).map(o => o.valor), ['R3', 'R2'])
assert.deepEqual(destinosParaMover(arvore, doces).map(o => o.valor), ['R1', 'R2'], 'Raiz sem filhas pode virar subcategoria')
assert.deepEqual(destinosParaMover(arvore, bolos), [], 'Raiz com filhas criaria terceiro nível')

// Excluir: raiz com filhas fica bloqueada na tela.
assert.equal(bloqueioDeExclusao(bolos), 'Mova ou exclua as subcategorias antes.')
assert.equal(bloqueioDeExclusao(doces), null)
assert.equal(bloqueioDeExclusao(pote), null)

// Corpo: o PUT troca tudo, então leva a descrição e o pai atuais junto do que mudou.
assert.deepEqual(corpoDaCategoria(pote, { nome: '  Potes  ' }, 'E'), { id: 'S2', empresaId: 'E', nome: 'Potes', descricao: 'Pote 300 ml', categoriaPaiId: 'R1' })
assert.deepEqual(corpoDaCategoria(pote, { categoriaPaiId: 'R3' }, 'E'), { id: 'S2', empresaId: 'E', nome: 'Bolos de pote', descricao: 'Pote 300 ml', categoriaPaiId: 'R3' })
assert.deepEqual(corpoDaCategoria(salgados, { nome: 'Salgados' }, 'E'), { id: 'R2', empresaId: 'E', nome: 'Salgados', descricao: 'Assados', categoriaPaiId: null })
assert.deepEqual(corpoDaNovaCategoria(' Tortas ', undefined, 'E'), { empresaId: 'E', nome: 'Tortas', descricao: null, categoriaPaiId: null })
assert.deepEqual(corpoDaNovaCategoria('Mini', 'R1', 'E'), { empresaId: 'E', nome: 'Mini', descricao: null, categoriaPaiId: 'R1' })

// Mensagens: a do servidor aparece; FK de subcategoria em uso (500) vira texto que diz o que fazer.
const duplicado = Object.assign(new Error('Já existe uma categoria chamada "Bolos".'), { status: 400 })
assert.equal(mensagemDaCategoria('criar', null, duplicado), 'Já existe uma categoria chamada "Bolos".')
assert.match(mensagemDaCategoria('excluir', pote, Object.assign(new Error('Erro interno'), { status: 500 })), /Se algum produto usa esta subcategoria, troque a subcategoria dele antes/)
assert.match(mensagemDaCategoria('excluir', doces, Object.assign(new Error('Erro interno'), { status: 500 })), /Não deu para excluir Doces/)
assert.match(mensagemDaCategoria('renomear', pote, Object.assign(new Error('x'), { status: 403 })), /Seu acesso não permite/)

// Nome: vazio e acima do limite do banco (120) viram erro no campo, sem cortar o texto colado.
assert.equal(erroDoNomeDaCategoria('   '), 'Informe o nome.')
assert.equal(erroDoNomeDaCategoria('x'.repeat(121)), 'Use até 120 caracteres. Agora tem 121.')
assert.equal(erroDoNomeDaCategoria(' Bolos '), null)

// Sucesso diz o que mudou, sem concordar gênero com o nome.
assert.equal(sucessoDaCategoria({ acao: 'criar', pai: null }, ' Tortas ', arvore), 'Categoria Tortas criada.')
assert.equal(sucessoDaCategoria({ acao: 'criar', pai: bolos }, 'Mini', arvore), 'Subcategoria Mini criada em Bolos.')
assert.equal(sucessoDaCategoria({ acao: 'renomear', categoria: pote }, 'Potes', arvore), 'Bolos de pote agora se chama Potes.')
assert.equal(sucessoDaCategoria({ acao: 'mover', categoria: pote }, 'R3', arvore), 'Subcategoria Bolos de pote movida para Doces.')
assert.equal(sucessoDaCategoria({ acao: 'excluir', categoria: doces }, '', arvore), 'Categoria Doces excluída.')
console.log('#2495 S4 domínio: árvore de dois níveis, mover só para raiz, excluir raiz com filhas bloqueado, corpo completo no PUT.')
