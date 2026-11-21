/* eslint-disable no-console */
// Prova da #2685 (P1, homologação #1769): a unidade aparece como se escreve (g, kg, ml, L, un) em toda a
// Produção, como a #2258 decidiu para Insumos. Antes, Calculadora, Planejamento, Perdas e o editor de Receitas
// mostravam o código da API: "Precisa 6.000 G · saldo 5.000 G", "60 Un", "tem 4.800 G".
//
// Roda com: node ferramentas/prova-2685-unidade-na-producao.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-2685-unidade-na-producao.mjs
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { pathToFileURL } from 'node:url'

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
    return proximo(url, contexto)
  },
})
const base = process.env.PROVA_SRC ? pathToFileURL(process.env.PROVA_SRC.replace(/[\\/]?$/, '/')) : new URL('../src/', import.meta.url)
const { rotuloDaUnidade } = await import(new URL('aplicacao/insumos.js', base).href)
const fonte = (arquivo) => readFileSync(new URL(`features/gestao/producao/${arquivo}`, base), 'utf8')

let falhas = 0
const caso = (nome, fn) => { try { fn(); console.log(`ok   ${nome}`) } catch (e) { falhas++; console.log(`FALHA ${nome}\n     ${e.message}`) } }

caso('P1: rotuloDaUnidade escreve a unidade como se lê', () => {
  assert.deepEqual(['G', 'Kg', 'Ml', 'L', 'Un'].map(rotuloDaUnidade), ['g', 'kg', 'ml', 'L', 'un'])
})

// Cada tela, a unidade que vem da API e chega ao texto. Nenhuma pode aparecer crua.
const TELAS = {
  'AbaCalculadoraApi.jsx': ['resultado.unidadeDesejada', 'insumo.unidadeReceita', 'insumo.unidadeSaldo'],
  'AbaPlanejamentoApi.jsx': ['i.unidade', 'i.unidadeSaldo'],
  'AbaPerdasApi.jsx': ['o.unidade', 'escolhido.unidade'],
  'AbaReceitasApi.jsx': ['l.unidade'],
}
const escapar = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

for (const [arquivo, expressoes] of Object.entries(TELAS)) {
  caso(`P1 ${arquivo}: unidade passa por rotuloDaUnidade`, () => {
    const src = fonte(arquivo)
    for (const e of expressoes) {
      // value={l.unidade} é o valor do select, não texto; só conta o que vai para a tela.
      const crua = new RegExp(`(?<!=)(\\{|\\$\\{)${escapar(e)}\\}`)
      assert.ok(!crua.test(src), `${e} aparece cru no texto`)
      assert.ok(src.includes(`rotuloDaUnidade(${e})`), `${e} usa rotuloDaUnidade`)
    }
  })
}

caso('P1 AbaReceitasApi.jsx: as opções de unidade do insumo usam o rótulo', () => {
  assert.ok(!/UNIDADES_RECEITA\.map\(\(u\) => \(\{ valor: u, rotulo: u \}\)\)/.test(fonte('AbaReceitasApi.jsx')), 'opção mostra o código cru')
})

console.log(falhas ? `\n${falhas} falha(s)` : '\nprova-2685 ok')
process.exit(falhas ? 1 : 0)
