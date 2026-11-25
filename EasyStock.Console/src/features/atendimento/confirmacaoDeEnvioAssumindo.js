// #3282 (crítica da tela de atendimento, P1): enviar com o Assistente atendendo assume a conversa. A primeira
// vez da sessão a atendente confirma; depois o "Enviar e assumir" basta. A escolha é de uma empresa e de um
// usuário, como a dispensa de #2285. Armazenamento bloqueado só faz a pergunta voltar.
const PREFIXO = 'easystok.confirmouEnviarAssumindo.v1'

export function chaveDaConfirmacao(sessao) {
  const empresa = sessao?.empresa?.id
  const usuario = sessao?.usuario?.id
  if (typeof empresa !== 'string' || !empresa || typeof usuario !== 'string' || !usuario) return null
  return `${PREFIXO}:${encodeURIComponent(empresa)}:${encodeURIComponent(usuario)}`
}

export function jaConfirmou(chave) {
  if (!chave) return false
  try { return globalThis.sessionStorage?.getItem(chave) === '1' } catch { return false }
}

export function gravarConfirmacao(chave) {
  if (!chave) return
  try { globalThis.sessionStorage?.setItem(chave, '1') } catch { /* sem armazenamento, pergunta de novo */ }
}
