import { Icone } from '../../componentes/Icone'
import css from './cozinha.module.css'

export function FeedbackArrasto({ feedback, erro = false }) {
  const aguardando = feedback?.destino && !feedback.confirmado
  const texto = aguardando && (erro || feedback.requerConferencia)
    ? 'Mudança ainda não confirmada. Confira o estado do pedido.'
    : feedback?.texto ?? 'Arraste pela alça para o próximo passo ou use o botão do pedido.'
  return (
    <output className={css.feedbackArrasto} aria-live="polite" aria-atomic="true">
      <Icone nome={feedback?.confirmado ? 'check' : 'grip-vertical'} tamanho={16} />
      <span>{texto}</span>
    </output>
  )
}
