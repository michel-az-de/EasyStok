// O que a polling da inbox relê a cada ciclo (F07, itens 2 e 7). Puro: recebe o
// resumo da API, o que está no cache e a conversa aberta na tela.
//
// Mensagens: mudança de status (Falhou, Entregue) não mexe em `ultimaMensagemEm` e a dona
// precisa ver que a mensagem não saiu. #1786: por isso a conversa selecionada é relida
// enquanto alguma mensagem do cache ainda pode mudar sozinha (saída sem entrega, foto do
// cliente baixando) e no ciclo lento; parada, fica no cache. As outras só quando mudaram.
//
// Pedido: relido para a selecionada, para a que mudou e, nas outras, só no ciclo
// lento (1 min). Pedido entregue ou cancelado fica no cache até o id mudar.
// #1576: "mudou" é o status do pedido quando o resumo o traz (`pedidoStatus`);
// na API que ainda não manda o campo, é qualquer mensagem nova da conversa.
import { instante } from '../infra/api/traducaoConversas'
import { ROTA_PRINCIPAL, rotaDaHash } from '../dominio/rota'

export const STATUS_FINAIS_DO_PEDIDO = new Set(['entregue', 'cancelado'])

//
// Conversa encerrada que nunca foi lida não busca mensagens na carga (#1287): a
// inbox pode ter centenas, e o cartão mostra `ultimaMensagemTexto` do resumo.
// Ela é lida ao ser aberta ou quando volta a ter atendimento.
export function deveRelerMensagens(resumo, guardado, selecionadaId, { cicloLento = false } = {}) {
  const selecionada = resumo.id === selecionadaId
  if (!guardado) return selecionada || resumo.situacao !== 'Encerrada'
  if (guardado.ultima !== resumo.ultimaMensagemEm) return true
  return selecionada && (cicloLento || (guardado.mensagens ?? []).some(podeMudarSozinha))
}

// #1786: o que muda no servidor sem mexer em `ultimaMensagemEm`. Saída até ser entregue (o reenvio
// troca o Falhou); a nota interna do sistema, sem id do canal, fica Pendente para sempre e não conta.
// Entrada com arquivo que o EasyStok ainda baixa do canal.
const STATUS_ASSENTADOS = new Set(['Entregue', 'Lida'])
const ENTRADA_COM_ARQUIVO = new Set(['Imagem', 'Audio', 'Documento'])
const podeMudarSozinha = (m) => (m.direcao === 'Saida'
  ? !STATUS_ASSENTADOS.has(m.status) && !(m.autor === 'Sistema' && !m.externoId)
  : ENTRADA_COM_ARQUIVO.has(m.tipoConteudo) && !m.midiaChave && !m.midiaFalhou)

// #1786: aba oculta não lê a inbox (o laço de 5 s rodava com o tablet na gaveta). Sem documento (node,
// provas), conta como visível. Oculta por um minuto ou mais, a volta varre a inbox inteira.
export const abaVisivel = (documento = globalThis.document) => documento?.visibilityState !== 'hidden'
// #1966: a conversa selecionada só está diante de alguém com a aba visível e o Balcão na rota. A seleção
// sobrevive à ida ao Hall e às telas de ajuste (o Provider é o mesmo), e lá ninguém lê a conversa.
export const conversaNaTela = (documento = globalThis.document, local = globalThis.location) =>
  abaVisivel(documento) && rotaDaHash(local?.hash, { fonteApi: true }).tipo === ROTA_PRINCIPAL
export const deveVarrerAoVoltar =(ocultaDesde, agora, limiteMs = 60000) => ocultaDesde != null && agora - ocultaDesde >= limiteMs

export function deveRelerPedido(resumo, guardado, { selecionadaId = null, cicloLento = false } = {}) {
  if (!resumo.pedidoEmAndamentoId) return false
  if (!guardado || guardado.pedidoId !== resumo.pedidoEmAndamentoId) return true
  // Status no resumo diferente do que está no cache relê mesmo o pedido que já tinha fechado. A comparação
  // é com o status que o RESUMO trazia na última leitura: se um dia ele divergir do formato do pedido lido,
  // a diferença relê uma vez, não a cada volta.
  const comStatus = resumo.pedidoStatus != null
  if (comStatus && (guardado.statusDoResumo ?? guardado.dados?.status) !== resumo.pedidoStatus) return true
  if (STATUS_FINAIS_DO_PEDIDO.has(guardado.dados?.status)) return false
  const mudou = !comStatus && guardado.ultima !== resumo.ultimaMensagemEm
  return resumo.id === selecionadaId || mudou || cicloLento
}

// #1576: a volta rápida lê só a página 1 da inbox e o laço guarda o resto. `mapa` é id -> resumo,
// já na ordem da tela. A varredura completa troca o mapa (quem sumiu sai); a página 1 atualiza quem
// veio. O reducer substitui a lista pelo que recebe, então o laço despacha sempre o mapa inteiro.
//
// Ordem: a da API, mais recente primeiro. Ela não desempata `ultimaMensagemEm` igual, então o
// empate fica como já estava (conversa nova depois): sem isso a lista pula e a tela parada re-renderiza.
const quando = (resumo) => Date.parse(instante(resumo.ultimaMensagemEm)) || 0

export function fundirPagina(mapa, itens, { completa = false } = {}) {
  const lidos = new Map()
  for (const item of itens) if (!lidos.has(item.id)) lidos.set(item.id, item)
  const fila = []
  for (const [id, anterior] of mapa) {
    if (lidos.has(id)) fila.push(lidos.get(id))
    else if (!completa) fila.push(anterior)
  }
  for (const [id, item] of lidos) if (!mapa.has(id)) fila.push(item)
  const tempo = new Map(fila.map((resumo) => [resumo.id, quando(resumo)]))
  // `sort` é estável: quem empata fica na ordem da fila.
  fila.sort((a, b) => tempo.get(b.id) - tempo.get(a.id))
  return new Map(fila.map((resumo) => [resumo.id, resumo]))
}

// #1576: `fn` roda em no máximo `limite` itens por vez (o pool do banco tem 10 conexões e a carga
// da inbox abria uma leitura por conversa de uma vez). Resultado na ordem de entrada; a primeira
// rejeição propaga, como em `Promise.all`, e a fila para de puxar.
export async function emFila(itens, limite, fn) {
  const resultado = Array.from({ length: itens.length })
  let proximo = 0
  let quebrou = false
  async function puxar() {
    while (proximo < itens.length && !quebrou) {
      const i = proximo
      proximo += 1
      try {
        resultado[i] = await fn(itens[i], i)
      } catch (erro) {
        quebrou = true
        throw erro
      }
    }
  }
  await Promise.all(Array.from({ length: Math.min(limite, itens.length) }, puxar))
  return resultado
}

// Dossiê do cliente (#1276, #1474): a Ficha lê telefone, tags e notas do cadastro. Vale para a
// conversa aberta por clique e para a que a carga restaurou ou escolheu sozinha.
export const deveCarregarDossie = (conversa) => Boolean(conversa?.clienteId && !conversa.cliente?.daApi)

// Aviso da faixa quando o cardápio da vitrine não vem (#1474): a vitrine desligada é um 404
// com texto técnico; a dona precisa saber o que ligar.
export function avisoDoCardapio(erro) {
  if (erro?.status === 404 && /vitrine ativa/i.test(erro.message ?? '')) {
    return 'A loja online está desligada: o cardápio não carrega até ligar a vitrine.'
  }
  return `Cardápio: ${erro?.message ?? 'não carregou.'}`
}

// #1241: só falta de rede vira queda de conexão (faixa do papel). Erro do EasyStok (500,
// 403...) é outro aviso: a internet está lá, o canhoto não precisa assumir.
export const quedaDaSincronizacao = (erro) => erro?.codigo === 'SEM_CONEXAO'

// Evento da operação (SSE) que o laço atende. Pedido e comanda (S18) releem o pedido; `ready` (conexão
// nova) relê todos. #1966: `conversa.atualizada` (o visitante do chat do site se identificou) relê a
// conversa, que traz o nome novo. O resto fica com a volta de 5 s.
export function alvoDoEvento(evento, dados) {
  if (evento === 'ready') return { tipo: 'pronto' }
  if (evento?.startsWith('pedido.') && dados?.pedidoId) return { tipo: 'pedido', pedidoId: dados.pedidoId }
  if (evento === 'conversa.comanda_atualizada' && dados?.conversaId) return { tipo: 'comanda', conversaId: dados.conversaId }
  if (evento === 'conversa.atualizada' && dados?.conversaId) return { tipo: 'conversa', conversaId: dados.conversaId }
  return null
}
