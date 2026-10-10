import { API_BASE } from '../fonteDados'
import { urlDeExibicaoDaFoto } from '../../dominio/vitrineCardapio'
import { chamarApi } from './cliente'
import { instante } from './traducaoConversas'
import { lerSessao } from './sessao'

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
  // #1448: a foto passa pela rota de mídia da API, de qualquer host gravado na URL.
  foto: urlDeExibicaoDaFoto(item.imagemUrl, API_BASE),
  fotos: (item.fotos?.length ? item.fotos : item.imagemUrl ? [item.imagemUrl] : [])
    .map((url) => urlDeExibicaoDaFoto(url, API_BASE)).filter(Boolean),
  // M1.2 (#1482): item novo em validação (RN-15) e novidade com prazo (fim do dia, hora da loja).
  emValidacao: item.emValidacao === true,
  novidadeAte: item.novidadeAte ? `${item.novidadeAte}T23:59:59-03:00` : null,
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
  rotulo: `${rotuloDoDia(j.data)} · ${j.horaInicio && j.horaFim ? `${j.horaInicio.slice(0, 5)} às ${j.horaFim.slice(0, 5)} · ` : ''}${j.label}`,
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

// #1474 (R1): etapa da esteira do console → status da API (`StatusPedidoMapper`), o mesmo que a
// Cozinha manda. "Pago" não se marca por aqui: entra pela cobrança ou pela baixa à mão.
export const STATUS_DO_PASSO = {
  preparo: 'preparando',
  embalado: 'pronto',
  entrega: 'saiu_para_entrega',
  entregue: 'entregue',
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
  // Sem meio anterior (polling, recarga), o meio é o padrão da forma: link do Mercado Pago
  // online, maquininha na entrega. Antes caía em `undefined` e a tela mostrava "Pix" (F07).
  const meio = meioAnterior && naEntrega === MEIOS_NA_ENTREGA.has(meioAnterior)
    ? meioAnterior
    : (naEntrega ? 'maquininha' : 'cartao-link')
  const criadaEm = ms(c.criadaEm)
  return {
    id: c.cobrancaId,
    meio,
    metodoRecebido: c.metodoPagamento ?? null,
    manual: naEntrega,
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

// Pedido persistido: a janela vem da vaga no servidor, inclusive após recarregar a página.
export function pedidoDaApi(p, anterior = null) {
  if (!p) return null
  const cobranca = cobrancaDaApi(p.cobranca, anterior?.meio)
  return {
    pedidoId: p.pedidoId,
    // Mesmo código curto do backend (VariaveisAtendimento.CodigoCurto): o que o cliente lê.
    numero: p.pedidoId.replace(/-/g, '').slice(0, 8).toUpperCase(),
    estado: ESTADO_DO_STATUS[p.status] ?? 'aguardando',
    statusApi: p.status,
    // #1474: pedido fora da área liberado espera aprovação; a baixa manual é barrada antes.
    requerAprovacao: Boolean(p.requerAprovacao),
    janela: p.janela ? idDaJanela(p.janela.janelaId, p.janela.data) : null,
    janelaRotulo: p.janela ? janelaDaApi(p.janela).rotulo : null,
    entregador: anterior?.entregador ?? null,
    itens: p.itens.map((i) => ({
      sku: i.cardapioItemId, qtd: i.quantidade, obs: i.observacao ?? '', acrescimo: false, entrouEm: null,
    })),
    agradecimentoEnviado: anterior?.agradecimentoEnviado ?? false,
    pagamentos: [],
    totalPagoApi: p.totalPago ?? 0,
    pagamentosApi: p.pagamentos?.map((pag) => ({
      valor: pag.valor, coberto: pag.valor, em: ms(pag.pagoEm), meio: pag.metodo, pagamentoId: pag.id,
    })),
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

export async function listarJanelasPedido(pedidoId, data = '') {
  const busca = data ? `?dataInicio=${encodeURIComponent(data)}&dataFim=${encodeURIComponent(data)}` : ''
  const resposta = await chamarApi(`/api/pedidos/${pedidoId}/janelas${busca}`)
  return {
    lojaDisponivel: resposta.lojaDisponivel,
    janelas: resposta.janelas.map(janelaDaApi),
    atual: resposta.atual ? janelaDaApi(resposta.atual) : null,
  }
}

export const trocarJanelaPedido = (pedidoId, janela, avisarCliente) =>
  chamarApi(`/api/pedidos/${pedidoId}/janela`, {
    metodo: 'PATCH', corpo: { ...janelaDoId(janela), avisarCliente }, sinal: AbortSignal.timeout(15000),
  })

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

// Emissão do link da S11 para pedido que nasceu sem cobrança (Mercado Pago fora na hora).
export const reemitirCobranca = (pedidoId) =>
  chamarApi(`/api/pedidos/${pedidoId}/cobranca`, { metodo: 'POST' })

// Troca de forma da S11: com conversa aberta, o link novo sai ao cliente pelo EasyStok.
export const trocarFormaPagamento = (pedidoId, forma) =>
  chamarApi(`/api/pedidos/${pedidoId}/cobranca/forma`, { metodo: 'POST', corpo: { forma } })

export const registrarPagamentoManual = (pedidoId, valor, metodo) =>
  chamarApi(`/api/pedidos/${pedidoId}/pagamentos`, {
    metodo: 'POST', corpo: { empresaId: lerSessao()?.empresa?.id, pedidoId, valor, metodo },
  })

export const desfazerPagamentoManual = (pedidoId, motivo) =>
  chamarApi(`/api/pedidos/${pedidoId}/pagamento-manual/desfazer`, { metodo: 'POST', corpo: { motivo } })

export const cancelarPedido = (pedidoId, motivo) =>
  chamarApi(`/api/pedidos/${pedidoId}/cancelar`, {
    metodo: 'POST', corpo: { empresaId: lerSessao()?.empresa?.id, id: pedidoId, motivo, origem: 'console' },
    sinal: AbortSignal.timeout(15000),
  })
