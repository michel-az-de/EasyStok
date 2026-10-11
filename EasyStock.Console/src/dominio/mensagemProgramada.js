// Mensagem programada ao cliente (#1424, S39). Regras puras da modal "Programar":
// o horário vem do `<input type="datetime-local">` (sem fuso), é lido como hora DA LOJA
// (#1563: a lista mostra a hora da loja, e um aparelho em outro fuso não desloca o envio)
// e vai à API em ISO UTC; a finalidade e a situação seguem os enums da API.

import { instanteNoFuso, partesNoFuso } from './formato'
import { avisoDeVariaveisNaoResolvidas } from './respostas'

const MS_POR_MINUTO = 60000
const dois = (n) => String(n).padStart(2, '0')

// Valor do datetime-local `minutos` à frente de agora, arredondado para o minuto (hora da loja).
export function horarioLocalPadrao(agoraMs, minutos = 60) {
  const p = partesNoFuso(Math.ceil((agoraMs + minutos * MS_POR_MINUTO) / MS_POR_MINUTO) * MS_POR_MINUTO)
  return `${p.ano}-${dois(p.mes)}-${dois(p.dia)}T${dois(p.horas)}:${dois(p.minutos)}`
}

const DATETIME_LOCAL = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/

// "2026-10-07T15:30" (hora da loja) -> "2026-10-07T18:30:00.000Z", em qualquer aparelho.
// Valor vazio ou inválido -> null.
export function horarioLocalParaUtc(valorLocal) {
  const casado = DATETIME_LOCAL.exec(valorLocal ?? '')
  if (!casado) return null
  const [ano, mes, dia, horas, minutos] = casado.slice(1).map(Number)
  const ms = instanteNoFuso({ ano, mes, dia, horas, minutos })
  return Number.isNaN(ms) ? null : new Date(ms).toISOString()
}

export const FINALIDADES = [
  { valor: 'Transacional', rotulo: 'Atendimento (pedido, entrega, aviso)' },
  { valor: 'Marketing', rotulo: 'Divulgação (só com o consentimento do cliente)' },
]

// Situação da API -> rótulo e tom da pílula.
export const SITUACOES = {
  Agendada: { rotulo: 'agendada', tom: 'aviso' },
  Enviando: { rotulo: 'saindo agora', tom: 'aviso' },
  Enviada: { rotulo: 'enviada', tom: 'ok' },
  Cancelada: { rotulo: 'cancelada', tom: 'neutro' },
  Falhou: { rotulo: 'falhou', tom: 'perigo' },
}

export const podeCancelar = (programada) => programada.situacao === 'Agendada'

// Parâmetros do modelo, um por linha, na ordem {{1}}, {{2}}... do modelo aprovado na Meta.
export const parametrosDoTexto = (texto) =>
  (texto ?? '').split('\n').map((p) => p.trim()).filter(Boolean)

// O que falta para agendar (null quando está tudo certo). A API confere o resto
// (janela do canal no horário do envio, consentimento, telefone do cliente).
export function faltaParaProgramar({ agendadaPara, agoraMs, usarModelo, texto, nomeModelo }) {
  if (!agendadaPara || !Number.isFinite(new Date(agendadaPara).getTime())) return 'Escolha a data e a hora do envio.'
  if (new Date(agendadaPara).getTime() <= agoraMs) return 'Escolha um horário depois de agora.'
  if (usarModelo ? !nomeModelo?.trim() : !texto?.trim()) {
    return usarModelo ? 'Informe o nome do modelo aprovado.' : 'Escreva a mensagem.'
  }
  return usarModelo ? null : avisoDeVariaveisNaoResolvidas(texto)
}
