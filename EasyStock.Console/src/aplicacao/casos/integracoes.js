// Casos de reducer da Frente Entregas e integrações (rodada 13, issue #46,
// registro 108). Arquivo próprio, mesmo molde de `casos/cobranca.js`: nenhuma
// outra frente edita este arquivo, e ele nunca edita `casos/entregas.js`
// (dona da R12/issue #17) nem `reducer.js` além da linha mínima de registro.
//
// Duas famílias de caso:
//   1. Configuração dos provedores (`estado.catalogo.integracoesLogistica`),
//      que a aba de Gestão liga/desliga.
//   2. A corrida chamada por um provedor integrado, que mora no MESMO campo
//      `pedido.viagem.chamado` que a R12 já usa para "Entregador próprio"
//      (`dominio/viagem.js`), só que com `chamado.provedor` preenchido e o
//      vocabulário de status de `dominio/corrida.js`.
import * as acao from '../acoes'
import { alternarProvedor, definirPadrao, salvarCredencial } from '../../dominio/integracoes'
import { statusMoveParaEntrega, statusMoveParaEntregue } from '../../dominio/corrida'
import { avisoDoPasso, indiceDoPasso, motivoParaNaoAvancar } from '../../dominio/esteira'
import { estaBloqueada } from '../../dominio/conversa'
import { entregadorResolvido, viagensDeHoje } from '../../dominio/viagem'
import { GATILHOS, contextoDePrevia, regraDoGatilho, textoDaRegra } from '../../dominio/automacao'
import { janelaPorId } from '../../dominio/entrega'

const mapear = (estado, id, transformar) => ({
  ...estado,
  conversas: estado.conversas.map((c) => (c.id === id ? transformar(c) : c)),
})

function comMensagem(conversa, mensagem, { id, agora }) {
  return {
    ...conversa,
    ultimaEm: new Date(agora).toISOString(),
    mensagens: [...conversa.mensagens, { id, em: new Date(agora).toISOString(), ...mensagem }],
  }
}

const grupoDaViagem = (estado, viagemId) => viagensDeHoje(estado.conversas).find((v) => v.id === viagemId)

// --- Coletado: mesma transição de SAIR_PARA_ENTREGA (casos/entregas.js), sem
// duplicar a regra de negócio: bloqueio (RN-14/US-019) continua valendo, o
// texto do aviso é o mesmo `avisoDoPasso`, e quem já passou de "entrega" não
// volta. A diferença é só QUEM aciona: o status do provedor, não o toque dela
// (RN-32, D8, issue #46).
function marcarColetado(estado, grupo, { agora, mensagemId }) {
  return grupo.paradas.reduce((acc, conversa) => mapear(acc, conversa.id, (c) => {
    if (indiceDoPasso(c.pedido.estado) >= indiceDoPasso('entrega')) return c
    if (motivoParaNaoAvancar('entrega', { bloqueado: estaBloqueada(c) })) return c
    const entregador = entregadorResolvido(c.pedido)
    const comPasso = {
      ...c,
      pedido: { ...c.pedido, estado: 'entrega', entregador },
    }
    const aviso = avisoDoPasso('entrega', { entregador })
    if (!aviso) return comPasso
    return comMensagem(
      comPasso,
      { dir: 'out', texto: aviso, status: 'lida', automatica: true, regra: 'esteira-entrega' },
      { id: `${mensagemId}-${c.id}`, agora },
    )
  }), estado)
}

// --- Entregue automático: mesma transição de MARCAR_PARADA_ENTREGUE, aplicada
// a cada parada da corrida (UC-10, E1: "sem confirmação, marca na mão" só
// quando ela precisar corrigir depois; aqui quem confirmou foi o provedor).
function marcarEntregue(estado, grupo, { agora, mensagemId }) {
  return grupo.paradas.reduce((acc, conversa) => mapear(acc, conversa.id, (c) => {
    if (!c.pedido || c.pedido.estado === 'entregue' || c.pedido.estado === 'cancelado') return c
    const aviso = avisoDoPasso('entregue', {})
    const agradecimento = c.pedido.agradecimentoEnviado ? null : regraDoGatilho(estado.regras ?? [], GATILHOS.POS_ENTREGA)
    const comPasso = {
      ...c,
      pedido: { ...c.pedido, estado: 'entregue', ...(agradecimento ? { agradecimentoEnviado: true } : {}) },
    }
    const comAviso = aviso
      ? comMensagem(
        comPasso,
        {
          dir: 'out', texto: agradecimento ? 'Pedido entregue.' : aviso, status: 'lida', automatica: true,
          regra: 'esteira-entregue',
        },
        { id: `${mensagemId}-${conversa.id}`, agora },
      )
      : comPasso
    if (!agradecimento) return comAviso
    const faixa = janelaPorId(estado.catalogo?.janelas ?? [], c.pedido.janela)?.faixa
    return comMensagem(
      comAviso,
      {
        dir: 'out', status: 'lida', automatica: true, regra: agradecimento.id,
        texto: textoDaRegra(agradecimento, contextoDePrevia(c, faixa)),
      },
      { id: `${mensagemId}-agr-${conversa.id}`, agora },
    )
  }), estado)
}

export const casosIntegracoes = {
  // --- Configuração da aba (Gestão · Entregas e integrações) ---------------
  [acao.ALTERNAR_PROVEDOR_LOGISTICA]: (estado, { chave }) => ({
    ...estado,
    catalogo: {
      ...estado.catalogo,
      integracoesLogistica: alternarProvedor(estado.catalogo.integracoesLogistica, chave),
    },
  }),

  [acao.SALVAR_CREDENCIAL_PROVEDOR]: (estado, { chave, credencial }) => ({
    ...estado,
    catalogo: {
      ...estado.catalogo,
      integracoesLogistica: salvarCredencial(estado.catalogo.integracoesLogistica, chave, credencial),
    },
  }),

  [acao.DEFINIR_PROVEDOR_PADRAO]: (estado, { chave }) => ({
    ...estado,
    catalogo: {
      ...estado.catalogo,
      integracoesLogistica: definirPadrao(estado.catalogo.integracoesLogistica, chave),
    },
  }),

  // --- Despacho por provedor integrado (Lalamove, 99 Entregas) -------------
  // Cotação já veio pronta de `acoes/integracoes.js` (que chamou
  // `infra/provedoresDeEntrega.cotar`, fora do reducer, mesmo padrão de
  // `GERAR_PEDIDO`/emitirCobranca): o reducer só grava, nunca inventa preço.
  [acao.CHAMAR_ENTREGADOR_PROVEDOR]: (estado, {
    viagemId, agora, provedor, cotacao, codigo,
  }) => {
    const grupo = grupoDaViagem(estado, viagemId)
    if (!grupo) return estado
    return grupo.paradas.reduce((acc, c) => mapear(acc, c.id, (cc) => ({
      ...cc,
      pedido: {
        ...cc.pedido,
        viagem: {
          ...cc.pedido.viagem,
          chamado: {
            status: 'procurando', iniciadoEm: agora, provedor, cotacao, codigo, entregador: null,
          },
        },
      },
    })), estado)
  },

  // Um tique da corrida: status/código/entregador já vieram de
  // `infra/provedoresDeEntrega.consultarStatus` (chamado por
  // `acoes/integracoes.js`). Este caso só decide o EFEITO na esteira, que é
  // regra de negócio (RN-32/D8/UC-10) e por isso mora aqui, não na tela.
  [acao.AVANCAR_CORRIDA]: (estado, {
    viagemId, agora, status, codigo, entregador, mensagemId,
  }) => {
    const grupo = grupoDaViagem(estado, viagemId)
    if (!grupo?.chamado) return estado
    const chamadoAnterior = grupo.chamado
    // Falha (achado da homologação: "às vezes o app não acha ninguém" ou "o
    // entregador desiste antes de chegar"): fica visível como `falha` (a
    // tela mostra o motivo e o botão "Escolher outro provedor", que reusa a
    // mesma `CANCELAR_CHAMADO` genérica de `casos/entregas.js` para zerar o
    // chamado) em vez de sumir sozinha e deixar a dona sem entender por quê.
    const comStatus = grupo.paradas.reduce((acc, c) => mapear(acc, c.id, (cc) => ({
      ...cc,
      pedido: {
        ...cc.pedido,
        viagem: {
          ...cc.pedido.viagem,
          chamado: {
            ...chamadoAnterior,
            status,
            codigo: codigo ?? chamadoAnterior.codigo,
            entregador: entregador ?? chamadoAnterior.entregador,
          },
        },
      },
    })), estado)
    const grupoAtualizado = { ...grupo, chamado: { ...chamadoAnterior, status } }
    if (statusMoveParaEntrega(status)) return marcarColetado(comStatus, grupoAtualizado, { agora, mensagemId })
    if (statusMoveParaEntregue(status)) return marcarEntregue(comStatus, grupoAtualizado, { agora, mensagemId })
    return comStatus
  },
}
