/* eslint-disable no-console */
// Prova da #2362: checklist de configuração da Produção (Insumos → Receitas).
//   - loja sem insumo: passo 1 ativo, com destino Insumos;
//   - insumo sem custo não conclui o passo 1;
//   - receitas: "N de M pratos com receita", concluído só com todas;
//   - tudo pronto: o checklist some.
//
//   node ferramentas/prova-2362-configuracao-producao.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    return proximo(url, contexto)
  },
})

const { configuracaoDaProducao } = await import('../src/aplicacao/configuracaoProducao.js')

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok    ' + descricao) } catch (e) { falhas.push(descricao); console.log('FALHA ' + descricao + '\n      ' + e.message) }
}

const insumo = (nome, custo) => ({ id: nome, nome, custo })
const prato = (sku, linhas) => ({ sku, nome: sku, linhas })

confere('loja vazia: passo 1 ativo, aponta para Insumos, checklist visível', () => {
  const c = configuracaoDaProducao({ insumos: [], receitas: [prato('a', 0), prato('b', 0)] })
  assert.equal(c.visivel, true)
  assert.deepEqual(c.passos.map((p) => p.id), ['insumos', 'receitas'])
  assert.equal(c.passos[0].feito, false)
  assert.equal(c.ativo, 'insumos')
  assert.equal(c.passos[0].tela, 'insumos')
  assert.match(c.passos[0].detalhe, /nenhum insumo/i)
  assert.equal(c.passos[1].detalhe, '0 de 2 pratos com receita')
})

confere('insumo sem custo não conclui o passo 1', () => {
  const c = configuracaoDaProducao({ insumos: [insumo('farinha', 0.0045), insumo('ovo', null)], receitas: [prato('a', 0)] })
  assert.equal(c.passos[0].feito, false)
  assert.equal(c.ativo, 'insumos')
  assert.equal(c.passos[0].detalhe, '2 insumos, 1 sem custo')
})

confere('insumos completos: passo 2 vira o ativo', () => {
  const c = configuracaoDaProducao({ insumos: [insumo('farinha', 0.0045)], receitas: [prato('a', 3), prato('b', 0)] })
  assert.equal(c.passos[0].feito, true)
  assert.equal(c.passos[0].detalhe, '1 insumo com custo')
  assert.equal(c.ativo, 'receitas')
  assert.equal(c.passos[1].tela, 'receitas')
  assert.equal(c.passos[1].detalhe, '1 de 2 pratos com receita')
})

confere('tudo pronto: checklist some', () => {
  const c = configuracaoDaProducao({ insumos: [insumo('farinha', 0.0045)], receitas: [prato('a', 3)] })
  assert.equal(c.passos.every((p) => p.feito), true)
  assert.equal(c.ativo, null)
  assert.equal(c.visivel, false)
})

confere('cardápio sem prato: passo 2 não conclui e explica', () => {
  const c = configuracaoDaProducao({ insumos: [insumo('farinha', 1)], receitas: [] })
  assert.equal(c.passos[1].feito, false)
  assert.match(c.passos[1].detalhe, /nenhum prato/i)
  assert.equal(c.visivel, true)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
