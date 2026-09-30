import { chamarApi } from './cliente'
import { instante } from './traducaoConversas'

// Comanda da conversa na API real (F03): cardápio e janelas da vitrine da empresa logada,
// o pedido da conversa (S10) com a cobrança do Mercado Pago (S11) e o estado dos dois.
// A regra (preço, frete, vaga, prazo, cobrança) é do EasyStok; aqui só se traduz o formato.
const BASE = '/api/atendimento'
const pedidoDaConversa = (conversaId) => `${BASE}/conversas/${conversaId}/pedido`

const LINHA_DO_CONTRATO = { paraServir: 'servir', prepararEmCasa: 'casa' }

// Item do cardápio público → produto do console. O `sku` é o `cardapioItemId`, que é o que
// o pedido recebe. Sem controle de saldo, `estoque` fica nulo (o domínio lê como sem limite).
export const produtoDaApi = (item) => ({
  sku: item.id,
  nome: item.nome,
  linha: LINHA_DO_CONTRATO[item.linha] ?? 'servir',
  porcao: item.pesoExibicao ?? '',
  preco: item.precoCentavos / 100,
  estoque: item.estoqueAtual ?? null,
  disponivelHoje: item.disponivel !== false,
  categoria: item.categoria ?? null,
})

const DIA_DA_SEMANA = ['dom', 'seg', 'ter', 'qua', 'qui', 'sex', 'sáb']

function rotuloDoDia(data) {
  const [ano, mes, dia] = data.split('-').map(Number)
  const semana = DIA_DA_SEMANA[new Date(ano, mes - 1, dia).getDay()]
  return `${semana} ${String(dia).padStart(2, '0')}/${String(mes).padStart(2, '0')}`
}

// A janela da API tem data; o id do console junta os dois para o pedido saber o que reservar.
export const idDaJanela = (janelaId, data) => `${janelaId}|${data}`
export function janelaDoId(id) {
  const [janelaId, data] = String(id ?? '').split('|')
  return janelaId && data ? { janelaId, data } : null
}

export const janelaDaApi = (j) => ({
  id: idDaJanela(j.janelaId, j.data),
  rotulo: `${rotuloDoDia(j.data)} · ${j.label}`,
  vagas: j.vagasRestantes,
  capacidade: j.capacidade,
})

const ESTADO_DO_STATUS = {
  rascunho: 'aguardando',
  aguardando_pagamento: 'aguardando',
  aguardando_aprovacao_baba: 'pago',
  aprovado_baba: 'pago',
  aguardando: 'pago',
  preparando: 'preparo',
  pronto: 'embalado',
  saiu_para_entrega: 'entrega',
  entregue: 'entregue',
  cancelado: 'cancelado',
}

export const FORMA_NA_ENTREGA = 'na_entrega'
export const FORMA_ONLINE = 'online'
const MEIOS_NA_ENTREGA = new Set(['maquininha', 'vale-refeicao'])
export const formaDoMeio = (meio) => (MEIOS_NA_ENTREGA.has(meio) ? FORMA_NA_ENTREGA : FORMA_ONLINE)

const ms = (valor) => (valor ? new Date(instante(valor)).getTime() : null)

// Cobrança da API → formato de `dominio/cobranca.js`, para `situacaoDaCobranca` e as telas
// lerem igual ao modo demonstração. Expirada sai pelo próprio `expiraEm` vencido.
function cobrancaDaApi(c, meioAnterior) {
  if (!c) return null
  const naEntrega = c.provedor === FORMA_NA_ENTREGA
  const meio = naEntrega === MEIOS_NA_ENTREGA.has(meioAnterior)
    ? meioAnterior
    : (naEntrega ? 'maquininha' : 'cartao-link')
  const criadaEm = ms(c.criadaEm)
  return {
    id: c.cobrancaId,
    meio,
    valor: c.valor,
    copiaECola: null,
    link: c.linkPagamento ?? null,
    criadaEm,
    expiraEm: ms(c.expiraEm),
    comprovanteEm: null,
    pagaEm: ms(c.pagaEm),
    valorPago: c.valorPago ?? null,
    liberadaEm: null,
    tentativa: c.tentativa,
    diferenca: false,
    estornadaEm: c.status === 'Estornada' ? criadaEm : null,
    canceladaEm: c.status === 'Cancelada' ? criadaEm : null,
    statusApi: c.status,
  }
}

// Pedido da API → pedido do console. A janela escolhida e o meio ficam do lado de cá
// (`anterior`): a API devolve o pedido, não a escolha da tela.
export function pedidoDaApi(p, anterior = null) {
  if (!p) return null
  const cobranca = cobrancaDaApi(p.cobranca, anterior?.meio)
  return {
    pedidoId: p.pedidoId,
    numero: `EZ-${p.pedidoId.slice(0, 6).toUpperCase()}`,
    estado: ESTADO_DO_STATUS[p.status] ?? 'aguardando',
    statusApi: p.status,
    janela: anterior?.janela ?? null,
    entregador: anterior?.entregador ?? null,
    itens: p.itens.map((i) => ({
      sku: i.cardapioItemId, qtd: i.quantidade, obs: i.observacao ?? '', acrescimo: false, entrouEm: null,
    })),
    agradecimentoEnviado: anterior?.agradecimentoEnviado ?? false,
    pagamentos: [],
    meio: cobranca?.meio ?? anterior?.meio ?? null,
    cobranca,
    frete: p.frete,
    totalApi: p.total,
  }
}

export const listarCardapio = async () =>
  ((await chamarApi(`${BASE}/comanda/cardapio`))?.itens ?? []).map(produtoDaApi)

export async function listarJanelas({ itens = [], dataInicio = null, dataFim = null } = {}) {
  const busca = new URLSearchParams()
  if (dataInicio) busca.set('dataInicio', dataInicio)
  if (dataFim) busca.set('dataFim', dataFim)
  for (const sku of itens) busca.append('itens', sku)
  const resposta = await chamarApi(`${BASE}/comanda/janelas?${busca}`)
  return {
    lojaDisponivel: Boolean(resposta?.lojaDisponivel),
    janelas: (resposta?.janelas ?? []).map(janelaDaApi),
  }
}

export const obterPedido = (conversaId) => chamarApi(pedidoDaConversa(conversaId))

// Comanda local → corpo do POST. Itens com o mesmo sku e anotações diferentes seguem como
// linhas próprias (RN-20); acréscimo só existe depois do pagamento, fora deste caminho.
export function corpoDoPedido(pedido) {
  const janela = janelaDoId(pedido?.janela)
  return {
    itens: (pedido?.itens ?? []).map((l) => ({ cardapioItemId: l.sku, qtd: l.qtd, observacao: l.obs || null })),
    janelaId: janela?.janelaId ?? null,
    dataEntrega: janela?.data ?? null,
    forma: formaDoMeio(pedido?.meio),
  }
}

export const gerarPedido = (conversaId, corpo) =>
  chamarApi(pedidoDaConversa(conversaId), { metodo: 'POST', corpo })

// Troca de forma da S11: com conversa aberta, o link novo sai ao cliente pelo EasyStok.
export const trocarFormaPagamento = (pedidoId, forma) =>
  chamarApi(`/api/pedidos/${pedidoId}/cobranca/forma`, { metodo: 'POST', corpo: { forma } })
