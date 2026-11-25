// #3363 (S2): Esc no campo de mensagem devolve o foco ao cartão da conversa aberta, na fila do Balcão. Depois de responder,
// voltar à lista custava 11 Shift+Tab (medido em modo API). Feature não importa feature: o seletor do cartão (o `role=list`
// e o `data-conversa-id` de `caixa-de-entrada/`) mora aqui também; a `prova-3363-fila-teclado.mjs` abre a tela de verdade e
// reprova se um dos dois mudar sozinho. Sem cartão na tela (celular, conversa aberta fora da lista) o foco não anda.

export function devolverFocoAoCartaoAberto(documento = document) {
  const cartao = documento.querySelector('[role=list] button[aria-current="true"][data-conversa-id]')
  if (!cartao) return false
  cartao.focus()
  return documento.activeElement === cartao
}
