// Frente 79 (rodada 10) · Cliente simulado que reage. Domínio puro: nenhum
// import fora de `dominio`, o dado sempre chega por parâmetro (regra da
// fronteira de camada, `ferramentas/verificar-camadas.mjs`).
//
// O pedido do dono (25/09/2026 23h42): "as simulações não estão completas,
// precisa simular real com mensagens mesmo, ver a interação mais
// profundamente, o protótipo tem que simular o sistema funcionando de
// verdade". Hoje cada cenário do Simular é um roteiro fixo com tempo
// marcado: o cliente manda as falas dele e não reage ao que a Thatiane faz.
// Este arquivo decide O QUE o cliente simulado diz de volta quando ela envia
// o cardápio, quando ele escolhe um prato, quando o pedido é despachado e
// quando chega a hora de avaliar. Quem observa o estado e decide QUANDO
// (o atraso plausível) é `aplicacao/useReacaoClienteSimulado.js`; quem
// aplica no estado é o novo caso `SIMULAR_EVENTO_CLIENTE` em
// `aplicacao/casos/simulacao.js`.
//
// Fronteira (registro 79): `respostaParaMensagemDeCliente` (em
// `simulacoes.js`, a frente 80 é dona) e `dominio/automacao.js` continuam
// intocados. Tudo daqui é aditivo, ao lado.

import { podeVender } from './cardapio'
import { favoritoDasTags } from './simulacoes'

// --- Atraso plausível -------------------------------------------------------
// "com atraso plausível no relógio simulado": o relógio efetivo é o real
// (agoraEfetivo = agora + deslocamentoMs), então um atraso de alguns
// segundos de parede já é um atraso de verdade no relógio simulado, sem
// travar quem está testando. Minutos de trajeto (item c) somam no
// deslocamento em vez de segurar o navegador parado.
export function atrasoAleatorioMs(minimoMs, maximoMs) {
  return Math.round(minimoMs + Math.random() * (maximoMs - minimoMs))
}

export const ATRASO_ESCOLHA_RECORRENTE_MS = [10000, 30000]
export const ATRASO_CONFIRMA_RECEBIMENTO_MS = [2500, 4500]
export const ATRASO_PROMPT_AVALIACAO_MS = [1800, 2600]
export const ATRASO_RESPOSTA_AVALIACAO_MS = [1600, 2600]

// Minutos de relógio (deslocamento) que o trajeto até a entrega consome,
// para o carimbo da mensagem de recebimento parecer viagem de verdade, não
// resposta instantânea (UC-04 passo 8, US-045).
export const MINUTOS_TRAJETO_ENTREGA = [6, 12]

// --- (b) Recorrente responde à oferta de favorito ou novidade --------------
// US-002, RN-07, UC-03 passo 4: a saudação nunca afirma hábito, sempre
// pergunta; aqui o cliente simulado responde a essa pergunta, escolhendo o
// favorito quando existe (RN-07 lê a tag "Gosta de X" da ficha) ou a
// novidade quando não há favorito cadastrado.
export function escolhaRecorrente(tags, cardapio) {
  const favorito = favoritoDasTags(tags)
  const item = favorito ? itemPorNomeParecido(cardapio, favorito) : null
  if (item) {
    return { texto: `Pode ser o de sempre, ${item.nome.toLowerCase()}! Bora fechar.`, item }
  }
  const novidade = primeiroDisponivel(cardapio)
  if (!novidade) return null
  return { texto: `Vou tentar a novidade hoje, pode ser ${novidade.nome.toLowerCase()}!`, item: novidade }
}

function itemPorNomeParecido(cardapio, pista) {
  const alvo = pista.toLowerCase()
  return cardapio.find((item) => podeVender(item) && item.nome.toLowerCase().includes(alvo)) ?? null
}

function primeiroDisponivel(cardapio) {
  return cardapio.find((item) => podeVender(item)) ?? null
}

// --- (a) Cardápio por link: eco do pedido na conversa -----------------------
// UC-01 passos 7 e 8: depois que o cliente fecha o carrinho no link, a
// conversa da Thatiane precisa de uma mensagem que pareça o cliente
// falando, não só um cartão de sistema. `linhas` já vem pronto de
// `dominio/pedido.js: itensDetalhados`.
export function textoEcoPedidoCardapioLink(linhas) {
  const resumo = linhas.map((l) => `${l.qtd}× ${l.produto?.nome ?? l.sku}${l.obs ? ` (${l.obs})` : ''}`).join(', ')
  return `Oi! Fechei o pedido pelo cardápio: ${resumo}. Pode confirmar pra mim?`
}

// --- (c) Confirmação de recebimento -----------------------------------------
// UC-04 passo 8, US-045: depois do aviso de saída, o cliente confirma que
// chegou. Texto curto, tom de cliente satisfeito, sem repetir o
// agradecimento que a própria casa já manda.
export const textoConfirmaRecebimento = () => 'Chegou! Muito obrigada, ficou ótimo de novo.'

// --- (d) Avaliação de um toque, distinta de reclamação ----------------------
// US-046, RN-34, RN-35: "duas opções de resposta, positiva e negativa, em
// um toque". `formato: 'avaliacaoPedido'` (a casa pergunta) e
// `formato: 'avaliacaoResposta'` (o toque do cliente) são bolhas próprias,
// nunca texto livre, para não repetir o roteiro de Reclamação (achado P2.7).
export const TEXTO_PROMPT_AVALIACAO = 'Como foi o seu pedido de hoje? Toque para avaliar.'

export const AVALIACOES = { POSITIVA: 'positiva', NEGATIVA: 'negativa' }

export function textoRespostaAvaliacao(valor) {
  return valor === AVALIACOES.NEGATIVA ? 'Não gostei dessa vez.' : 'Adorei, muito obrigada!'
}

// RN-35 (mesma trava da Reclamação): a ocorrência guarda o relato do
// cliente. Toque de avaliação não escreve texto nenhum, então o relato
// precisa dizer isso, para quem ler o histórico da ocorrência não achar que
// o cliente digitou uma reclamação.
export const RELATO_AVALIACAO_NEGATIVA = 'Avaliação negativa em um toque, sem relato escrito.'

// --- (e) Pergunta fora do roteiro nunca fica muda ---------------------------
// `ehPerguntaSemResposta` e `MOTIVO_PERGUNTA_FORA_DO_ROTEIRO` mudaram de
// morada para `dominio/mensagem.js` na rodada 13 (issue #41): o heurístico
// não é nada específico de cliente simulado, e `dominio/automatico.js`
// (Precisa de você) passou a reaproveitar o mesmo texto para não inventar uma
// segunda conta. Importar daqui criava ciclo
// automatico -> clienteSimulado -> simulacoes -> automatico
// (oxlint import/no-cycle). Quem usava, agora importa de `./mensagem.js`.
