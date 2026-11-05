import { resumoDoDia } from '../../dominio/estadoDoHall'
import css from './hall.module.css'

// #2232 (M3): "Hoje · N em andamento · N na fila" saiu do menu do Atendimento e mora no Início.
// Sem leitura (ou com ela falhada) o resumo não aparece: nunca mostra número chutado.
export function ResumoDoDia({ fontes }) {
  const { texto } = resumoDoDia(fontes)
  return texto ? <p className={css.resumoDoDia}>{texto}</p> : null
}
