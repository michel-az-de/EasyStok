import { dataIsoNoFuso, horaCurta } from './formato'
import { agruparPorLinha } from './pedido'

// KDS sobre Pedido (S19), usado pela cozinha no modo API (F05). Os status são os
// tokens da API (`StatusPedidoMapper`); a máquina de estados mora na API, aqui só
// fica o próximo toque de cada coluna, como na cozinha da demonstração.
export const COLUNAS_KDS = [
  { status: 'aguardando', rotulo: 'Aguardando', toque: 'Começar preparo' },
  { status: 'preparando', rotulo: 'Em preparo', toque: 'Marcar pronto' },
  { status: 'pronto', rotulo: 'Pronto', toque: 'Saiu para entrega' },
  { status: 'saiu_para_entrega', rotulo: 'Saiu para entrega', toque: 'Marcar entregue' },
]

const PROXIMO = {
  aguardando: 'preparando',
  preparando: 'pronto',
  pronto: 'saiu_para_entrega',
  saiu_para_entrega: 'entregue',
}

// `{ status, toque }` do próximo passo, ou `null` quando o pedido não anda mais.
export function proximoStatusKds(status) {
  const proximo = PROXIMO[status]
  if (!proximo) return null
  return { status: proximo, toque: COLUNAS_KDS.find((c) => c.status === status).toque }
}

const UMA_HORA_MS = 60 * 60 * 1000
const minutos = (ms) => Math.max(1, Math.round(Math.abs(ms) / 60000))

// S21: quando o preparo precisa começar e se já passou. `atrasado` vem da API
// (ela sabe também do pedido aberto de ontem); o texto só conta os minutos.
// Issue #1446: depois que o preparo começou, "Começar em" mentiria; sem `status` (quem só
// tem as datas) segue como antes.
export function avisoDeInicio({ inicioPrevistoEm, atrasado, status }, agora) {
  if (!inicioPrevistoEm) return atrasado ? { atrasado: true, texto: 'Atrasado: pedido de outro dia' } : null
  if (status && status !== 'aguardando' && !atrasado) return null
  const falta = Date.parse(inicioPrevistoEm) - agora
  if (atrasado) return { atrasado: true, texto: `Atrasado ${minutos(falta)} min: devia ter começado` }
  if (falta <= 0) return { atrasado: false, texto: 'Começar agora' }
  // #1474: com mais de 1 h pela frente, a hora diz mais que "Começar em 754 min".
  if (falta > UMA_HORA_MS) {
    const dia = dataIsoNoFuso(Date.parse(inicioPrevistoEm))
    const quando = dia === dataIsoNoFuso(agora) ? ''
      : dia === dataIsoNoFuso(agora + 24 * UMA_HORA_MS) ? 'amanhã '
        : `em ${dia.slice(8, 10)}/${dia.slice(5, 7)} `
    return { atrasado: false, texto: `Começar ${quando}às ${horaCurta(inicioPrevistoEm)}` }
  }
  return { atrasado: false, texto: `Começar em ${minutos(falta)} min` }
}

// --- Cozinha viva (issue #1446): o que a cozinha do protótipo faz, sobre o cartão do KDS ---

// Trilha de etapas da comanda: as quatro colunas e o fim da esteira.
export const ETAPAS_KDS = [...COLUNAS_KDS, { status: 'entregue', rotulo: 'Entregue' }]
const rotuloDoStatus = (status) => ETAPAS_KDS.find((e) => e.status === status)?.rotulo ?? status

// Linha do item na API (`paraServir`, `prepararEmCasa`, `preparar_em_casa`) → chave do
// console (`servir`, `casa`). Item sem linha cai em "servir": nada some do papel nem da tela.
const chaveDaLinha = (linha) => {
  const bruto = String(linha ?? '').toLowerCase().replace(/[^a-z]/g, '')
  return bruto === 'prepararemcasa' || bruto === 'casa' ? 'casa' : 'servir'
}

// Molho mora na observação, como no canhoto do protótipo.
const observacaoDoItem = (item) => [item.molho && `Molho ${item.molho}`, item.observacao].filter(Boolean).join(' · ')

// Itens do cartão no formato de `dominio/pedido.js` (`{ sku, qtd, obs, produto }`), para a
// comanda em tela e o canhoto de papel usarem o mesmo agrupamento do protótipo.
export const itensDoKds = (pedido) => (pedido?.itens ?? []).map((item, indice) => ({
  sku: `${indice}`,
  qtd: item.qtd,
  obs: observacaoDoItem(item),
  produto: { nome: item.nome, porcao: item.variacao ?? '', linha: chaveDaLinha(item.linha) },
}))

export const gruposDoKds = (pedido, linhas) => agruparPorLinha(itensDoKds(pedido), linhas)

export const nomeNaComanda = (pedido) => [pedido.clienteNome ?? 'Cliente', pedido.clienteApt].filter(Boolean).join(' · ')

// Dados de `documentoDoCanhoto` (dominio/impressao.js): o MESMO PDF de 80 mm da cozinha do
// protótipo, sem preço (não é cupom fiscal) e com a observação do pedido.
export const canhotoDoKds = (pedido, linhas) => ({
  pedido: { numero: pedido.numeroCurto, cobranca: null },
  itens: itensDoKds(pedido),
  linhas,
  nomeCliente: nomeNaComanda(pedido),
  endereco: pedido.endereco ?? null,
  faixa: pedido.janela?.label ?? null,
  observacoes: pedido.observacoes ?? null,
})

export const filtrarPorLinha = (pedidos, linha) => (linha
  ? pedidos.filter((p) => (p.itens ?? []).some((i) => chaveDaLinha(i.linha) === linha))
  : pedidos)

// Entre duas leituras da fila: quem entrou, quem mudou de coluna e quem saiu. É o que a tela
// anima. A primeira leitura não anima nada (senão a fila inteira "chegaria" ao abrir).
export function transicaoDaFila(anterior, atual) {
  if (!anterior) return { novos: [], mudaram: [], sairam: [] }
  const antes = new Map(anterior.map((p) => [p.id, p]))
  const agoraIds = new Set(atual.map((p) => p.id))
  return {
    novos: atual.filter((p) => !antes.has(p.id)).map((p) => p.id),
    mudaram: atual.filter((p) => antes.has(p.id) && antes.get(p.id).status !== p.status).map((p) => p.id),
    sairam: anterior.filter((p) => !agoraIds.has(p.id)),
  }
}

// Soltar = o mesmo toque do botão: só a coluna do próximo passo aceita. Soltar na própria
// coluna não é recusa, o cartão só volta.
export function respostaAoSoltarKds(status, destino) {
  if (destino === status) return { aceita: false, motivo: null }
  const proximo = proximoStatusKds(status)
  if (!proximo) return { aceita: false, motivo: 'Pedido entregue não anda mais na esteira.' }
  if (destino !== proximo.status) {
    return { aceita: false, motivo: `Um passo por vez: daqui o pedido só vai para ${rotuloDoStatus(proximo.status)}.` }
  }
  return { aceita: true, motivo: null }
}

// Quanto antes do início previsto o cartão começa a chamar atenção.
export const MINUTOS_DE_AVISO = 15

// Urgência do cartão que ainda aguarda: atrasado (a API decide), agora (o início chegou),
// logo (faltam até 15 min) ou nada. Depois de começar o preparo, não há mais urgência.
export function urgenciaDoPedido(pedido, agora) {
  if (pedido.status !== 'aguardando') return null
  if (pedido.atrasado) return 'atrasado'
  if (!pedido.inicioPrevistoEm) return null
  const falta = Date.parse(pedido.inicioPrevistoEm) - agora
  if (falta <= 0) return 'agora'
  return falta <= MINUTOS_DE_AVISO * 60000 ? 'logo' : null
}

// A gaveta de canhotos da Cozinha: pendentes cujo pedido está na fila do KDS, mais antigo
// primeiro. Pendente de pedido que já saiu da cozinha não é dela.
export function impressoesDaCozinha(fila, pedidos) {
  const naCozinha = new Set((pedidos ?? []).map((p) => p.id))
  return (fila ?? [])
    .filter((i) => i.status === 'pendente' && naCozinha.has(i.pedidoId))
    .sort((a, b) => Date.parse(a.criadaEm) - Date.parse(b.criadaEm))
}

// Barra de tempo do cartão que aguarda: fração (1 a 0) do tempo entre o pagamento (ou a
// criação) e o início previsto que ainda falta. Sem início previsto, nada a medir.
export function tempoAteInicio(pedido, agora) {
  if (pedido.status !== 'aguardando' || !pedido.inicioPrevistoEm) return null
  const inicio = Date.parse(pedido.inicioPrevistoEm)
  const base = Date.parse(pedido.pagoEm ?? pedido.criadoEm)
  const total = inicio - base
  if (!(total > 0)) return agora >= inicio ? 0 : 1
  return Math.min(1, Math.max(0, (inicio - agora) / total))
}

// Fila vazia (#1474): a cozinha mostra um dia por vez. O pedido pago para amanhã não aparece
// em Hoje, e a tela dizia "Cozinha em dia" como se nada faltasse.
export const textoDaCozinhaVazia = ({ amanha }) => (amanha
  ? 'Nenhum pedido pago para amanhã ainda.'
  : 'Cozinha em dia hoje. O pedido pago para hoje aparece aqui sozinho; o de amanhã, em Amanhã.')
