// #2232 (banca UX, L8): hora da última mensagem na lista de conversas, no formato de app de
// mensagem: "21:48" no dia da loja, "ontem" no dia anterior e "26 out" antes disso. Substitui a
// duração ("9 d", "23 h 50"), que obrigava a fazer conta. Puro; o dia é o da loja (FUSO).

import { diasDeCalendarioEntre, horaCurta, partesNoFuso } from './formato'

const MESES = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez']

export function horaDaLista(iso, agora) {
  const ms = new Date(iso).getTime()
  if (!Number.isFinite(ms)) return ''
  const dias = diasDeCalendarioEntre(ms, agora)
  if (dias <= 0) return horaCurta(ms)
  if (dias === 1) return 'ontem'
  const { dia, mes, ano } = partesNoFuso(ms)
  const anoDeAgora = partesNoFuso(agora).ano
  return `${dia} ${MESES[mes - 1]}${ano === anoDeAgora ? '' : ` ${ano}`}`
}
