// #2005 (console-08): a ordem em que as coisas aconteceram neste navegador. Cada volta da inbox leva o
// instante em que SAIU, e cada confirmação do servidor (assumir, devolver, encerrar, reabrir) leva o instante
// em que chegou. O reducer descarta o resumo que saiu antes da confirmação: ele traz a conversa como era.
// Crescente de verdade (dois pedidos no mesmo milissegundo ainda se ordenam), como a `leitura` do pedido (#1548).
let ultimo = 0

export const proximoInstante = () => {
  ultimo = Math.max(Date.now(), ultimo + 1)
  return ultimo
}
