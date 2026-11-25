// #3299: a leitura do desempenho dos produtos para a tela. As contas (curva, tendência, parado, margem) são da API;
// aqui só o texto e o tom. Dado ausente (API antiga) vira nulo, nunca erro.

export const porProduto = (desempenho) => new Map((Array.isArray(desempenho?.produtos) ? desempenho.produtos : []).map(p => [p.produtoId, p]))

const NUMERO = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 })
export const numero = (n) => NUMERO.format(n ?? 0)

// Receita da janela contra a anterior: sobe, desce ou igual. Sem base anterior, diz isso em vez de inventar %.
export function variacaoDaReceita(atual, anterior) {
  if (!Number.isFinite(atual) || !Number.isFinite(anterior)) return null
  if (anterior <= 0) return atual > 0 ? { texto: 'sem venda nos 90 dias antes', tom: 'neutro' } : null
  const p = Math.round(((atual - anterior) / anterior) * 100)
  if (p === 0) return { texto: 'igual aos 90 dias antes', tom: 'neutro' }
  return { texto: `${p > 0 ? '+' : '−'}${Math.abs(p)}% sobre os 90 dias antes`, tom: p > 0 ? 'sobe' : 'desce' }
}

export const TENDENCIAS = Object.freeze({
  subindo: { rotulo: 'Subindo', icone: 'arrow-up', tom: 'ok', titulo: 'Vendeu 25% a mais (e 2 unidades ou mais) que nos 90 dias antes.' },
  caindo: { rotulo: 'Caindo', icone: 'arrow-down', tom: 'perigo', titulo: 'Vendeu 25% a menos (e 2 unidades ou mais) que nos 90 dias antes.' },
  estavel: { rotulo: 'Estável', icone: null, tom: 'neutro', titulo: 'Vendas parecidas com as dos 90 dias antes.' },
})

export const CURVAS = Object.freeze({
  A: 'Curva A: os produtos que somam 80% da receita do ano.',
  B: 'Curva B: os próximos 15% da receita do ano.',
  C: 'Curva C: os últimos 5% da receita do ano.',
})

export const textoDosDias = (d) => (!Number.isFinite(d) ? null : d === 0 ? 'hoje' : d === 1 ? 'ontem' : `há ${d} dias`)

// O sinal da venda que pede atenção (o do preço mora na coluna do preço). Parado pesa mais que nunca vendeu.
export function sinalDoProduto(d) {
  if (!d) return null
  if (d.parado) return { rotulo: 'Parado', tom: 'perigo', titulo: `Vendia com frequência e está sem vender ${textoDosDias(d.diasSemVender)}, mais que o dobro do intervalo de costume.` }
  if (d.nuncaVendeu) return { rotulo: 'Nunca vendeu', tom: 'aviso', titulo: 'Ativo há 30 dias ou mais e sem nenhuma venda.' }
  return null
}
