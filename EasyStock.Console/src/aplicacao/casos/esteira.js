import { contextoDePrevia, GATILHOS, regraDoGatilho, textoDaRegra } from '../../dominio/automacao'
import { estaBloqueada } from '../../dominio/conversa'
import { faixaParaCliente, janelaPorId } from '../../dominio/entrega'
import { avisoDoPasso, motivoParaNaoAvancar, passoAnterior } from '../../dominio/esteira'
import { pedidoEncerrado } from '../../dominio/pedido'
import { entregadorResolvido } from '../../dominio/viagem'
import * as acao from '../acoes'
import { comMensagem, mapear } from '../reducer/auxiliares'
import { ATENDENTE } from '../reducer/estadoInicial'

// SÓ DA DEMONSTRAÇÃO (carregado por `src/demonstracao/`, fora do build de produção): a esteira do pedido e as
// ações que, no modo API, o EasyStok faz (`aplicacao/api/comanda.js`, `acoesApi.js`).

export const casosEsteira = {
  [acao.CONFIRMAR_ENTREGA]: (estado, { id, mensagemId }) =>
    mapear(estado, id, (c) => ({
      ...c,
      mensagens: c.mensagens.map((m) => (m.id === mensagemId ? { ...m, status: 'lida' } : m)),
    })),

  // Assumir e devolver ao automático moram em ASSUMIR_ATENDIMENTO e
  // DEVOLVER_AUTOMATICO, lá embaixo. As versões antigas destes dois nomes
  // saíram: nenhuma tela despachava, e caso sem consumidor diverge calado.
  // Integração 48: `reabertaNaMao` segura o encerramento por janela (46). Sem
  // ela, conversa de janela vencida reabria e fechava de novo no mesmo tique,
  // e "Reabrir conversa" não fazia nada na tela.
  [acao.REABRIR_CONVERSA]: (estado, { id }) =>
    mapear(estado, id, (c) => ({
      ...c, estado: 'Em atendimento', responsavel: c.responsavel ?? ATENDENTE, reabertaNaMao: true,
    })),

  // O cliente só é avisado quando a dona marca. O sistema não adivinha.
  [acao.AVANCAR_ESTEIRA]: (estado, {
    id, passo, agora, mensagemId, posEntregaId, entregador: entregadorEscolhido,
  }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    // Pedido cancelado não anda na esteira (dominio/pedido.js, pedidoEncerrado):
    // sem esta trava, avançar um passo nele ressuscitava o pedido calado.
    if (!conversa?.pedido || pedidoEncerrado(conversa.pedido)) return estado
    // RN-14 / US-019: com o cadastro bloqueado o pedido não anda para preparo,
    // embalado nem entrega, venha o toque da ficha, da cozinha ou de Entregas.
    if (motivoParaNaoAvancar(passo, { bloqueado: estaBloqueada(conversa) })) return estado
    const janela = janelaPorId(estado.catalogo.janelas, conversa.pedido.janela)
    // US-040: por ficha, cozinha ou o cartão solto de Entregas, "quem leva"
    // é o que a dona acabou de escolher no despacho (`entregadorEscolhido`,
    // `componentes/EscolhaDeEntregador.jsx`) ou o que já está resolvido
    // (viagem com chamado achado, ou já escolhido antes). Ponto único de
    // leitura, mesmo `entregadorResolvido` que `casos/entregas.js` usa.
    const entregador = passo === 'entrega'
      ? (entregadorEscolhido ?? entregadorResolvido(conversa.pedido))
      : undefined
    // RN-06 e RN-22 (US-028): respiro antes de virar texto, ponto único em
    // dominio/entrega.js. Vale sobretudo para o aviso de "em preparo". Rodada
    // 13 (issue #42): respiro passou a ser ajustável na tela de Janelas
    // (`estado.catalogo.respiroMinutos`), sem nunca descer do chão do RN-22.
    const aviso = avisoDoPasso(passo, {
      faixa: janela?.faixa
        ? faixaParaCliente(janela.faixa, agora, estado.catalogo.respiroMinutos)
        : janela?.faixa,
      entregador,
    })
    // Pós-entrega: mesmo padrão de PAGAMENTO_CONFIRMADO (regraDoGatilho +
    // textoDaRegra + contextoDePrevia), disparado quando o passo marcado é
    // "Entregue". É a automática "Agradecimento e avaliação" da tela virando
    // verdade: hoje ela só saía pelo botão de teste do modal (QA3-17).
    // Só dispara uma vez por pedido: CORRIGIR_PASSO volta o passo sem apagar
    // o agradecimento já lido pelo cliente, então marcar Entregue de novo não
    // pode mandar um segundo (achado da auditoria cruzada QA5).
    const agradecimento = passo === 'entregue' && !conversa.pedido.agradecimentoEnviado
      ? regraDoGatilho(estado.regras, GATILHOS.POS_ENTREGA)
      : null
    // O agradecimento automático já avisa que chegou e agradece: mandar o
    // aviso genérico do passo "Entregue" (que também tem "obrigada" escrito)
    // saía como um segundo agradecimento colado no primeiro (achado do
    // arquiteto, 23/09/2026; a guarda de não duplicar cobria remarcar, não
    // esta dobra na mesma marcação). Com o agradecimento ativo, o aviso vira
    // só o fato; sem ele (regra desligada ou já enviado), o aviso completo
    // continua sendo a única mensagem.
    const textoDoAviso = agradecimento && aviso ? 'Pedido entregue.' : aviso
    const proximo = mapear(estado, id, (c) => {
      const comPasso = {
        ...c,
        pedido: {
          ...c.pedido,
          estado: passo,
          ...(entregador !== undefined ? { entregador } : {}),
          ...(agradecimento ? { agradecimentoEnviado: true } : {}),
        },
      }
      const comAviso = aviso
        ? comMensagem(
          comPasso,
          // Item D (banca 10): "esteira" sozinho não dizia QUAL passo mandou a
          // mensagem; com o passo no valor, a etiqueta "automática" do balão
          // (Balao.jsx) sabe abrir a definição certa (dominio/respostas.js,
          // itemDaBibliotecaPelaRegra) em vez de nenhuma.
          { dir: 'out', texto: textoDoAviso, status: 'lida', automatica: true, regra: `esteira-${passo}` },
          { id: mensagemId, agora },
        )
        : comPasso
      if (!agradecimento) return comAviso
      return comMensagem(
        comAviso,
        {
          dir: 'out', status: 'lida', automatica: true, regra: agradecimento.id,
          texto: textoDaRegra(agradecimento, contextoDePrevia(c, janela?.faixa)),
        },
        { id: posEntregaId, agora },
      )
    })
    return {
      ...proximo,
      ultimoAvanco: {
        conversaId: id, mensagemId, em: agora, posEntregaId: agradecimento ? posEntregaId : null,
      },
    }
  },

  // Desfazer de verdade: volta o passo e apaga a mensagem que a casa mandou.
  // Sem isto, marcar por engano é irreversível e o cliente já leu. Quando o
  // passo desfeito é "Entregue", o agradecimento automático some junto: senão
  // sobra um "obrigada pela preferência" sem entrega nenhuma por trás.
  [acao.DESFAZER_ESTEIRA]: (estado, { id, mensagemId, posEntregaId }) =>
    mapear(estado, id, (c) => {
      if (!c.pedido) return c
      const anterior = passoAnterior(c.pedido.estado)
      if (!anterior) return c
      return {
        ...c,
        pedido: {
          ...c.pedido,
          estado: anterior,
          // Aqui a mensagem some de verdade (apagada duas linhas abaixo), diferente
          // de CORRIGIR_PASSO: então a marca de agradecimento também some, senão o
          // próximo "Entregue" de verdade fica mudo.
          ...(c.pedido.estado === 'entregue' ? { agradecimentoEnviado: false } : {}),
        },
        mensagens: c.mensagens.filter((m) => m.id !== mensagemId && m.id !== posEntregaId),
      }
    }),

  // Correção deliberada, fora da janela de 10s do desfazer: ela percebe o
  // erro depois, com a mensagem automática já lida pelo cliente. Só volta o
  // passo, nunca apaga mensagem, diferente do DESFAZER_ESTEIRA (P1, QA2-13).
  [acao.CORRIGIR_PASSO]: (estado, { id }) =>
    mapear(estado, id, (c) => {
      if (!c.pedido) return c
      const anterior = passoAnterior(c.pedido.estado)
      if (!anterior) return c
      return { ...c, pedido: { ...c.pedido, estado: anterior } }
    }),

  [acao.TROCAR_JANELA]: (estado, { id, janela }) =>
    mapear(estado, id, (c) => (c.pedido ? { ...c, pedido: { ...c.pedido, janela } } : c)),

  // P0 (banca3/e1-e3, achado 1): o telefone é o próprio identificador do
  // canal no WhatsApp. Sem copiar `telefoneCanal` aqui, o lead virava cliente
  // sem telefone e RN-02/US-002 (saudar pelo nome na próxima) nunca disparava.
  //
  // RN-03/UC-01 passo 10 (rodada 10, item P1.4): confirmar endereço grava o
  // cadastro, mas NÃO promove a lead a cliente. "Lead é quem ainda não
  // comprou" (RN-03); quem promove é a primeira compra paga, em
  // `comPagamentoReconhecido` mais abaixo.
  [acao.CADASTRAR_ENDERECO]: (estado, { id, agora, mensagemId }) =>
    mapear(estado, id, (c) => comMensagem({
      ...c,
      cliente: {
        ...c.cliente,
        endereco: c.cliente.enderecoCapturado,
        enderecoCapturado: null,
        telefone: c.cliente.telefone || (c.canal === 'WhatsApp' ? c.cliente.telefoneCanal : null) || null,
      },
    }, {
      dir: 'sistema',
      texto: 'Cadastro criado a partir do endereço confirmado na conversa.',
    }, { id: mensagemId, agora })),
}
