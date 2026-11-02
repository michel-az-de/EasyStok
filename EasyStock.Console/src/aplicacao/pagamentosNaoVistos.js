// Ponto de "pagamento novo, não visto" no cartão da conversa (#2185 F24). "Visto" é ela ter a conversa à vista:
// abrir a conversa OU o pagamento cair numa conversa que já está aberta com a aba visível. Antes só a abertura
// limpava, e o ponto ficava no cartão da conversa que ela estava olhando. Aba escondida não conta como vista.
export function semOPagamentoDaAberta(naoVistos, selecionadaId, abaVisivel = true) {
  if (!selecionadaId || !abaVisivel || !naoVistos.has(selecionadaId)) return naoVistos
  const novo = new Set(naoVistos)
  novo.delete(selecionadaId)
  return novo
}
