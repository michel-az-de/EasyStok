/* eslint-disable no-console */
// #3206: regras puras da lista de mensagens padrão do compositor (nome legível, prévia, origem, podeEnviarModelo).
//   node ferramentas/prova-3206-mensagens-padrao-lista.mjs
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
})
const m = await import('../src/dominio/mensagensPadrao.js')

// 1 nome legível
assert.equal(m.nomeLegivel('boas_vindas_loja'), 'Boas vindas loja')
assert.equal(m.nomeLegivel('pedido__pronto'), 'Pedido pronto')
assert.equal(m.nomeLegivel(''), '')
assert.equal(m.nomeLegivel(null), '')

// 2 parâmetros e prévia ao vivo
const modelo = { nome: 'x', cabecalho: 'Olá {{1}}', corpo: 'Pedido {{2}} sai às {{3}}.', rodape: 'Loja {{1}}', quantidadeParametros: 3 }
assert.deepEqual(m.numerosDosParametros(modelo), [1, 2, 3])
assert.deepEqual(m.numerosDosParametros({ corpo: 'sem campos' }), [])
assert.deepEqual(m.numerosDosParametros({ corpo: 'a', quantidadeParametros: 2 }), [1, 2])
assert.deepEqual(m.previaDoModelo(modelo, ['Ana', '', ' 18h ']), { cabecalho: 'Olá Ana', corpo: 'Pedido {{2}} sai às 18h.', rodape: 'Loja Ana' })
assert.deepEqual(m.previaDoModelo({ corpo: 'oi' }, []), { cabecalho: '', corpo: 'oi', rodape: '' })
assert.equal(m.parametrosCompletos(modelo, ['Ana', '12', '18h']), true)
assert.equal(m.parametrosCompletos(modelo, ['Ana', ' ', '18h']), false)
assert.equal(m.parametrosCompletos(modelo, ['Ana', '{{2}}', '18h']), false)
assert.equal(m.parametrosCompletos({ corpo: 'oi' }, []), true)

// 3 filtro por origem: ausente conta como Meta
const lista = [{ nome: 'a' }, { nome: 'b', origem: 'Meta' }, { nome: 'c', origem: 'Local' }]
assert.deepEqual(m.soOrigemMeta(lista).map((x) => x.nome), ['a', 'b'])
assert.deepEqual(m.soOrigemMeta(null), [])

// 3b vazio do seletor: catálogo sem itens é diferente de catálogo só com mensagem padrão da loja (Local)
assert.equal(m.motivoDaListaVazia([]), 'catalogo-vazio')
assert.equal(m.motivoDaListaVazia(null), 'catalogo-vazio')
assert.equal(m.motivoDaListaVazia([{ nome: 'c', origem: 'Local' }, { nome: 'd', origem: 'Local' }]), 'so-locais')
assert.equal(m.motivoDaListaVazia(lista), null)
assert.equal(m.motivoDaListaVazia([{ nome: 'a' }]), null)

// 4 podeEnviarModelo não exige clienteId
const base = { fonteApi: true, podeReabrir: true, aceitaModelo: true, encerrada: false, bloqueada: false }
assert.equal(m.podeEnviarModelo(base), true)
assert.equal(m.podeEnviarModelo({ ...base, clienteId: undefined }), true)
for (const k of ['fonteApi', 'podeReabrir', 'aceitaModelo']) assert.equal(m.podeEnviarModelo({ ...base, [k]: false }), false, k)
for (const k of ['encerrada', 'bloqueada']) assert.equal(m.podeEnviarModelo({ ...base, [k]: true }), false, k)
console.log('prova-3206: ok')
