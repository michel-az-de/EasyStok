// #3156 (re-pontuação da #3076): "Fechar aviso" da loja desligada valia só até recarregar. Agora a dispensa fica no
// aparelho (localStorage, em try/catch: sem armazenamento vale só na visita), amarrada ao texto do aviso: texto novo
// volta a aparecer, e o cardápio lido de novo apaga a dispensa.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
})
const { lerDispensaDaLoja, gravarDispensaDaLoja } = await import('../src/aplicacao/dispensaDaLoja.js')

const guardado = new Map()
const armazenamento = { getItem: (k) => guardado.get(k) ?? null, setItem: (k, v) => guardado.set(k, v), removeItem: (k) => guardado.delete(k) }
const AVISO = 'A loja online está desligada.'

gravarDispensaDaLoja('A', AVISO, armazenamento)
assert.equal(lerDispensaDaLoja('A', armazenamento), AVISO, 'a dispensa guarda o texto do aviso fechado')
assert.equal(guardado.size, 1, 'uma chave só no aparelho')
// A dispensa é da empresa: fechar o aviso na empresa A não esconde o da empresa B no mesmo aparelho.
assert.equal(lerDispensaDaLoja('B', armazenamento), '', 'a dispensa da empresa A não vale para a B')
gravarDispensaDaLoja('A', '', armazenamento)
assert.equal(lerDispensaDaLoja('A', armazenamento), '', 'vazio apaga a dispensa')
assert.equal(guardado.size, 0, 'a chave sai do armazenamento')

// Sem armazenamento (modo privado, cota cheia): a dispensa vale na visita e a tela segue de pé.
const quebrado = { getItem() { throw new Error('bloqueado') }, setItem() { throw new Error('cota') }, removeItem() { throw new Error('bloqueado') } }
gravarDispensaDaLoja('A', AVISO, quebrado)
assert.equal(lerDispensaDaLoja('A', quebrado), AVISO, 'sem armazenamento a dispensa vale na visita')
assert.equal(lerDispensaDaLoja('B', quebrado), '', 'sem armazenamento a dispensa da A também não vale para a B')
gravarDispensaDaLoja('A', '', quebrado)
assert.equal(lerDispensaDaLoja('A', quebrado), '')
console.log('prova-3156-faixas-pe: ok')
