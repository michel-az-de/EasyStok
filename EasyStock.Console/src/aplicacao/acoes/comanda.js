// Criadores de ação da Frente 3 · Comanda e cardápio (rodada 5, seção 3).
// `AtendimentoProvider.jsx` chama `criarAcoesComanda(despachar)` e espalha o
// resultado no objeto `acoes` do contexto; a F3 só mexe aqui dentro, nunca
// no Provider. `despachar` já é o `dispatch` do reducer.
import * as acao from '../acoes'

export function criarAcoesComanda(despachar) {
  return {
    marcarCanhotoImpresso: (numero) => despachar({ tipo: acao.MARCAR_CANHOTO_IMPRESSO, numero }),
  }
}
