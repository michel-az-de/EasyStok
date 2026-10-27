export const STATUS_CONTAS = ['Rascunho', 'Aberta', 'ParcialmentePaga', 'Paga', 'Vencida', 'Cancelada']
// #2095 (C10): conta a receber fala de receber.
export function nomeStatusConta(status, lado = 'pagar') {
  if (status === 'ParcialmentePaga') return lado === 'receber' ? 'Parcialmente recebida' : 'Parcialmente paga'
  if (status === 'Paga') return lado === 'receber' ? 'Recebida' : 'Paga'
  return status
}
// #2095 (C2): a Pilula tem ok, aviso, perigo e neutro; "erro" não existe e apagava o vencido.
export const tomDoStatusConta = (status) => status === 'Vencida' ? 'perigo' : status === 'Paga' ? 'ok' : status === 'ParcialmentePaga' ? 'aviso' : 'neutro'
export const nomeStatusPagamento = (status) => ({ Confirmado: 'Registrado', Estornado: 'Estornado', Pendente: 'Pendente' })[status] ?? status
export const palavrasDoLado = (lado) => lado === 'receber'
  ? { verbo: 'Receber', registro: 'recebimento', registros: 'recebimentos', feito: 'Recebido', forma: 'Forma de recebimento', data: 'Data do recebimento', jaFeito: 'Já foi recebida' }
  : { verbo: 'Pagar', registro: 'pagamento', registros: 'pagamentos', feito: 'Pago', forma: 'Forma de pagamento', data: 'Data do pagamento', jaFeito: 'Já foi paga' }

export function diaFinanceiro(instante = Date.now()) {
  const partes = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date(instante))
  const valor = (tipo) => partes.find((parte) => parte.type === tipo).value
  return `${valor('year')}-${valor('month')}-${valor('day')}`
}

export const instanteFinanceiro = (dia, fim = false) => new Date(`${dia}T${fim ? '23:59:59.999' : '00:00:00'}-03:00`).toISOString()
export const periodoFinanceiro = () => { const hoje = diaFinanceiro(); return { inicio: hoje.slice(0, 7) + '-01', fim: hoje } }
// #2095 (C4): o relatório olha o mês inteiro, para o "a pagar" dos próximos dias aparecer.
export function mesFinanceiro(hoje = diaFinanceiro()) {
  const [ano, mes] = hoje.split('-').map(Number)
  return { inicio: hoje.slice(0, 7) + '-01', fim: new Date(Date.UTC(ano, mes, 0)).toISOString().slice(0, 10) }
}
export function diaMais(dia, dias) {
  const [ano, mes, d] = dia.split('-').map(Number)
  return new Date(Date.UTC(ano, mes - 1, d + dias)).toISOString().slice(0, 10)
}

// ------------------------------------------------------------ lista orientada a vencimento (#2095)
const centavosDe = (valor) => Math.round((Number(valor) || 0) * 100)
export const parcelaEmAberto = (parcela) => parcela.saldo > 0 && !['Paga', 'Cancelada'].includes(parcela.status)
export const contaEmAberto = (conta) => !['Rascunho', 'Cancelada', 'Paga'].includes(conta.status) && (conta.parcelas ?? []).some(parcelaEmAberto)
// A API só marca Vencida no job; a pílula já diz "Vencida" quando a próxima parcela passou do dia.
export function statusDaConta(conta, hoje = diaFinanceiro()) {
  if (!['Aberta', 'ParcialmentePaga'].includes(conta.status)) return conta.status
  const parcela = proximaParcela(conta)
  return parcela && diaFinanceiro(parcela.dataVencimento) < hoje ? 'Vencida' : conta.status
}
export const proximaParcela = (conta) => (conta.parcelas ?? []).filter(parcelaEmAberto).sort((a, b) => Date.parse(a.dataVencimento) - Date.parse(b.dataVencimento))[0] ?? null
// Conta cancelada zera o total na API; o que foi lançado é a soma das parcelas.
export const valorLancado = (conta) => (conta.parcelas ?? []).reduce((soma, parcela) => soma + centavosDe(parcela.valor), 0) / 100

export function valorDaLinha(conta, lado = 'pagar') {
  if (conta.status === 'Cancelada') return { rotulo: 'Cancelada', valor: valorLancado(conta) }
  if (conta.status === 'Rascunho') return { rotulo: 'Rascunho', valor: valorLancado(conta) }
  if (conta.status === 'Paga' || !(conta.pendente > 0)) return { rotulo: palavrasDoLado(lado).feito, valor: valorLancado(conta) }
  return { rotulo: 'Falta', valor: conta.pendente }
}

export const CHIPS_CONTAS = [
  { id: 'vencidas', rotulo: 'Vencidas' },
  { id: 'hoje', rotulo: 'Vencem hoje' },
  { id: 'semana', rotulo: 'Próximos 7 dias' },
  { id: 'abertas', rotulo: 'Em aberto' },
  { id: 'todas', rotulo: 'Todas' },
]
export const CHIP_PADRAO = 'abertas'

// O que pedir à API e o que conferir aqui. A API filtra por um status só e por qualquer
// parcela no intervalo (inclusive a paga): o recorte fino é feito no cliente, sobre a lista
// inteira daquele filtro, e só "Todas" pagina no servidor.
export function planoDoChip(chip, hoje = diaFinanceiro()) {
  const venceEntre = (de, ate) => (conta) => contaEmAberto(conta) && conta.parcelas.some((parcela) => {
    if (!parcelaEmAberto(parcela)) return false
    const dia = diaFinanceiro(parcela.dataVencimento)
    return dia >= de && dia <= ate
  })
  if (chip === 'vencidas') return { api: { status: 'Vencida' }, local: contaEmAberto }
  if (chip === 'hoje') return { api: { vencimentoDe: instanteFinanceiro(hoje), vencimentoAte: instanteFinanceiro(hoje, true) }, local: venceEntre(hoje, hoje) }
  if (chip === 'semana') {
    const de = diaMais(hoje, 1), ate = diaMais(hoje, 7)
    return { api: { vencimentoDe: instanteFinanceiro(de), vencimentoAte: instanteFinanceiro(ate, true) }, local: venceEntre(de, ate) }
  }
  if (chip === 'abertas') return { api: {}, local: contaEmAberto }
  return { api: {}, local: null }
}

// Filtros escritos pela dona (situação e datas) vão como instante: data pura a API recusa (F4).
export function filtrosDaApi({ busca = '', status = '', vencimentoDe = '', vencimentoAte = '' } = {}) {
  return { busca: busca.trim(), status, vencimentoDe: vencimentoDe ? instanteFinanceiro(vencimentoDe) : '', vencimentoAte: vencimentoAte ? instanteFinanceiro(vencimentoAte, true) : '' }
}

// Próxima parcela em aberto primeiro; sem parcela em aberto vai para o fim, a emissão mais recente antes.
export function ordenarPorVencimento(contas) {
  const chave = (conta) => { const parcela = proximaParcela(conta); return parcela ? Date.parse(parcela.dataVencimento) : Infinity }
  return [...contas].sort((a, b) => {
    const ka = chave(a), kb = chave(b)
    if (ka !== kb) return ka < kb ? -1 : 1
    return (Date.parse(b.dataEmissao) || 0) - (Date.parse(a.dataEmissao) || 0)
  })
}

export const TAMANHO_PAGINA = 20
export const paginaDaLista = (itens, pagina, tamanho = TAMANHO_PAGINA) => itens.slice((pagina - 1) * tamanho, pagina * tamanho)
// #2095 (C12): depois de pagar com filtro ligado a página pode esvaziar; volta para a primeira.
export const paginaValida = (pagina, total, tamanho = TAMANHO_PAGINA) => pagina > 1 && (pagina - 1) * tamanho >= total ? 1 : pagina

// A API só edita rascunho (C5) e, desde a #476, registrar pagamento exige gerenciar.
export const podeEditarConta = (conta, podeGerenciar) => podeGerenciar === true && conta?.status === 'Rascunho'
export const podeRegistrarNaConta = (conta, podeGerenciar) => podeGerenciar === true && !['Rascunho', 'Cancelada'].includes(conta?.status) && Boolean(proximaParcela(conta ?? {}))

// Visão geral leva à lista já filtrada: `#/m/financeiro/<lado>/<chip>`.
export const hashDoChip = (lado, chip) => `#/m/financeiro/${lado}/${chip}`
export function chipDaHash(hash) {
  const chip = String(hash ?? '').split('/')[4]
  return CHIPS_CONTAS.some((item) => item.id === chip) ? chip : null
}

// Motivo de Salvar desabilitado na conta nova (#2095, C8). Nulo: pode salvar.
export function motivoContaInvalida({ descricao, categoria, centavos, quantidade, emissao, vencimento }) {
  if (!String(descricao ?? '').trim()) return 'Escreva a descrição da conta.'
  if (!categoria) return 'Escolha a categoria financeira.'
  if (!(centavos > 0)) return 'Informe o valor total.'
  if (!Number.isInteger(quantidade) || quantidade < 1 || quantidade > 60) return 'Use de 1 a 60 parcelas.'
  if (centavos < quantidade) return 'Cada parcela precisa de pelo menos R$ 0,01.'
  if (!emissao) return 'Informe a data de emissão.'
  if (!vencimento) return 'Informe o primeiro vencimento.'
  if (vencimento < emissao) return 'O primeiro vencimento não pode ser antes da emissão.'
  return null
}

// Mensagem técnica do servidor (Npgsql, exceção, stack) nunca vai crua para a tela (F4).
const TECNICA = /Cannot write|Kind=|Npgsql|Exception|System\.|PostgreSQL|SQLSTATE|stack trace|\bat [A-Z][\w.]+\(|Object reference/i
const TRADUCOES = [
  [/^So e possivel atualizar conta em Rascunho\.?$/i, 'Só dá para editar uma conta em rascunho.'],
  [/^Conta a (pagar|receber) nao encontrada\.?$/i, 'Esta conta não foi encontrada. Atualize a lista.'],
]
export function mensagemFinanceira(texto) {
  if (!texto) return texto
  const mensagem = String(texto)
  if (TECNICA.test(mensagem)) return 'O servidor não conseguiu atender a este pedido. Revise os filtros e tente de novo; se continuar, avise o suporte.'
  return TRADUCOES.find(([padrao]) => padrao.test(mensagem.trim()))?.[1] ?? mensagem
}

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
