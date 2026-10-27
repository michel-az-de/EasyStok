import { chamarApi } from './cliente'

// Caixa do dia (#1443): `api/caixa` do EasyStok. O resumo e o saldo esperado são calculados pela
// API (a mesma fonte do card do painel), nunca pelo console. A empresa sai do token.
const BASE = '/api/caixa'

// Sem data, a API devolve o dia de hoje da loja. Com data (#1624), o resumo daquele dia.
export const obterCaixaDoDia = (data = null) => chamarApi(`${BASE}/dia${data ? `?data=${data}` : ''}`).then(caixaDiaDaApi)

export const listarFechamentos = (quantos = 7) =>
  chamarApi(`${BASE}/fechamentos?page=1&pageSize=${quantos}`).then((itens) => (itens ?? []).map(fechamentoDaApi))

export const abrirCaixa = ({ saldoInicial, observacoes }) =>
  chamarApi(`${BASE}/abrir`, { metodo: 'POST', corpo: { saldoInicial, observacoes } })

// Saída pela web exige método e descrição (FIN-003); a tela já pede os dois.
export const registrarMovimento = ({ tipo, valor, categoria, metodo, descricao }, chave = null) =>
  chamarApi(`${BASE}/movimentos`, { metodo: 'POST', chave, corpo: { tipo, valor, categoria, metodo, descricao } })

export const estornarMovimento = (id, motivo) =>
  chamarApi(`${BASE}/movimentos/${id}/estornar`, { metodo: 'POST', corpo: { motivo } })

// Sem data: a API fecha a sessão aberta, que pode ser de um dia anterior (#640).
// #2113 (B3 da #2084): o contado vai em campo próprio; a abertura seguinte parte dele.
export const fecharCaixa = ({ observacoes, valorContado = null }) =>
  chamarApi(`${BASE}/fechar`, { metodo: 'POST', corpo: { observacoes, valorContado } })

export const fechamentoDaApi = (f) => (f ? {
  id: f.id,
  data: f.data,
  saldoInicial: f.saldoInicial,
  totalVendas: f.totalVendas,
  totalPagamentosPedidos: f.totalPagamentosPedidos,
  totalEntradasExtras: f.totalEntradasExtras,
  totalSaidasExtras: f.totalSaidasExtras,
  saldoFinal: f.saldoFinal,
  // #1776-11: dinheiro físico que ficou na gaveta; a API só informa no histórico (`/fechamentos`).
  dinheiroNaGaveta: f.dinheiroNaGaveta ?? null,
  fechadoPorNome: f.fechadoPorNome ?? null,
  observacoes: f.observacoes ?? null,
  fechadoEm: f.fechadoEm,
} : null)

const movimentoDaApi = (m) => ({
  id: m.id,
  tipo: m.tipo,
  valor: m.valor,
  categoria: m.categoria || (m.tipo === 'abertura' ? 'Abertura de caixa' : m.tipo === 'fechamento' ? 'Fechamento' : m.tipo),
  meio: m.metodo ?? null,
  descricao: m.descricao ?? '',
  em: m.dataMovimento,
  autorNome: m.registradoPorNome ?? null,
  estornadoEm: m.estornadoEm ?? null,
  estornadoPorNome: m.estornadoPorNome ?? null,
  motivoEstorno: m.motivoEstorno ?? null,
  devolucaoPedido: m.origem === 'devolucao_pedido',
})

// `aberturaPendenteCrossDay`: o caixa de um dia anterior ficou aberto e ainda não fechou (#596).
export function caixaDiaDaApi(d) {
  return {
    data: d.data,
    saldoInicial: d.saldoInicial ?? 0,
    totalVendas: d.totalVendas ?? 0,
    totalPagamentosPedidos: d.totalPagamentosPedidos ?? 0,
    totalEntradasExtras: d.totalEntradasExtras ?? 0,
    totalSaidasExtras: d.totalSaidasExtras ?? 0,
    saldoEsperado: d.saldoEsperado ?? 0,
    aberto: Boolean(d.aberto),
    fechado: Boolean(d.fechado),
    fechamento: fechamentoDaApi(d.fechamento),
    // #1630: no dia fechado, o que entrou depois de fechar (os totais acima são os do fechamento).
    posFechamento: d.posFechamento ?? 0,
    esquecidoAberto: Boolean(d.aberturaPendenteCrossDay),
    abertoDesde: d.abertoDesde ?? null,
    // #2113 (#2084): B1, o caixa abriu sozinho no primeiro pagamento e espera a gerente conferir; D1, no caixa
    // esquecido, o esperado do dia da abertura e o dinheiro de hoje que fica no caixa de hoje; B4, no dia
    // fechado, o dinheiro que entrou depois de fechar e vai para a abertura seguinte.
    aberturaAConfirmar: Boolean(d.aberturaAConfirmar),
    dinheiroEsperadoNoFechamento: d.dinheiroEsperadoNoFechamento ?? null,
    dinheiroEntrouDepois: d.dinheiroEntrouDepois ?? null,
    dinheiroPosFechamento: d.dinheiroPosFechamento ?? 0,
    movimentos: (d.movimentos ?? []).map(movimentoDaApi),
    linhasExtras: (d.linhasExtras ?? []).map((l) => ({
      em: l.hora, tipo: l.tipo, valor: l.valor, descricao: l.descricao ?? '', meio: l.metodo ?? null, origem: l.origem,
      // #2098: só no pagamento de pedido.
      pedidoNumeroDoDia: l.pedidoNumeroDoDia ?? null, clienteNome: l.clienteNome ?? null,
    })),
  }
}
