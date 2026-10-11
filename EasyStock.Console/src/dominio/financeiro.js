export const STATUS_CONTAS = ['Rascunho', 'Aberta', 'ParcialmentePaga', 'Paga', 'Vencida', 'Cancelada']
export const nomeStatusConta = (status) => status === 'ParcialmentePaga' ? 'Parcialmente paga' : status

export function diaFinanceiro(instante = Date.now()) {
  const partes = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date(instante))
  const valor = (tipo) => partes.find((parte) => parte.type === tipo).value
  return `${valor('year')}-${valor('month')}-${valor('day')}`
}

export const instanteFinanceiro = (dia, fim = false) => new Date(`${dia}T${fim ? '23:59:59.999' : '00:00:00'}-03:00`).toISOString()
export const periodoFinanceiro = () => { const hoje = diaFinanceiro(); return { inicio: hoje.slice(0, 7) + '-01', fim: hoje } }

// Divide em centavos; o dia 31 cai no último dia dos meses menores.
export function parcelasFinanceiras(centavos, quantidade, primeiroDia) {
  if (!Number.isSafeInteger(centavos) || !Number.isInteger(quantidade) || quantidade < 1 || quantidade > 60 || centavos < quantidade || !/^\d{4}-\d{2}-\d{2}$/.test(primeiroDia)) return []
  const [ano, mes, dia] = primeiroDia.split('-').map(Number)
  const base = Math.floor(centavos / quantidade)
  return Array.from({ length: quantidade }, (_, indice) => {
    const ultimo = new Date(Date.UTC(ano, mes + indice, 0))
    const vencimento = new Date(Date.UTC(ultimo.getUTCFullYear(), ultimo.getUTCMonth(), Math.min(dia, ultimo.getUTCDate()))).toISOString().slice(0, 10)
    return { numero: indice + 1, valor: (base + (indice === quantidade - 1 ? centavos % quantidade : 0)) / 100, dataVencimento: instanteFinanceiro(vencimento) }
  })
}

export function extratoFinanceiro(contas, movimentos, inicio, fim) {
  const de = Date.parse(instanteFinanceiro(inicio)), ate = Date.parse(instanteFinanceiro(fim, true))
  const vinculados = new Set()
  const linhas = contas.flatMap(({ lado, conta }) => conta.parcelas.flatMap((parcela) => parcela.pagamentos.map((pagamento) => {
    if (pagamento.movimentoCaixaId) vinculados.add(pagamento.movimentoCaixaId)
    return { id: pagamento.id, em: pagamento.dataPagamento, descricao: conta.descricao, lado, valor: pagamento.valor, metodo: pagamento.metodo, estornado: pagamento.status === 'Estornado', contaId: conta.id }
  })))
  for (const movimento of movimentos) {
    if (vinculados.has(movimento.id) || ['abertura', 'fechamento'].includes(movimento.tipo.toLowerCase())) continue
    linhas.push({ id: movimento.id, em: movimento.dataMovimento, descricao: movimento.descricao || movimento.categoria || movimento.tipo, lado: movimento.tipo.toLowerCase() === 'saida' ? 'pagar' : 'receber', valor: movimento.valor, metodo: movimento.metodo, estornado: Boolean(movimento.estornadoEm), contaId: null })
  }
  return linhas.filter((linha) => Date.parse(linha.em) >= de && Date.parse(linha.em) <= ate).sort((a, b) => Date.parse(b.em) - Date.parse(a.em))
}
