// O que a polling da inbox relê a cada ciclo (F07, itens 2 e 7). Puro: recebe o
// resumo da API, o que está no cache e a conversa aberta na tela.
//
// Mensagens: a conversa selecionada é relida sempre, porque mudança de status
// (Falhou, Entregue) não mexe em `ultimaMensagemEm` e a dona precisa ver que a
// mensagem não saiu. As outras só quando a conversa mudou.
//
// Pedido: relido para a selecionada, para a que mudou e, nas outras, só no ciclo
// lento (1 min). Pedido entregue ou cancelado fica no cache até o id mudar.
// #1576: "mudou" é o status do pedido quando o resumo o traz (`pedidoStatus`);
// na API que ainda não manda o campo, é qualquer mensagem nova da conversa.
import { instante } from '../infra/api/traducaoConversas'

export const STATUS_FINAIS_DO_PEDIDO = new Set(['entregue', 'cancelado'])

//
// Conversa encerrada que nunca foi lida não busca mensagens na carga (#1287): a
// inbox pode ter centenas, e o cartão mostra `ultimaMensagemTexto` do resumo.
// Ela é lida ao ser aberta ou quando volta a ter atendimento.
export function deveRelerMensagens(resumo, guardado, selecionadaId) {
  if (resumo.id === selecionadaId) return true
  if (!guardado) return resumo.situacao !== 'Encerrada'
  return guardado.ultima !== resumo.ultimaMensagemEm
}

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
