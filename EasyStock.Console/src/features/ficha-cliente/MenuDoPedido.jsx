import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Popover } from '../../componentes/Popover'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import css from './ficha.module.css'

// Menu de acoes do pedido (secao 5 da direcao visual): cancelar, estornar,
// voltar etapa e, desde a rodada 5, encerrar o atendimento. So o gatilho e o
// popover moram aqui; a confirmacao em linha de cancelar/estornar (nunca
// modal) mora em quem monta o bloco Pedido, porque ela substitui o botao
// primario no corpo do bloco, nao um item do menu. "Encerrar atendimento" é
// diferente: sempre presente, e abre a modal do resumo (rodada 5, seção 5)
// direto por `abrirEncerramento`, lendo a conversa selecionada do contexto
// para não precisar de uma prop nova vinda de `BlocoPedido.jsx` (F3).
export function MenuDoPedido({
  podeCancelar, podeEstornar, podeVoltarEtapa, podeDesfazerPagamento = false, podeRegistrarOcorrencia = false,
  aoEscolher,
}) {
  const [aberto, setAberto] = useState(false)
  const { selecionada } = useAtendimento()
  const { abrirEncerramento } = useAcoes()

  const fechar = () => setAberto(false)
  const escolher = (chave) => { fechar(); aoEscolher(chave) }
  const escolherEncerrar = () => { fechar(); if (selecionada) abrirEncerramento(selecionada.id) }

  return (
    <span className={css.comMenu}>
      <Botao
        variante="secundario"
        className={css.gatilhoMenu}
        aria-haspopup="menu"
        aria-expanded={aberto}
        aria-label="Mais ações do pedido"
        title="Mais ações do pedido"
        onClick={() => setAberto((v) => !v)}
      >
        <Icone nome="ellipsis" tamanho={24} />
      </Botao>
      {aberto && (
        <Popover rotulo="Mais ações do pedido" posicao="abaixo" aoFechar={fechar}>
          <div className={css.menuAcoes}>
            {podeCancelar && (
              <button type="button" role="menuitem" className={css.itemMenu} onClick={() => escolher('cancelar')}>
                <Icone nome="circle-x" />
                Cancelar pedido
              </button>
            )}
            {podeDesfazerPagamento && (
              // Rodada 12 (issue #13): baixa marcada por engano. Abre a
              // confirmação com motivo em BarraProximoPasso, como o estorno.
              <button type="button" role="menuitem" className={css.itemMenu} onClick={() => escolher('desfazer')}>
                <Icone nome="undo-2" />
                Desfazer pagamento
              </button>
            )}
            {podeEstornar && (
              // UC-06 passo 5: escolher isto abre o formulário de motivo (obrigatório)
              // e valor, em BarraProximoPasso, não um confirmar direto.
              <button type="button" role="menuitem" className={css.itemMenu} onClick={() => escolher('estornar')}>
                <Icone nome="undo-2" />
                Marcar estorno
              </button>
            )}
            {podeRegistrarOcorrencia && (
              // F09 (S27): só no modo API. Abre o formulário em BarraProximoPasso.
              <button type="button" role="menuitem" className={css.itemMenu} onClick={() => escolher('ocorrencia')}>
                <Icone nome="message-square-warning" />
                Registrar ocorrência
              </button>
            )}
            {podeVoltarEtapa && (
              <button type="button" role="menuitem" className={css.itemMenu} onClick={() => escolher('voltar')}>
                <Icone nome="arrow-left" />
                Voltar etapa
              </button>
            )}
            <button type="button" role="menuitem" className={css.itemMenu} onClick={escolherEncerrar}>
              <Icone nome="log-out" />
              Encerrar atendimento
            </button>
          </div>
        </Popover>
      )}
    </span>
  )
}
