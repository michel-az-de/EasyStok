// Casos de reducer da Frente 6 · Entregas de hoje (rodada 5, seção 6).
// Arquivo da F6: nenhuma outra frente edita este arquivo, e a F6 nunca edita
// `reducer.js` (ele já importa e espalha `casosEntregas` no objeto composto).
//
// A viagem não ganhou uma coleção nova no estado (isso pediria mexer em
// `estadoInicial`, que é do passo zero, em `reducer.js`): ela mora
// denormalizada em `pedido.viagem = { id, modo, ordem, chamado }` dentro de
// cada conversa que é parada dela. `dominio/viagem.js` sabe juntar isso de
// volta em "uma viagem com N paradas" para a tela ler.
import * as acao from '../acoes'
import { avisoDoPasso, indiceDoPasso, motivoParaNaoAvancar } from '../../dominio/esteira'
import { estaBloqueada } from '../../dominio/conversa'
import { entregadorResolvido, viagensDeHoje } from '../../dominio/viagem'
import { GATILHOS, contextoDePrevia, regraDoGatilho, textoDaRegra } from '../../dominio/automacao'
import { janelaPorId } from '../../dominio/entrega'

// Mesmo padrão de `reducer.js` (que não exporta o dele): mapear uma conversa
// por id, e marcar mensagem do sistema/automática nela.
const mapear = (estado, id, transformar) => ({
  ...estado,
  conversas: estado.conversas.map((c) => (c.id === id ? transformar(c) : c)),
})

const mapearVarias = (estado, ids, transformar) => {
  const alvo = new Set(ids)
  return {
    ...estado,
    conversas: estado.conversas.map((c) => (alvo.has(c.id) ? transformar(c) : c)),
  }
}

function comMensagem(conversa, mensagem, { id, agora }) {
  return {
    ...conversa,
    ultimaEm: new Date(agora).toISOString(),
    mensagens: [...conversa.mensagens, { id, em: new Date(agora).toISOString(), ...mensagem }],
  }
}

const grupoDaViagem = (estado, viagemId) => viagensDeHoje(estado.conversas).find((v) => v.id === viagemId)

export const casosEntregas = {
  [acao.CRIAR_VIAGEM]: (estado, { id, viagemId, modo }) =>
    mapear(estado, id, (c) => (c.pedido ? {
      ...c, pedido: { ...c.pedido, viagem: { id: viagemId, modo, ordem: 0, chamado: null } },
    } : c)),

  [acao.DESFAZER_VIAGEM]: (estado, { viagemId }) => {
    const grupo = grupoDaViagem(estado, viagemId)
    if (!grupo) return estado
    return mapearVarias(estado, grupo.paradas.map((c) => c.id), (c) => ({
      ...c, pedido: { ...c.pedido, viagem: null },
    }))
  },

  [acao.TROCAR_MODO_VIAGEM]: (estado, { viagemId, modo }) => {
    const grupo = grupoDaViagem(estado, viagemId)
    if (!grupo) return estado
    return mapearVarias(estado, grupo.paradas.map((c) => c.id), (c) => ({
      ...c, pedido: { ...c.pedido, viagem: { ...c.pedido.viagem, modo } },
    }))
  },

  [acao.POR_NA_VIAGEM]: (estado, { viagemId, id }) => {
    const grupo = grupoDaViagem(estado, viagemId)
    if (!grupo) return estado
    const proximaOrdem = grupo.paradas.length
    return mapear(estado, id, (c) => (c.pedido ? {
      ...c,
      pedido: {
        ...c.pedido,
        viagem: { id: viagemId, modo: grupo.modo, ordem: proximaOrdem, chamado: grupo.chamado },
      },
    } : c))
  },

  [acao.TIRAR_DA_VIAGEM]: (estado, { id }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const viagemId = conversa?.pedido?.viagem?.id
    if (!viagemId) return estado
    const restante = grupoDaViagem(estado, viagemId).paradas.filter((c) => c.id !== id)
    const semParada = mapear(estado, id, (c) => ({ ...c, pedido: { ...c.pedido, viagem: null } }))
    // Reindexa a `ordem` de quem ficou, para não abrir buraco na sequência.
    return restante.reduce(
      (acc, c, indice) => mapear(acc, c.id, (cc) => ({
        ...cc, pedido: { ...cc.pedido, viagem: { ...cc.pedido.viagem, ordem: indice } },
      })),
      semParada,
    )
  },

  [acao.REORDENAR_PARADA]: (estado, { viagemId, deIndice, paraIndice }) => {
    const grupo = grupoDaViagem(estado, viagemId)
    if (!grupo || deIndice === paraIndice) return estado
    const ordemDeIds = grupo.paradas.map((c) => c.id)
    const movido = ordemDeIds[deIndice]
    if (movido == null) return estado
    ordemDeIds.splice(deIndice, 1)
    ordemDeIds.splice(paraIndice, 0, movido)
    return ordemDeIds.reduce(
      (acc, id, indice) => mapear(acc, id, (c) => ({
        ...c, pedido: { ...c.pedido, viagem: { ...c.pedido.viagem, ordem: indice } },
      })),
      estado,
    )
  },

  // Gerar rota (seção 6) regrava a ordem da viagem com a sugerida; o
  // Desfazer da modal devolve a ordem anterior pela mesma ação. Id que não é
  // parada desta viagem é ignorado, e parada que não veio na lista vai para
  // o fim, sem abrir buraco na sequência.
  [acao.APLICAR_ORDEM_VIAGEM]: (estado, { viagemId, ids }) => {
    const grupo = grupoDaViagem(estado, viagemId)
    if (!grupo || !Array.isArray(ids)) return estado
    const daViagem = new Set(grupo.paradas.map((c) => c.id))
    const pedidos = ids.filter((id) => daViagem.has(id))
    const restantes = grupo.paradas.map((c) => c.id).filter((id) => !pedidos.includes(id))
    return [...pedidos, ...restantes].reduce(
      (acc, id, indice) => mapear(acc, id, (c) => ({
        ...c, pedido: { ...c.pedido, viagem: { ...c.pedido.viagem, ordem: indice } },
      })),
      estado,
    )
  },

  // Trocar a janela é sempre permitido; se a entrega estava numa viagem, ela
  // sai (a viagem foi montada em cima do horário antigo, e não recalculamos
  // rota aqui — sem âncora exata de "não cabe", decisão de protótipo).
  [acao.ALTERAR_AGENDAMENTO_ENTREGA]: (estado, {
    id, janela, avisar, texto, agora, mensagemId,
  }) => mapear(estado, id, (c) => {
    if (!c.pedido) return c
    const comJanela = { ...c, pedido: { ...c.pedido, janela, viagem: null } }
    if (!avisar) return comJanela
    return comMensagem(
      comJanela,
      { dir: 'out', texto, status: 'lida', automatica: true, regra: 'agendamento-entrega' },
      { id: mensagemId, agora },
    )
  }),

  [acao.CHAMAR_ENTREGADOR]: (estado, { viagemId, agora }) => {
    const grupo = grupoDaViagem(estado, viagemId)
    if (!grupo) return estado
    return mapearVarias(estado, grupo.paradas.map((c) => c.id), (c) => ({
      ...c,
      pedido: {
        ...c.pedido,
        viagem: {
          ...c.pedido.viagem,
          chamado: { status: 'procurando', iniciadoEm: agora, entregador: null },
        },
      },
    }))
  },

  [acao.CANCELAR_CHAMADO]: (estado, { viagemId }) => {
    const grupo = grupoDaViagem(estado, viagemId)
    if (!grupo) return estado
    return mapearVarias(estado, grupo.paradas.map((c) => c.id), (c) => ({
      ...c, pedido: { ...c.pedido, viagem: { ...c.pedido.viagem, chamado: null } },
    }))
  },

  // Passos simulados do chamado (Procurando → Achou → Entregador chegou).
  // Quem decide QUANDO chamar isto é a tela (setTimeout curto, seção 6, "por
  // 3 s"); aqui só grava o status. Marca no código de quem despacha
  // (`ConteudoEntregas.jsx`) o ponto onde a Lalamove real entraria (estudo
  // 18, E1: `cotar`/`chamar`/`cancelar`, e E4: `acompanhar`).
  [acao.ATUALIZAR_CHAMADO]: (estado, { viagemId, status, entregador }) => {
    const grupo = grupoDaViagem(estado, viagemId)
    if (!grupo) return estado
    return mapearVarias(estado, grupo.paradas.map((c) => c.id), (c) => ({
      ...c,
      pedido: {
        ...c.pedido,
        viagem: { ...c.pedido.viagem, chamado: { ...c.pedido.viagem.chamado, status, entregador } },
      },
    }))
  },

  // Despacha a viagem inteira: "Entregar ao entregador" (modo entregador,
  // depois que ele chegou) ou "Sair agora" (modo eu-levo). Cada parada avança
  // para "Em entrega" e leva a mensagem de saída (seção 6, "com a mensagem
  // de saída para cada cliente"); o texto é o mesmo da esteira normal
  // (`avisoDoPasso`), reaproveitado em vez de copiado.
  [acao.SAIR_PARA_ENTREGA]: (estado, { viagemId, agora, mensagensPorId }) => {
    const grupo = grupoDaViagem(estado, viagemId)
    if (!grupo) return estado
    return grupo.paradas.reduce((acc, conversa) => mapear(acc, conversa.id, (c) => {
      if (indiceDoPasso(c.pedido.estado) >= indiceDoPasso('entrega')) return c
      // RN-14 / US-019: parada de cliente bloqueado fica em casa, a viagem
      // sai com as outras (mesma regra de AVANCAR_ESTEIRA em reducer.js).
      if (motivoParaNaoAvancar('entrega', { bloqueado: estaBloqueada(c) })) return c
      // US-040: a viagem já sabe quem leva (chamado achado ou "eu levo");
      // grava no pedido para a mensagem usar o nome real, não o texto fixo.
      const entregador = entregadorResolvido(c.pedido)
      const comPasso = {
        ...c,
        pedido: {
          ...c.pedido,
          estado: 'entrega',
          entregador,
          viagem: c.pedido.viagem && { ...c.pedido.viagem, chamado: c.pedido.viagem.chamado && { ...c.pedido.viagem.chamado, status: 'em-rota' } },
        },
      }
      const aviso = avisoDoPasso('entrega', { entregador })
      if (!aviso) return comPasso
      return comMensagem(
        comPasso,
        // Rodada 10, item D: passo no valor de `regra`, mesma convenção de
        // aplicacao/reducer.js (AVANCAR_ESTEIRA), para a etiqueta "automática"
        // do balão abrir a definição certa.
        { dir: 'out', texto: aviso, status: 'lida', automatica: true, regra: 'esteira-entrega' },
        { id: mensagensPorId[c.id], agora },
      )
    }), estado)
  },

  // "Entregue" de uma parada só, dentro ou fora de viagem (seção 6, "Em
  // rota: cada parada vira Entregue... ela pode marcar antes").
  //
  // Integração da rodada 5: mesma regra do Entregue da esteira (reducer.js,
  // AVANCAR_ESTEIRA, defeito b do corte). Com "Agradecimento e avaliação"
  // ligada e ainda não enviada, o aviso vira só o fato e quem agradece é a
  // regra (RN-34, RN-37). Antes este caminho nunca mandava a regra.
  [acao.MARCAR_PARADA_ENTREGUE]: (estado, { id, agora, mensagemId }) => mapear(estado, id, (c) => {
    if (!c.pedido) return c
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
        { id: mensagemId, agora },
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
      { id: `${mensagemId}-agradecimento`, agora },
    )
  }),
}
