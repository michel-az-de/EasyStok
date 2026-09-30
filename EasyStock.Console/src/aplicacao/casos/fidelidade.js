// Casos de reducer da frente Fidelidade e cupons (rodada 13, issue #45,
// registro 107). Arquivo da frente: nenhuma outra frente edita este arquivo,
// e esta frente nunca edita `reducer.js` além da linha de import e da linha
// que espalha `casosFidelidade` (mesmo molde de `casos/lote.js`).
//
// Config, cupons, recompensas e sorteios moram em `estado.fidelidade`
// (estado inicial em `reducer.js`, "linha mínima de registro"). Pontos
// GANHOS não são um contador aqui dentro: são derivados do histórico de
// pedidos pagos por `dominio/fidelidade.js` (saldoDePontos), lido direto na
// tela (ficha do cliente). Só o RESGATE (que gasta ponto) precisa de estado
// próprio, porque gastar não é algo que dê pra recalcular a partir do pedido.
import * as acao from '../acoes'
import {
  comParticipante, novoCupom, novoSorteio, novaRecompensa, valorDoDesconto, validarCupom,
} from '../../dominio/fidelidade'
import { totalDoPedido } from '../../dominio/pedido'

const mapear = (estado, id, transformar) => ({
  ...estado,
  conversas: estado.conversas.map((c) => (c.id === id ? transformar(c) : c)),
})

export const casosFidelidade = {
  // ---------------------------------------------------------------------
  // Cupons (Gestão · aba Fidelidade e cupons).
  // ---------------------------------------------------------------------
  [acao.CRIAR_CUPOM]: (estado, { dados, cupomId }) => ({
    ...estado,
    fidelidade: {
      ...estado.fidelidade,
      cupons: [...estado.fidelidade.cupons, novoCupom(cupomId, dados)],
    },
  }),

  [acao.EDITAR_CUPOM]: (estado, { id, dados }) => ({
    ...estado,
    fidelidade: {
      ...estado.fidelidade,
      cupons: estado.fidelidade.cupons.map((c) => (c.id === id ? { ...c, ...dados } : c)),
    },
  }),

  [acao.ALTERNAR_CUPOM_ATIVO]: (estado, { id }) => ({
    ...estado,
    fidelidade: {
      ...estado.fidelidade,
      cupons: estado.fidelidade.cupons.map((c) => (c.id === id ? { ...c, ativo: !c.ativo } : c)),
    },
  }),

  // ---------------------------------------------------------------------
  // Cupom aplicado NO PEDIDO (ficha/Cobrança). `Venda.ValorDesconto` do mapa
  // EasyStok: o desconto fica gravado no próprio pedido, não só na tela, para
  // o caixa (quando existir) somar depois.
  // ---------------------------------------------------------------------
  [acao.APLICAR_CUPOM]: (estado, { id, codigo, agora }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const pedido = conversa?.pedido
    if (!pedido || pedido.cobranca) return estado
    const cupom = estado.fidelidade.cupons.find(
      (c) => c.codigo === (codigo ?? '').trim().toUpperCase(),
    )
    const totalPedido = totalDoPedido(pedido, estado.catalogo.cardapio)
    const { valido } = validarCupom(cupom, { agora, totalPedido })
    if (!valido) return estado
    const valorDesconto = valorDoDesconto(cupom, totalPedido)
    return {
      ...mapear(estado, id, (c) => ({
        ...c, pedido: { ...c.pedido, cupom: { id: cupom.id, codigo: cupom.codigo }, valorDesconto },
      })),
      fidelidade: {
        ...estado.fidelidade,
        cupons: estado.fidelidade.cupons.map((cp) => (cp.id === cupom.id ? { ...cp, usos: cp.usos + 1 } : cp)),
      },
    }
  },

  [acao.REMOVER_CUPOM_DO_PEDIDO]: (estado, { id }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const cupomAplicado = conversa?.pedido?.cupom
    if (!cupomAplicado) return estado
    return {
      ...mapear(estado, id, (c) => ({
        ...c, pedido: { ...c.pedido, cupom: null, valorDesconto: 0 },
      })),
      fidelidade: {
        ...estado.fidelidade,
        cupons: estado.fidelidade.cupons.map((cp) => (cp.id === cupomAplicado.id
          ? { ...cp, usos: Math.max(0, cp.usos - 1) }
          : cp)),
      },
    }
  },

  // ---------------------------------------------------------------------
  // Regra de fidelidade: proposta padrão ajustável (ver dominio/fidelidade.js,
  // REGRA_FIDELIDADE_PADRAO). Edita só os campos que vieram em `dados`, para
  // trocar de tipo ('valor'/'pedidos') sem perder os números do outro tipo.
  // ---------------------------------------------------------------------
  [acao.EDITAR_REGRA_FIDELIDADE]: (estado, { dados }) => ({
    ...estado,
    fidelidade: { ...estado.fidelidade, config: { ...estado.fidelidade.config, ...dados } },
  }),

  // ---------------------------------------------------------------------
  // Catálogo de recompensas.
  // ---------------------------------------------------------------------
  [acao.CRIAR_RECOMPENSA]: (estado, { dados, recompensaId }) => ({
    ...estado,
    fidelidade: {
      ...estado.fidelidade,
      recompensas: [...estado.fidelidade.recompensas, novaRecompensa(recompensaId, dados)],
    },
  }),

  [acao.EDITAR_RECOMPENSA]: (estado, { id, dados }) => ({
    ...estado,
    fidelidade: {
      ...estado.fidelidade,
      recompensas: estado.fidelidade.recompensas.map((r) => (r.id === id ? { ...r, ...dados } : r)),
    },
  }),

  [acao.ALTERNAR_RECOMPENSA_ATIVA]: (estado, { id }) => ({
    ...estado,
    fidelidade: {
      ...estado.fidelidade,
      recompensas: estado.fidelidade.recompensas.map((r) => (r.id === id ? { ...r, ativo: !r.ativo } : r)),
    },
  }),

  // ---------------------------------------------------------------------
  // Sorteios.
  // ---------------------------------------------------------------------
  [acao.CRIAR_SORTEIO]: (estado, { dados, sorteioId }) => ({
    ...estado,
    fidelidade: {
      ...estado.fidelidade,
      sorteios: [...estado.fidelidade.sorteios, novoSorteio(sorteioId, dados)],
    },
  }),

  // ---------------------------------------------------------------------
  // Resgate: gasta ponto por uma recompensa do catálogo. `saldoDisponivel`
  // chega já calculado de fora (a tela tem o `historico` do cliente, que o
  // reducer não tem — mesmo padrão de `emissao` em GERAR_COBRANCA: valor
  // efeito/derivado é calculado fora e chega pronto no despacho). O reducer
  // só faz a guarda final: sem saldo, sem recompensa ativa ou sorteio
  // inexistente, o resgate não acontece.
  // ---------------------------------------------------------------------
  [acao.RESGATAR_RECOMPENSA]: (estado, {
    cadastroId, nomeCliente, recompensaId, saldoDisponivel, agora, resgateId,
  }) => {
    const recompensa = estado.fidelidade.recompensas.find((r) => r.id === recompensaId)
    if (!recompensa?.ativo || saldoDisponivel < recompensa.custoPontos) return estado
    const resgate = {
      id: resgateId, recompensaId, custoPontos: recompensa.custoPontos, em: agora,
    }
    const resgatesDoCadastro = [
      ...(estado.fidelidade.resgatesPorCadastro[cadastroId] ?? []),
      resgate,
    ]
    const sorteios = recompensa.tipo === 'sorteio'
      ? estado.fidelidade.sorteios.map((s) => (s.id === recompensa.sorteioId
        ? comParticipante(s, { cadastroId, nome: nomeCliente, em: agora })
        : s))
      : estado.fidelidade.sorteios
    return {
      ...estado,
      fidelidade: {
        ...estado.fidelidade,
        sorteios,
        resgatesPorCadastro: {
          ...estado.fidelidade.resgatesPorCadastro,
          [cadastroId]: resgatesDoCadastro,
        },
      },
    }
  },
}
