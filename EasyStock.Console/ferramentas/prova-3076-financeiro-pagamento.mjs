// #3076 (F1): em Nova conta, marcar "Já foi paga/recebida" fazia surgir forma e data do pagamento DEPOIS de Observações,
// fora do grupo e longe da caixa marcada; a tela pulava e a pessoa não via o que apareceu. Os campos vêm logo abaixo da
// caixa, antes de Observações.
//
//   node ferramentas/prova-3076-financeiro-pagamento.mjs
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
const jsx = readFileSync(new URL('../src/features/gestao/financeiro/ModaisFinanceiras.jsx', import.meta.url), 'utf8')
const caixa = jsx.indexOf('{palavras.jaFeito}</label>}')
const campos = jsx.indexOf('{registrarJunto && <div')
const observacoes = jsx.indexOf('<CampoArea rotulo="Observações"')
assert.ok(caixa > 0 && campos > 0 && observacoes > 0, 'marcos do formulário não encontrados')
assert.ok(caixa < campos && campos < observacoes, 'forma e data do pagamento ficam logo abaixo de "Já foi paga", antes de Observações')
console.log('ok prova-3076-financeiro-pagamento')
