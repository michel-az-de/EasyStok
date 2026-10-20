// Apresentação do cartão do Balcão (#1868). Puro e sem JSX, para a prova
// (`ferramentas/prova-1868-visual.mjs`) conferir a mesma regra que a tela usa.
//
// Medido no laboratório: "Visitante do site" era o nome em 37 de 39 cartões e a
// terceira linha repetia "Aguardando equipe" e "Chat do site" em 38 de 39. O que
// todo cartão tem não ajuda a escolher: o canal já está no selo do avatar e o
// "Precisa de você" no trilho lateral do cartão e no chip do filtro.

import { origemDaPassagem } from '../../dominio/passagem'

export const NOME_DO_VISITANTE = 'Visitante do site'

// Nome do cartão. O genérico do visitante cede o lugar à comanda, que é o que
// distingue um visitante do outro; o nome inteiro segue no `title`.
export function nomeDoCartao(conversa) {
  const nome = conversa?.nome ?? ''
  const comanda = conversa?.numeroComanda ? `Comanda ${conversa.numeroComanda}` : null
  const titulo = comanda ? `${nome} · ${comanda}` : nome
  if (comanda && nome.trim() === NOME_DO_VISITANTE) return { principal: comanda, complemento: null, titulo }
  return { principal: nome, complemento: comanda, titulo }
}

// Origem da passagem no contexto do cartão. "Chat do site" repete o selo do
// canal no avatar; as outras origens dizem o porquê e ficam.
export function origemNoCartao(passagem) {
  if (!passagem || passagem.origem === 'chat-site') return null
  return origemDaPassagem(passagem)
}

// A etiqueta do estado sai quando não ajuda a escolher o cartão: na passagem ("Aguardando
// equipe", "Passou para você") o cartão ganha o trilho lateral de atenção; em "Automático" e
// "Com você" (#1899: eram 14 de 16 cartões) quem conduz já está na pílula da BarraModo. Nos
// três casos o texto vai para o leitor de tela. Bloqueado, Encerrada, Outro atendente e os
// motivos (reclamação, cobrança venceu, pedido atrasado) continuam escritos.
const ESTADOS_SEM_ETIQUETA = ['passagem', 'automatico', 'voce']
export function mostraEtiquetaDoEstado(estado) {
  return !ESTADOS_SEM_ETIQUETA.includes(estado?.chave)
}

// A terceira linha só existe se sobrar o que dizer nela.
export function temLinhaDeContexto({ etiqueta, contexto }) {
  return Boolean(etiqueta) || (contexto?.length ?? 0) > 0
}
