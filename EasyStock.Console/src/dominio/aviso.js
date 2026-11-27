// #1807 (revisão de 14/10): o que o Balcão percebe entre dois retratos da lista vira aviso que
// aparece de fato, na tela (toast) e no navegador, além do som. Função pura: quem lê o estado é
// `aplicacao/useAvisoSonoro.js`, quem mostra é o Provider (`aplicacao/useFilaDeAvisos.js`).
//
// Retrato: Map de id da conversa -> { pago, precisa, ultimaMensagemClienteId, etapa, nome, motivo }.
// O aviso leva só nome e evento, nunca o texto da mensagem do cliente.
import { EVENTOS_DE_SOM } from './automatico.js'
import { mudancasDoRetrato } from './avisoSonoro.js'
import { rotuloDoEstado } from './pedido.js'
import { indiceDoPasso } from './esteira.js'

// O valor vira parte da `tag` da notificação do navegador; `precisa` é a tag de antes (rodada 10).
export const TIPOS_DE_AVISO = {
  CONTATO_NOVO: 'contato-novo',
  PASSOU_PARA_VOCE: 'precisa',
  MENSAGEM_NOVA: 'mensagem-nova',
  PAGAMENTO_CONFIRMADO: 'pagamento',
  ETAPA_DO_PEDIDO: 'etapa',
}

// Mais que isso na pilha vira parede; o mais velho sai.
export const LIMITE_DE_AVISOS = 4

const primeiro = (nome) => String(nome ?? '').trim().split(/\s+/)[0] || 'Cliente'

const criar = (tipo, conversaId, titulo, corpo, som, destaca = false) => ({ tipo, conversaId, titulo, corpo, som, destaca })

export function avisosDoRetrato(antes, retrato) {
  const avisos = []
  for (const mudanca of mudancasDoRetrato(antes, retrato)) {
    const { id } = mudanca
    const linha = retrato.get(id) ?? {}
    const nome = primeiro(linha.nome)
    if (mudanca.contatoNovo) {
      avisos.push(criar(TIPOS_DE_AVISO.CONTATO_NOVO, id, 'Contato novo', `${nome} entrou em contato`, EVENTOS_DE_SOM.NOVO_ATENDIMENTO))
    }
    // Achado 6 (rodada 10): passou para você e mensagem nova no mesmo retrato avisam uma vez só,
    // pelo mais importante (precisa de ação > chegou mensagem).
    if (mudanca.passouAgora) {
      avisos.push(criar(TIPOS_DE_AVISO.PASSOU_PARA_VOCE, id, 'Precisa de você', linha.motivo || nome, EVENTOS_DE_SOM.PASSOU_PARA_VOCE, true))
    } else if (mudanca.mensagemNova) {
      avisos.push(criar(TIPOS_DE_AVISO.MENSAGEM_NOVA, id, 'Mensagem nova', `${nome} mandou mensagem`, EVENTOS_DE_SOM.NOVO_ATENDIMENTO))
    }
    if (mudanca.sinalAgora) {
      avisos.push(criar(TIPOS_DE_AVISO.PAGAMENTO_CONFIRMADO, id, 'Sinal pago', `${nome} pagou o sinal`, EVENTOS_DE_SOM.PAGAMENTO_CONFIRMADO, true))
    }
    if (mudanca.pagouAgora) {
      avisos.push(criar(TIPOS_DE_AVISO.PAGAMENTO_CONFIRMADO, id, 'Pagamento confirmado', `${nome} pagou o pedido`, EVENTOS_DE_SOM.PAGAMENTO_CONFIRMADO, true))
    }
    if (mudanca.etapaMudou) {
      // #3374 (C11): para frente é "avançou" (quem respondeu vê o efeito); voltar ou sair da trilha segue neutro.
      // Etapa fora da trilha (índice -1) em qualquer lado não permite dizer que avançou.
      const de = indiceDoPasso(antes?.get(id)?.etapa)
      const para = indiceDoPasso(linha.etapa)
      const avancou = de >= 0 && para > de
      avisos.push(criar(TIPOS_DE_AVISO.ETAPA_DO_PEDIDO, id, avancou ? 'Pedido avançou' : 'Pedido mudou de etapa', `${nome}: ${rotuloDoEstado(linha.etapa)}`, null))
    }
  }
  return avisos
}

// #3339 (R2F-06): a mensagem nova da conversa que está à vista já aparece no fio e o som toca; o toast só repetia e
// cobria o fim da barra, onde fica "Assumir conversa". Só esse aviso, só para essa conversa, só com a aba visível.
export function avisoJaEstaNaTela(aviso, { selecionadaId, painelNaTela, abaVisivel }) {
  return aviso?.tipo === TIPOS_DE_AVISO.MENSAGEM_NOVA && Boolean(selecionadaId) && aviso.conversaId === selecionadaId
    && Boolean(painelNaTela) && Boolean(abaVisivel)
}

// Pilha da tela: o aviso novo entra no fim; o do mesmo tipo e da mesma conversa sai (a segunda
// mensagem da Marina substitui a primeira, não empilha), e acima do limite o mais velho cai.
export function filaComAviso(fila, aviso, id) {
  const resto = fila.filter((a) => !(a.tipo === aviso.tipo && a.conversaId === aviso.conversaId))
  return [...resto, { ...aviso, id }].slice(-LIMITE_DE_AVISOS)
}
