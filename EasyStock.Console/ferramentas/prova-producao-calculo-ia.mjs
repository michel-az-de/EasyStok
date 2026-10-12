// Regressões de cálculo e de revisão explícita das sugestões públicas.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
registerHooks({
  resolve(s, c, proximo) {
    if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return proximo(s + '.js', c) } catch { /* resolução original */ } }
    return proximo(s, c)
  },
  load(url, c, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API=true; export const API_BASE=''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, c)
  },
})
const { numeroDaReceita, quantidadeDaReceita, custoDoRascunho, rascunhoDaSugestao } = await import('../src/aplicacao/calculosReceita.js')
const { erroDaReceita } = await import('../src/aplicacao/receitas.js')
const { detalheDaReceitaDaApi } = await import('../src/infra/api/producaoApi.js')
const { moeda } = await import('../src/dominio/formato.js')
let passou = 0
const conferir = (nome, fn) => { fn(); passou++; process.stdout.write(`ok ${nome}\n`) }
const insumos = [{ id: 'farinha', nome: 'Farinha de trigo', unidade: 'G', custo: 0.02 }, { id: 'leite', nome: 'Leite', unidade: 'L', custo: 4 }]
const linha = (insumoId, quantidade, unidade) => ({ insumoId, quantidade, unidade })
const calcular = (linhas, rendimento = '2', unidadeRendimento = 'Un', cadastro = insumos) => custoDoRascunho({ linhas, rendimento, unidadeRendimento }, cadastro)
conferir('decimal pt-BR, dot e quantidade pequena preservados', () => {
  assert.equal(numeroDaReceita('1,25'), 1.25); assert.equal(numeroDaReceita('.5'), 0.5); assert.equal(numeroDaReceita('0.0001'), 0.0001)
  for (const v of ['', '-1', 'Infinity', '1e3', '1.000.000', '1000001']) assert.ok(Number.isNaN(numeroDaReceita(v)))
  assert.equal(quantidadeDaReceita(0.0001), '0,0001'); assert.equal(quantidadeDaReceita(0.00000001), '0,00000001')
})
conferir('massa e volume usam referências cadastradas com conversão', () => {
  assert.deepEqual(calcular([linha('farinha', '0,5', 'Kg'), linha('leite', '250', 'Ml')]), { custos: [10, 1], total: 11, porRendimento: 5.5 })
})
conferir('custo parcial, dimensão incompatível e quantidade inválida não viram total', () => {
  for (const l of [linha('leite', '1', 'Kg'), linha('ausente', '1', 'G'), linha('farinha', 'Infinity', 'G')]) {
    const r = calcular([linha('farinha', '1', 'G'), l]); assert.equal(r.total, null); assert.equal(r.porRendimento, null)
  }
})
conferir('custo zero conhecido é válido, custo desconhecido permanece null', () => {
  assert.equal(calcular([linha('zero', '3', 'Un')], '2', 'Un', [{ id: 'zero', unidade: 'Un', custo: 0 }]).total, 0)
  assert.equal(calcular([linha('zero', '3', 'Un')], '2', 'Un', [{ id: 'zero', unidade: 'Un', custo: null }]).total, null)
})
conferir('rendimento sem unidade não inventa custo por porção', () => {
  assert.equal(calcular([linha('farinha', '500', 'G')], '2', '').porRendimento, null)
  assert.match(erroDaReceita({ rendimento: '2', unidadeRendimento: '', linhas: [linha('farinha', '500', 'G')] }), /unidade do rendimento/)
})
conferir('IA preserva lacunas e exige revisão antes de salvar', () => {
  const r = rascunhoDaSugestao({ rendimento: null, unidadeRendimento: null, linhas: [{ nome: 'Farinha de trigo', quantidade: null, unidade: null }, { nome: 'Não cadastrado', quantidade: 1, unidade: 'Un' }] }, insumos)
  assert.equal(r.rendimento, ''); assert.equal(r.unidadeRendimento, ''); assert.equal(r.linhas[0].insumoId, 'farinha')
  assert.equal(r.linhas[0].quantidade, ''); assert.equal(r.linhas[0].unidade, ''); assert.equal(r.linhas[1].insumoId, '')
  assert.ok(erroDaReceita(r)); assert.equal(calcular(r.linhas, r.rendimento, r.unidadeRendimento).total, null)
})
conferir('nomes ambíguos não são vinculados automaticamente', () => {
  const r = rascunhoDaSugestao({ rendimento: 1, unidadeRendimento: 'Un', linhas: [{ nome: 'Farinha de trígo', quantidade: 100, unidade: 'G' }] }, [...insumos, { ...insumos[0], id: 'duplicado' }])
  assert.equal(r.linhas[0].insumoId, '')
})
conferir('referência original fora do catálogo mantém custo sem derivar do arredondado', () => {
  const d = detalheDaReceitaDaApi({ linhas: [{ insumoId: 'antigo', insumo: 'Antigo', quantidade: 0.3, unidade: 'Kg', custo: 1.23, unidadeDoInsumo: 'Kg', custoReferencia: 4.11 }] })
  const referencias = d.linhas.map((l) => ({ id: l.insumoId, unidade: l.unidadeDoInsumo, custo: l.custoReferencia }))
  const r = calcular(d.linhas, '0.25', 'Kg', referencias)
  assert.equal(r.total, 1.233); assert.equal(Number(r.porRendimento.toFixed(2)), 4.93)
  assert.equal(calcular([{ ...d.linhas[0], insumoId: 'outro' }], '1', 'Kg', referencias).total, null)
})
conferir('meio centavo coincide com a ficha, soma antes de arredondar e dividir', () => {
  const r = calcular([linha('meio', '0,1', 'Kg')], '1', 'Kg', [{ id: 'meio', unidade: 'Kg', custo: 1.15 }])
  assert.equal(r.total, 0.115); assert.match(moeda(r.total), /0,12/); assert.match(moeda(r.porRendimento), /0,12/)
  const soma = calcular([linha('meio', '0,1', 'Kg'), linha('meio', '0,1', 'Kg')], '2', 'Kg', [{ id: 'meio', unidade: 'Kg', custo: 1.15 }])
  assert.equal(soma.total, 0.23); assert.match(moeda(soma.total), /0,23/); assert.match(moeda(soma.porRendimento), /0,12/)
  assert.match(moeda(calcular([linha('meio', '0,0999', 'Kg')], '1', 'Kg', [{ id: 'meio', unidade: 'Kg', custo: 1.15 }]).total), /0,11/)
})
process.stdout.write(`${passou}/${passou} provas de produção, cálculo e IA passaram.\n`)
