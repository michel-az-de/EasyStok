// Regras do texto que a atendente está escrevendo (#2185). Puro.

// Quem põe texto no campo "de fora" (resposta pronta, sugestão do Assistente, rascunho da IA) soma ao que
// já estava escrito, com um espaço, em vez de apagar. O botão Respostas já fazia assim.
export function juntarAoRascunho(atual, texto) {
  const antes = (atual ?? '').trimEnd()
  return antes ? `${antes} ${texto}` : texto
}

// Depois de enviar `enviado`, o que sobra no campo: o que ela digitou enquanto o envio esperava (assumir a
// conversa vai ao servidor) fica. Se mexeu no começo do texto enviado, nada é descartado.
export function restoDoEnvio(atual, enviado) {
  const campo = atual ?? ''
  if (campo === enviado) return ''
  return campo.startsWith(enviado) ? campo.slice(enviado.length).trimStart() : campo
}
