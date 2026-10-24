import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoArea } from '../../../componentes/Campo'
import css from './abaCaixa.module.css'

// Prompt de estorno com motivo obrigatório, compartilhado entre lançamento e
// venda (aceite: "estorno em um toque... a partir do caixa e do pedido"; a
// versão "do pedido" já existe em `BlocoOcorrencia.jsx`/`PainelFicha.jsx`,
// reaproveitando a MESMA ação `marcarEstorno`). Mesmo padrão de campo e
// validação de `features/ficha-cliente/BlocoOcorrencia.jsx` ("Confirmar
// reembolso"), só que sem valor parcial: aqui é o toque único do caixa.
export function PromptEstorno({ rotulo, aoCancelar, aoConfirmar, enviando = false }) {
  const [motivo, setMotivo] = useState('')
  return (
    <div className={css.promptEstorno}>
      <p className={css.corpoBloco}>Estornar {rotulo}?</p>
      <CampoArea
        rotulo="Motivo do estorno"
        dica="Descreva o motivo com pelo menos 3 caracteres. Fica gravado no lançamento."
        rows={2}
        value={motivo}
        disabled={enviando}
        onChange={(e) => setMotivo(e.target.value)}
      />
      <p className={css.confirmarAcoes}>
        <Botao variante="texto" disabled={enviando} onClick={aoCancelar}>Manter lançamento</Botao>
        <Botao
          variante="primario"
          icone="undo-2"
          disabled={enviando || motivo.trim().length < 3}
          onClick={() => aoConfirmar(motivo.trim())}
        >
          {enviando ? 'Estornando…' : 'Confirmar estorno'}
        </Botao>
      </p>
    </div>
  )
}
