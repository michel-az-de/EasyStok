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

// A etiqueta do estado ("Aguardando equipe", "Passou para você") sai quando o
// motivo é a passagem: o cartão ganha o trilho lateral de atenção e o texto vai
// para o leitor de tela. Os outros motivos (reclamação, cobrança venceu, pedido
// atrasado) e os estados sem motivo continuam escritos.
export function mostraEtiquetaDoEstado(motivo) {
  return motivo?.chave !== 'passagem'
}

// A terceira linha só existe se sobrar o que dizer nela.
export function temLinhaDeContexto({ etiqueta, contexto }) {
  return Boolean(etiqueta) || (contexto?.length ?? 0) > 0
}
