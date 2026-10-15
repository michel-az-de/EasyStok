// Criadores de ação da frente Fidelidade e cupons (rodada 13, issue #45,
// registro 107). Mesmo molde de `aplicacao/acoes/<tema>.js`: só mexe em
// `AtendimentoProvider.jsx` na linha de import e na linha que espalha
// `criarAcoesFidelidade(despachar)`.
import * as acao from '../acoes'
import { proximoId } from '../../infra/repositorioConversas'

export function criarAcoesFidelidade(despachar) {
  return {
    // Gestão · Cupons.
    criarCupom: (dados) => despachar({ tipo: acao.CRIAR_CUPOM, dados, cupomId: proximoId('cupom') }),
    editarCupom: (id, dados) => despachar({ tipo: acao.EDITAR_CUPOM, id, dados }),
    alternarCupomAtivo: (id) => despachar({ tipo: acao.ALTERNAR_CUPOM_ATIVO, id }),

    // Cobrança (ficha) · aplicar/remover cupom do pedido em atendimento.
    aplicarCupom: (conversaId, codigo, agora) => despachar({
      tipo: acao.APLICAR_CUPOM, id: conversaId, codigo, agora,
    }),
    removerCupomDoPedido: (conversaId) => despachar({ tipo: acao.REMOVER_CUPOM_DO_PEDIDO, id: conversaId }),

    // Gestão · regra de fidelidade (proposta ajustável).
    editarRegraFidelidade: (dados) => despachar({ tipo: acao.EDITAR_REGRA_FIDELIDADE, dados }),

    // Gestão · catálogo de recompensas.
    criarRecompensa: (dados) => despachar({
      tipo: acao.CRIAR_RECOMPENSA, dados, recompensaId: proximoId('recompensa'),
    }),
    editarRecompensa: (id, dados) => despachar({ tipo: acao.EDITAR_RECOMPENSA, id, dados }),
    alternarRecompensaAtiva: (id) => despachar({ tipo: acao.ALTERNAR_RECOMPENSA_ATIVA, id }),

    // Gestão · sorteios.
    criarSorteio: (dados) => despachar({ tipo: acao.CRIAR_SORTEIO, dados, sorteioId: proximoId('sorteio') }),

    // Ficha do cliente · "Resgatar". `saldoDisponivel` já vem calculado da
    // tela (que tem o histórico do cliente); o reducer só faz a guarda final
    // (ver `aplicacao/casos/fidelidade.js`).
    resgatarRecompensa: (cadastroId, nomeCliente, recompensaId, saldoDisponivel, agora) => despachar({
      tipo: acao.RESGATAR_RECOMPENSA,
      cadastroId,
      nomeCliente,
      recompensaId,
      saldoDisponivel,
      agora,
      resgateId: proximoId('resgate'),
    }),
  }
}
