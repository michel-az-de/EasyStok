// Quando a Ficha recomeça do zero (#2185 F5). Ela remonta ao trocar de conversa, de pedido ou de situação, para
// nenhum formulário nem janela do ciclo anterior atravessar. O pedido ganhar `pedidoId` (a cobrança foi gerada
// aqui, ou o cliente confirmou pelo link e a sincronização trouxe o pedido) NÃO é outro ciclo: a mesma conversa
// segue, e remontar fechava a janela do cadastro com nome, telefone e endereço pela metade.
// `antes` e `depois`: `{ pedidoId, numero }` do pedido da conversa (nulos quando não há).
export function pedidoMudou(antes, depois) {
  if (antes.pedidoId) return depois.pedidoId !== antes.pedidoId
  if (depois.pedidoId) return false
  if (!antes.numero) return false
  return depois.numero !== antes.numero
}
