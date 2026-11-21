// Fechamentos de caixa do aparelho antigo (#2813, B4), somente leitura. Função pura: o dado chega por parâmetro.
//
// A API devolve `{ itens, ignorados }`. Cada item traz `id`, `dateKey` ("AAAA-MM-DD", dia de calendário sem fuso),
// `closedAt` (instante em UTC), `closedBy`, `notes` (nulo sem observação), `origem` e `totals`:
//   sold (vendido), in (entradas), out (saídas), balance (saldo), conferredBalance (gaveta conferida; nulo se ninguém
//   conferiu), entriesCount (lançamentos) e ordersCount (pedidos).
// Os valores estão em REAIS, como no aparelho (não é centavo): vão para `moeda`, nunca para `moedaDosCentavos`.
// O `totals` não traz forma de pagamento: a tela mostra só o que a API manda.
import { dataHora, dataPorExtenso, moeda, plural } from './formato'

export const ROTULO_GAVETA_NAO_CONFERIDA = 'Não conferida'

const DIA_DE_CALENDARIO = /^\d{4}-\d{2}-\d{2}$/
const numero = (valor) => (typeof valor === 'number' && Number.isFinite(valor) ? valor : null)
const texto = (valor) => (typeof valor === 'string' ? valor.trim() : '')
// Ordem de texto simples (a mesma da API, `StringComparer.Ordinal`): -1, 0 ou 1.
const ordemDoTexto = (a, b) => Number(a > b) - Number(a < b)
const contagem = (valor) => (valor === null ? null : String(valor))

// Um item da API no que a tela usa. Sem id ou sem dia de calendário a linha não tem como ser identificada nem
// mostrada: vira nulo, e quem chama conta o item como não lido.
export function fechamentoLegadoDaApi(item) {
  const id = texto(item?.id)
  const dia = texto(item?.dateKey)
  if (!id || !DIA_DE_CALENDARIO.test(dia)) return null
  const fechadoEm = Date.parse(item.closedAt)
  const totais = item.totals ?? {}
  return {
    id,
    dia,
    fechadoEm: Number.isNaN(fechadoEm) ? null : fechadoEm,
    fechadoPor: texto(item.closedBy),
    observacoes: texto(item.notes),
    totais: {
      vendido: numero(totais.sold),
      entradas: numero(totais.in),
      saidas: numero(totais.out),
      saldo: numero(totais.balance),
      conferido: numero(totais.conferredBalance),
      lancamentos: numero(totais.entriesCount),
      pedidos: numero(totais.ordersCount),
    },
  }
}

// Do dia mais novo para o mais antigo; no mesmo dia, o fechamento mais tarde primeiro; o id desempata (ordem estável).
const maisRecentePrimeiro = (a, b) =>
  ordemDoTexto(b.dia, a.dia) || (b.fechadoEm ?? 0) - (a.fechadoEm ?? 0) || ordemDoTexto(a.id, b.id)

// A resposta da API no que a tela usa. O que a API pulou e o que a tela não conseguiu ler entram na mesma conta:
// nenhum fechamento some sem aviso.
export function fechamentosLegadosDaApi(resposta) {
  const brutos = Array.isArray(resposta?.itens) ? resposta.itens : []
  const itens = brutos.map(fechamentoLegadoDaApi).filter(Boolean).sort(maisRecentePrimeiro)
  const daApi = Number.isInteger(resposta?.ignorados) && resposta.ignorados > 0 ? resposta.ignorados : 0
  return { itens, ignorados: daApi + (brutos.length - itens.length) }
}

// "2 de março de 2026": o dia de calendário vem pronto da API, sem fuso, então não escorrega para o dia anterior.
export const diaDoFechamento = (fechamento) => dataPorExtenso(fechamento.dia)

// "Fechado em 02/03/2026 18:30 por Ana", no fuso da loja. Sem hora ou sem nome, fica só o que houver.
export function quemFechou({ fechadoEm, fechadoPor }) {
  const quando = fechadoEm === null ? '' : dataHora(fechadoEm)
  if (quando && fechadoPor) return `Fechado em ${quando} por ${fechadoPor}`
  if (quando) return `Fechado em ${quando}`
  return fechadoPor ? `Fechado por ${fechadoPor}` : ''
}

// Os totais na ordem em que a tela mostra. Valor ausente na API aparece como "Sem valor". Gaveta sem conferência
// aparece como "Não conferida", nunca como zero (zero é gaveta conferida e vazia).
export function totaisDoFechamento({ totais }) {
  return [
    { rotulo: 'Vendido', valor: moeda(totais.vendido) },
    { rotulo: 'Entradas', valor: moeda(totais.entradas) },
    { rotulo: 'Saídas', valor: moeda(totais.saidas) },
    { rotulo: 'Saldo', valor: moeda(totais.saldo) },
    { rotulo: 'Gaveta conferida', valor: totais.conferido === null ? ROTULO_GAVETA_NAO_CONFERIDA : moeda(totais.conferido) },
    { rotulo: 'Pedidos', valor: contagem(totais.pedidos) },
    { rotulo: 'Lançamentos', valor: contagem(totais.lancamentos) },
  ].filter((linha) => linha.valor !== null)
}

// Aviso quando a API pulou fechamentos que não deu para ler (campo ausente ou de tipo errado). Vazio quando não houve.
export const avisoDeIgnorados = (quantidade) => (quantidade > 0
  ? `${plural(quantidade, 'fechamento do aparelho não pôde ser lido e ficou', 'fechamentos do aparelho não puderam ser lidos e ficaram')} fora desta lista.`
  : '')
