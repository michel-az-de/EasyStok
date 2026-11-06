import { resumoDoDia } from '../../dominio/estadoDoHall'
import css from './hall.module.css'

// #2232 (M3): "Hoje · N em andamento · N na fila" saiu do menu do Atendimento e mora no Início.
// Sem leitura (ou com ela falhada) o resumo não aparece: nunca mostra número chutado.
// #2258 (H1): `ids` são os cartões de número que estão na tela; o resumo soma só esses.
export function ResumoDoDia({ fontes, ids }) {
  const { texto } = resumoDoDia(fontes, ids)
  return texto ? <p className={css.resumoDoDia}>{texto}</p> : null
}
