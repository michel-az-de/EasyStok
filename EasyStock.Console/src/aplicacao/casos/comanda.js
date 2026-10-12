// Casos de reducer da Frente 3 · Comanda e cardápio (rodada 5, seção 3).
// Arquivo da F3: nenhuma outra frente edita este arquivo, e a F3 nunca edita
// `reducer.js` (ele já importa e espalha `casosComanda` no objeto composto).
// Ações previstas em `aplicacao/acoes.js`: ADICIONAR_ACRESCIMO,
// TIRAR_ACRESCIMO, TIRAR_ITEM_PAGO, SEPARAR_ITEM_ANOTADO,
// MARCAR_CANHOTO_IMPRESSO.
import * as acao from '../acoes'

export const casosComanda = {
  // RN-27: grava que o canhoto daquele pedido já foi enviado para impressão.
  // Por `numero` do pedido, não por id de conversa, porque quem dispara é
  // tanto o "Ver canhoto" manual (BlocoPedido) quanto a fila automática do
  // pagamento (FilaCanhotos), e as duas só têm o pedido em mãos. Hoje é só o
  // carimbo que evita reimprimir sozinho; quando a impressora térmica real
  // entrar, este é o caso que vira o comando pra ela.
  [acao.MARCAR_CANHOTO_IMPRESSO]: (estado, { numero }) => ({
    ...estado,
    conversas: estado.conversas.map((c) => (
      c.pedido?.numero === numero ? { ...c, pedido: { ...c.pedido, canhotoImpresso: true } } : c
    )),
  }),
}
