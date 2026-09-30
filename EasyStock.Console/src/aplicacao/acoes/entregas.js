// Criadores de ação da Frente 6 · Entregas de hoje (rodada 5, seção 6).
// `AtendimentoProvider.jsx` chama `criarAcoesEntregas(despachar)` e espalha
// o resultado no objeto `acoes` do contexto; a F6 só mexe aqui dentro, nunca
// no Provider. A janela de Entregas (`TelaEntregas.jsx`) chama a MESMA
// função com o `despachar` do espelho (`infra/canalEntreJanelas.js`) em vez
// do `dispatch` do reducer: como este arquivo só empacota `{ tipo, ... }` e
// entrega a quem chamou, as duas janelas ganham os mesmos criadores sem
// duplicar lógica.
//
// Id de viagem por `crypto.randomUUID()`, não por `infra/repositorioConversas`
// (`proximoId`): as duas janelas são dois documentos, cada uma com a própria
// cópia do contador de `proximoId`, e um contador que reinicia por janela
// colidiria ("viagem-1" nascendo duas vezes). UUID não colide entre janelas.
import * as acao from '../acoes'

const novoId = () => crypto.randomUUID()

export function criarAcoesEntregas(despachar) {
  return {
    criarViagem: (id, modo) => despachar({ tipo: acao.CRIAR_VIAGEM, id, viagemId: novoId(), modo }),
    desfazerViagem: (viagemId) => despachar({ tipo: acao.DESFAZER_VIAGEM, viagemId }),
    trocarModoViagem: (viagemId, modo) => despachar({ tipo: acao.TROCAR_MODO_VIAGEM, viagemId, modo }),
    porNaViagem: (viagemId, id) => despachar({ tipo: acao.POR_NA_VIAGEM, viagemId, id }),
    tirarDaViagem: (id) => despachar({ tipo: acao.TIRAR_DA_VIAGEM, id }),
    reordenarParada: (viagemId, deIndice, paraIndice) => despachar({
      tipo: acao.REORDENAR_PARADA, viagemId, deIndice, paraIndice,
    }),
    aplicarOrdemDaViagem: (viagemId, ids) => despachar({ tipo: acao.APLICAR_ORDEM_VIAGEM, viagemId, ids }),

    // `avisar` monta a mensagem com o texto pronto que a tela já mostrou na
    // prévia (seção 6, caixa "Avisar Vanessa"): o domínio não escreve copy.
    alterarAgendamentoEntrega: (id, janela, avisar, texto, agora) => despachar({
      tipo: acao.ALTERAR_AGENDAMENTO_ENTREGA, id, janela, avisar, texto, agora, mensagemId: novoId(),
    }),

    // Chamar entregador (simulado, estudo 18 § 2.6): aqui só abre o chamado
    // e grava "procurando". O avanço Procurando → Achou → Chegou é tique de
    // tela (setTimeout curto), que despacha `atualizarChamado` de novo — é
    // o ponto marcado no código onde a Lalamove real entraria
    // (`infra/provedoresDeEntrega.js: cotar/chamar`, ainda não escrito).
    chamarEntregador: (viagemId, agora) => despachar({ tipo: acao.CHAMAR_ENTREGADOR, viagemId, agora }),
    cancelarChamado: (viagemId) => despachar({ tipo: acao.CANCELAR_CHAMADO, viagemId }),
    atualizarChamado: (viagemId, status, entregador = null) => despachar({
      tipo: acao.ATUALIZAR_CHAMADO, viagemId, status, entregador,
    }),

    // Despacha a viagem (modo entregador ou eu-levo): uma mensagem de saída
    // por parada, texto pronto pela esteira (`avisoDoPasso`), id novo por
    // parada para não colidir na lista de mensagens de cada conversa.
    sairParaEntrega: (viagemId, paradaIds, agora) => despachar({
      tipo: acao.SAIR_PARA_ENTREGA,
      viagemId,
      agora,
      mensagensPorId: Object.fromEntries(paradaIds.map((id) => [id, novoId()])),
    }),

    marcarParadaEntregue: (id, agora) => despachar({
      tipo: acao.MARCAR_PARADA_ENTREGUE, id, agora, mensagemId: novoId(),
    }),
  }
}

// Texto da mensagem "sua entrega mudou de horário" (seção 6, caixa "Avisar
// Vanessa": "Sua entrega mudou para 18h30 às 19h30. Tudo certo?").
export const textoAvisoDeAgendamento = (faixa) => `Sua entrega mudou para ${faixa}. Tudo certo?`
