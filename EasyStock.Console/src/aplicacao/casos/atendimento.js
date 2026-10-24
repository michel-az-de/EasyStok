import { abrirOcorrencia } from '../../dominio/ocorrencia'
import * as acao from '../acoes'
import { comRascunhoDeFora, comRascunhoDoCampo } from '../rascunhoLocal'
import { comEscritaDaCasa, comMensagem, mapear, recusaEnvio } from '../reducer/auxiliares'
import { ATENDENTE } from '../reducer/estadoInicial'

// Casos de conversa que os dois modos despacham: selecionar, filtro, rascunho, envio, encerramento, mídia,
// alerta de estoque, agente, passagem para a dona, som e a modal de encerramento. O modo API usa estes
// mesmos casos (`aplicacao/acoesApi.js`, `api/*`): não são da demonstração.

export const casosAtendimento = {
  [acao.SELECIONAR_CONVERSA]: (estado, { id }) => ({
    ...estado,
    selecionadaId: id,
    selecaoPendente: estado.conversas.some((c) => c.id === id) ? null : { id, voltas: 0 },
    agente: estado.agente.conversaId === id ? estado.agente : { estado: 'ocioso', sugestao: null, conversaId: null },
  }),

  [acao.MUDAR_FILTRO]: (estado, { chave, valor }) => ({
    ...estado, filtros: { ...estado.filtros, [chave]: valor },
  }),

  // O rascunho vive no estado da conversa, não dentro do componente. Assim o
  // agente e as respostas rápidas escrevem nele sem truque de remontagem.
  // #1577: o campo de escrever manda o texto na pausa da digitação (`doCampo`); o resto é escrita de fora.
  [acao.DEFINIR_RASCUNHO]: (estado, { id, texto, doCampo = false, escritaVista, adotar = false }) =>
    (doCampo ? comRascunhoDoCampo(estado, { id, texto, escritaVista, adotar }) : comRascunhoDeFora(estado, id, texto)),

  [acao.ENVIAR_MENSAGEM]: (estado, { id, texto, agora, mensagemId, modelo = false, automatica = false, regra = null }) => {
    if (recusaEnvio(estado, id, agora)) return estado
    const base = mapear(estado, id, (c) => ({
      ...comMensagem(c, {
        dir: 'out', texto, automatica, modelo, regra,
        status: automatica ? 'lida' : 'enviando',
      }, { id: mensagemId, agora }),
      estado: c.estado === 'Aberto' ? 'Em atendimento' : c.estado,
      responsavel: c.responsavel ?? ATENDENTE,
    }))
    const comRascunhoLimpo = comRascunhoDeFora(base, id, '')
    if (automatica) return comRascunhoLimpo
    return {
      ...comRascunhoLimpo,
      automaticoPausado: { ...comRascunhoLimpo.automaticoPausado, [id]: true },
    }
  },

  // Fecha o ciclo depois de "Entregue" ou "Cancelado" (seção 2, ações de
  // pedido). Copy da confirmação em linha é da Frente A.
  [acao.ENCERRAR_ATENDIMENTO]: (estado, { id, confirmadaEm }) =>
    mapear(estado, id, (c) => ({ ...c, estado: 'Encerrado', ...(confirmadaEm != null ? { confirmadaEm } : null) })),

  // Figurinha, carta de cardápio, e (rodada 7, frente Anexos) arquivo, áudio
  // gravado e peça da galeria: mensagem como qualquer outra, com tipo próprio.
  // Saem na hora, sem rascunho, porque é resposta de mão ocupada. Pausam o
  // automático e gravam o dono igual ao texto: quem mandou foi ela, e a
  // conversa passou a ser dela. `...extra` é só espalhado (nunca lido aqui):
  // cada formato novo carrega os campos que o molde de `dominio/anexos.js`
  // decidiu (duracaoMs do áudio, nome/descrição da peça etc.), sem este case
  // precisar conhecer formato nenhum por nome.
  [acao.ENVIAR_MIDIA]: (estado, {
    id, formato, arte, texto, agora, mensagemId, ...extra
  }) => {
    if (recusaEnvio(estado, id, agora)) return estado
    return mapear(comEscritaDaCasa(estado, id), id, (c) => comMensagem(c, {
      dir: 'out', formato, arte, texto, status: 'lida', ...extra,
    }, { id: mensagemId, agora }))
  },

  // #1241: o "Entendi" de um alerta do EasyStok vale até recarregar a página; sem isso a
  // releitura do minuto seguinte traria o mesmo cartão de volta.
  [acao.FECHAR_ALERTA]: (estado, { alertaId }) => ({
    ...estado,
    alertasEstoque: estado.alertasEstoque.filter((a) => a.id !== alertaId),
    alertasFechados: [...(estado.alertasFechados ?? []), alertaId],
  }),

  [acao.ALERTAS_DE_ESTOQUE_DA_API]: (estado, { alertas }) => {
    const fechados = new Set(estado.alertasFechados ?? [])
    return {
      ...estado,
      alertasEstoque: [
        ...estado.alertasEstoque.filter((a) => !a.daApi),
        ...alertas.filter((a) => !fechados.has(a.id)),
      ],
    }
  },

  [acao.TROCAR_MODO_AGENTE]: (estado, { modo }) => ({ ...estado, modoAgente: modo }),

  [acao.AGENTE_PEDINDO]: (estado, { id }) => ({
    ...estado, agente: { estado: 'pensando', sugestao: null, conversaId: id },
  }),

  [acao.AGENTE_RESPONDEU]: (estado, { id, sugestao }) => ({
    ...estado, agente: { estado: 'pronto', sugestao, conversaId: id },
  }),

  [acao.AGENTE_LIMPOU]: (estado) => ({
    ...estado, agente: { estado: 'ocioso', sugestao: null, conversaId: null },
  }),

  // Transporte falhou (servidor local fora, sem credencial, modelo recusou).
  // A falha fica visível na tela em vez de deixar o painel em "Lendo" para sempre.
  [acao.AGENTE_FALHOU]: (estado, { id, erro }) => ({
    ...estado, agente: { estado: 'erro', sugestao: null, conversaId: id, erro },
  }),

  // US-049: abertura automática, disparada de `consultarAgente`
  // (AtendimentoProvider) quando o classificador reconhece reclamação de
  // produto entregue. Uma ocorrência aberta por pedido: uma segunda tentativa
  // de abrir não duplica.
  // `origem` é novo na rodada 10 (item 4, ocorrência de entrega): default
  // preserva quem já chamava isto sem saber do motivo novo (reclamação).
  [acao.ABRIR_OCORRENCIA]: (estado, { id, agora, ocorrenciaId, relatoCliente, origem }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido || conversa.pedido.ocorrencia) return estado
    const ocorrencia = abrirOcorrencia({
      id: ocorrenciaId, pedidoNumero: conversa.pedido.numero, relatoCliente, agora, ...(origem ? { origem } : {}),
    })
    return mapear(estado, id, (c) => ({ ...c, pedido: { ...c.pedido, ocorrencia } }))
  },

  // O agente devolveu a conversa (acao passar_para_dona). A marca fica NA
  // conversa, com o motivo, para o cartão do balcão contar a mesma história do
  // painel. Passagem ABERTA não é sobrescrita: o relógio da espera é o da
  // primeira devolução, senão o lembrete de 10 min nunca vence. Passagem já
  // ASSUMIDA é passado, e a devolução nova toma o lugar dela: senão o motivo
  // novo era descartado calado e o cartão contava a história errada.
  [acao.PASSAR_PARA_DONA]: (estado, { id, motivo, agora }) =>
    mapear(estado, id, (c) => (c.passagem && !c.passagem.assumida ? c : {
      ...c, passagem: { motivo, em: new Date(agora).toISOString(), assumida: false },
    })),

  [acao.ALTERNAR_SOM]: (estado, { ligado }) => ({ ...estado, som: ligado }),

  // Ponta (d) da integração: com `numero`, abre em leitura o resumo daquele
  // atendimento (modal Histórico, aba Atendimentos).
  [acao.ABRIR_ENCERRAMENTO]: (estado, { id, numero = null }) => ({
    ...estado, ui: { ...estado.ui, encerrando: id, encerrandoNumero: numero },
  }),

  [acao.FECHAR_ENCERRAMENTO]: (estado) => ({ ...estado, ui: { ...estado.ui, encerrando: null, encerrandoNumero: null } }),
}
