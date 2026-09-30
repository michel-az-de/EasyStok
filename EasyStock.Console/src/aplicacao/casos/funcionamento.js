// Casos de reducer da Frente Horário e loja (pedido do dono, 24/09/2026).
import * as acao from '../acoes'
import { estaAberta } from '../../dominio/funcionamento'

export const casosFuncionamento = {
  // Um toque, Aberta ou Fechada: grava o OPOSTO do que está valendo agora
  // (horário configurado se ninguém tinha tocado o controle ainda), e essa
  // escolha vale por cima do horário até o próximo toque (RN análoga a D4:
  // a volta é decisão dela, nunca do relógio).
  [acao.ALTERNAR_LOJA]: (estado, { agora }) => ({
    ...estado,
    lojaAberta: !estaAberta(agora, { funcionamento: estado.funcionamento, lojaAberta: estado.lojaAberta }),
  }),

  [acao.EDITAR_FUNCIONAMENTO]: (estado, { dia, campos }) => ({
    ...estado,
    funcionamento: {
      ...estado.funcionamento,
      [dia]: { ...estado.funcionamento[dia], ...campos },
    },
  }),
}
