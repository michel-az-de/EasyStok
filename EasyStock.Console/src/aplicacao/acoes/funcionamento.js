// Criadores de ação da Frente Horário e loja (pedido do dono, 24/09/2026).
// `AtendimentoProvider.jsx` chama `criarAcoesFuncionamento(despachar)` e
// espalha o resultado no objeto `acoes` do contexto, mesmo padrão das
// outras frentes (ver `acoes/simulacao.js`).
import * as acao from '../acoes'

export function criarAcoesFuncionamento(despachar) {
  return {
    alternarLoja: (agora) => despachar({ tipo: acao.ALTERNAR_LOJA, agora }),
    editarFuncionamento: (dia, campos) => despachar({ tipo: acao.EDITAR_FUNCIONAMENTO, dia, campos }),
  }
}
