// Mensagem programada ao cliente (#1424, S39). Regras puras da modal "Programar":
// o horário vem do `<input type="datetime-local">` (hora local do navegador, sem fuso)
// e vai à API em ISO UTC; a finalidade e a situação seguem os enums da API.

const MS_POR_MINUTO = 60000
const dois = (n) => String(n).padStart(2, '0')

// Valor do datetime-local `minutos` à frente de agora, arredondado para o minuto (hora local).
export function horarioLocalPadrao(agoraMs, minutos = 60) {
  const d = new Date(Math.ceil((agoraMs + minutos * MS_POR_MINUTO) / MS_POR_MINUTO) * MS_POR_MINUTO)
  return `${d.getFullYear()}-${dois(d.getMonth() + 1)}-${dois(d.getDate())}T${dois(d.getHours())}:${dois(d.getMinutes())}`
}

// "2026-10-07T15:30" (local) -> "2026-10-07T18:30:00.000Z". Valor vazio ou inválido -> null.
export function horarioLocalParaUtc(valorLocal) {
  if (!valorLocal) return null
  const ms = new Date(valorLocal).getTime()
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
  if (!agendadaPara) return 'Escolha a data e a hora do envio.'
  if (new Date(agendadaPara).getTime() <= agoraMs) return 'Escolha um horário depois de agora.'
  if (usarModelo ? !nomeModelo?.trim() : !texto?.trim()) {
    return usarModelo ? 'Informe o nome do modelo aprovado.' : 'Escreva a mensagem.'
  }
  return null
}
