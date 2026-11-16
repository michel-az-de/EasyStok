/* eslint-disable no-console */
// #2734: a tag é a mesma em toda a tela. O EasyStok grava a tag normalizada ("Sem glúten" vira "sem_gluten").
// A tela precisa: (1) reconhecer a restrição sem depender de acento, caixa ou "_", senão o alerta some;
// (2) achar a tag pela busca do jeito que ela é lida; (3) mostrar o rótulo, nunca a chave crua.
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

const { ehRestricao, ordenarTags, rotuloDaTag } = await import('../src/dominio/cliente.js')
const { combinaBusca, tagCorrespondida } = await import('../src/dominio/conversa.js')
const { contextoDaLinha } = await import('../src/dominio/linhaDoBalcao.js')
const { RESTRICOES } = await import('../src/infra/catalogo.js')

// O que o servidor devolve depois de gravar a tag (S24): minúsculo, sem acento, "_" no lugar do espaço.
const gravada = (tag) => tag.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim().replace(/\s+/g, '_')

// 1. Toda restrição do catálogo continua restrição depois de gravada. Glúten e Celíaco têm acento: eram as que sumiam.
for (const restricao of RESTRICOES) {
  assert.equal(ehRestricao(gravada(restricao), RESTRICOES), true, `${restricao} gravada como ${gravada(restricao)} segue restrição`)
  assert.equal(ehRestricao(gravada(`Sem ${restricao}`), RESTRICOES), true, `Sem ${restricao} gravada segue restrição`)
}
assert.equal(ehRestricao('alergia_a_camarao', RESTRICOES), true)
assert.equal(ehRestricao('Alergia a lactose', ['Lactose']), true, 'a tag escrita na tela continua valendo')
assert.equal(ehRestricao('CELÍACO', RESTRICOES), true)
for (const comum of ['cliente_antiga', 'Gosta de lasanha', 'pede_as_sextas', ''])
  assert.equal(ehRestricao(comum, RESTRICOES), false, `"${comum}" não é restrição`)
assert.equal(ehRestricao('sem_gluten', []), false)
assert.equal(ehRestricao('sem_gluten'), false)

// Restrição vem primeiro; o resto mantém a ordem em que foi criado.
assert.deepEqual(ordenarTags(['cliente_antiga', 'sem_gluten', 'pede_as_sextas', 'celiaco'], RESTRICOES),
  ['sem_gluten', 'celiaco', 'cliente_antiga', 'pede_as_sextas'])

// 2. A busca acha a tag do jeito que ela aparece na tela (com espaço e com acento) e também pela chave.
const marina = {
  id: 'c1', clienteId: 'a', nome: 'Marina', numeroComanda: 12, canal: 'WhatsApp', contato: '',
  cliente: { telefone: '', tags: ['cliente_antiga', 'sem_gluten'] }, mensagens: [], historico: [],
}
for (const busca of ['cliente antiga', 'Cliente Antiga', 'cliente_antiga', 'antiga', 'sem glúten', 'SEM GLUTEN', 'glúten'])
  assert.equal(combinaBusca(marina, busca), true, `a busca "${busca}" acha a conversa pela tag`)
assert.equal(combinaBusca(marina, 'lasanha'), false)
assert.equal(tagCorrespondida(marina, 'cliente antiga'), 'cliente_antiga')
assert.equal(tagCorrespondida(marina, 'Sem Glúten'), 'sem_gluten')
assert.equal(tagCorrespondida(marina, 'lasanha'), null)
assert.equal(tagCorrespondida(marina, ''), null)

// 3. O cartão da conversa diz por que achou com o rótulo, não com a chave.
assert.equal(rotuloDaTag('cliente_antiga'), 'cliente antiga')
assert.deepEqual(contextoDaLinha({ tagAchada: 'cliente_antiga' }),
  [{ chave: 'tag', texto: 'Tag: cliente antiga', icone: null, tom: 'neutro' }])
assert.deepEqual(contextoDaLinha({ tagAchada: 'Gosta de lasanha' }),
  [{ chave: 'tag', texto: 'Tag: Gosta de lasanha', icone: null, tom: 'neutro' }])
assert.deepEqual(contextoDaLinha({}), [])

console.log(`prova-2734 ok: ${RESTRICOES.length} restrições reconhecidas depois de gravadas, busca e rótulo da tag coerentes`)
