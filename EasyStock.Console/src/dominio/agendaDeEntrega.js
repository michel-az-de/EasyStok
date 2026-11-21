// Agenda de encomendas (Entregas › Agenda, #2553, Q10 da auditoria QA de 13/11). Puro: a tela só exibe.
// Às 20:27 as janelas da manhã e da tarde de hoje apareciam como "0 de 3 vagas", sem dizer que já
// tinham passado, e a janela chamada "12h00 às 14h00" repetia o horário logo acima.

import { dataIsoNoFuso, partesNoFuso } from './formato'

const hhmm = (hora) => String(hora ?? '').slice(0, 5)

// O dia e a hora são os da loja: depois das 21 h o UTC já é amanhã.
export function janelaEncerrada({ data, fim }, agora) {
  const hoje = dataIsoNoFuso(agora)
  if (data !== hoje) return data < hoje
  const { horas, minutos } = partesNoFuso(agora)
  return `${String(horas).padStart(2, '0')}:${String(minutos).padStart(2, '0')}` >= hhmm(fim)
}

export const ocupacaoDaJanela = ({ ocupadas, capacidade }) =>
  `${ocupadas} ${ocupadas === 1 ? 'ocupada' : 'ocupadas'} de ${capacidade}`

// "12h00 às 14h00", "12h às 14h" e "12:00 - 14:00" sob "12:00–14:00" não dizem nada novo.
const HORARIO = /(\d{1,2})\s*(?:[:h]\s*(\d{2})?)/g
export function nomeDaJanela(nome, inicio, fim) {
  const texto = String(nome ?? '').trim()
  if (!texto) return null
  const horarios = [...texto.matchAll(HORARIO)].map(([, h, m]) => `${h.padStart(2, '0')}:${m ?? '00'}`)
  return horarios.length === 2 && horarios[0] === hhmm(inicio) && horarios[1] === hhmm(fim) ? null : texto
}
