import { MenuDeAcoes } from '../../componentes/MenuDeAcoes'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'

// Menu de ações do pedido (seção 5 da direção visual): cancelar, estornar, voltar etapa e, desde a
// rodada 5, concluir a conversa. A confirmação em linha de cancelar e estornar (nunca modal) mora em
// quem monta o bloco Pedido, porque ela substitui o botão primário no corpo do bloco. "Concluir
// conversa" abre a modal do resumo direto por `abrirEncerramento`, com a conversa selecionada.
export function MenuDoPedido({
  podeCancelar, podeEstornar, podeVoltarEtapa, podeDesfazerPagamento = false, aoEscolher,
}) {
  const { selecionada } = useAtendimento()
  const { abrirEncerramento } = useAcoes()
  return (
    <MenuDeAcoes
      rotulo="Mais opções do pedido"
      itens={[
        podeCancelar && { chave: 'cancelar', rotulo: 'Cancelar pedido', icone: 'circle-x', aoEscolher: () => aoEscolher('cancelar') },
        // Rodada 12 (issue #13): baixa marcada por engano. Abre a confirmação com motivo em BarraProximoPasso.
        podeDesfazerPagamento && { chave: 'desfazer', rotulo: 'Desfazer pagamento', icone: 'undo-2', aoEscolher: () => aoEscolher('desfazer') },
        // UC-06 passo 5: abre o formulário de motivo (obrigatório) e valor, não um confirmar direto.
        podeEstornar && { chave: 'estornar', rotulo: 'Marcar estorno', icone: 'undo-2', aoEscolher: () => aoEscolher('estornar') },
        podeVoltarEtapa && { chave: 'voltar', rotulo: 'Voltar etapa', icone: 'arrow-left', aoEscolher: () => aoEscolher('voltar') },
        selecionada && { chave: 'concluir', rotulo: 'Concluir conversa', icone: 'circle-check', abreJanela: true, aoEscolher: () => abrirEncerramento(selecionada.id) },
      ]}
    />
  )
}
