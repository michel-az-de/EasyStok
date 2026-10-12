export const numeroDaReceita = (valor) => {
  const texto = String(valor ?? '').trim()
  const numero = /^(?:\d+(?:[.,]\d*)?|[.,]\d+)$/.test(texto) ? Number(texto.replace(',', '.')) : NaN
  return Number.isFinite(numero) && numero <= 1000000 ? numero : NaN
}
export const quantidadeDaReceita = (valor) => Number(valor).toLocaleString('pt-BR', { maximumSignificantDigits: 12 })

const medida = { G: ['massa', 1], Kg: ['massa', 1000], Ml: ['volume', 1], L: ['volume', 1000], Un: ['unidade', 1] }
// Remove o ruído binário antes da apresentação monetária, sem arredondar linhas a centavos.
const decimalDoCalculo = (valor) => Number(valor.toPrecision(15))
export function custoDoRascunho({ linhas, rendimento, unidadeRendimento }, insumos) {
  const custos = linhas.map((linha) => {
    const insumo = insumos.find((i) => i.id === linha.insumoId)
    const de = medida[linha.unidade], para = medida[insumo?.unidade]
    const quantidade = numeroDaReceita(linha.quantidade)
    if (!insumo || insumo.custo == null || !Number.isFinite(insumo.custo) || insumo.custo < 0 || !(quantidade > 0) || !de || !para || de[0] !== para[0]) return null
    return decimalDoCalculo(quantidade * de[1] / para[1] * insumo.custo)
  })
  const total = custos.length && custos.every((custo) => custo != null) ? decimalDoCalculo(custos.reduce((soma, custo) => soma + custo, 0)) : null
  return { custos, total, porRendimento: total != null && medida[unidadeRendimento] && numeroDaReceita(rendimento) > 0 ? decimalDoCalculo(total / numeroDaReceita(rendimento)) : null }
}

const nomeNormalizado = (nome) => nome.normalize('NFD').replace(/[\u0300-\u036f]/g, '').trim().toLocaleLowerCase('pt-BR')
export function rascunhoDaSugestao(sugestao, insumos) {
  return {
    rendimento: sugestao.rendimento == null ? '' : String(sugestao.rendimento),
    unidadeRendimento: sugestao.unidadeRendimento ?? '',
    linhas: sugestao.linhas.map((linha) => {
      const encontrados = insumos.filter((i) => nomeNormalizado(i.nome) === nomeNormalizado(linha.nome))
      return { insumoId: encontrados.length === 1 ? encontrados[0].id : '', insumo: linha.nome, quantidade: linha.quantidade == null ? '' : String(linha.quantidade), unidade: linha.unidade ?? '', custo: null }
    }),
  }
}

export { pesquisarInsumosWeb, pesquisarReceitaIa, calcularReceita } from '../infra/api/producaoPesquisaApi'
