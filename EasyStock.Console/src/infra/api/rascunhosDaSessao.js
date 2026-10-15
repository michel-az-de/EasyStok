// Rascunhos por conversa guardados no sessionStorage (F07, item 6). O JWT vence em 8 h e o
// 401 desmonta a tela; o que a dona estava escrevendo volta depois do novo login.
// A chave separa empresa e usuário: rascunho de uma conta nunca aparece na outra. Some com
// a aba (sessionStorage) e no "Sair".
const PREFIXO = 'easystok.rascunhos'

const chaveDe = (sessao) => `${PREFIXO}:${sessao?.empresa?.id ?? '-'}:${sessao?.usuario?.id ?? '-'}`

export function lerRascunhos(sessao) {
  try {
    const salvo = JSON.parse(sessionStorage.getItem(chaveDe(sessao)) ?? '{}')
    return salvo && typeof salvo === 'object' && !Array.isArray(salvo) ? salvo : {}
  } catch {
    return {}
  }
}

export function gravarRascunhos(sessao, rascunhos) {
  try {
    const comTexto = Object.entries(rascunhos ?? {}).filter(([, texto]) => typeof texto === 'string' && texto.trim())
    if (comTexto.length === 0) sessionStorage.removeItem(chaveDe(sessao))
    else sessionStorage.setItem(chaveDe(sessao), JSON.stringify(Object.fromEntries(comTexto)))
  } catch { /* sem storage: o rascunho vale só nesta carga */ }
}

export function esquecerRascunhos() {
  try {
    for (let i = sessionStorage.length - 1; i >= 0; i -= 1) {
      const chave = sessionStorage.key(i)
      if (chave?.startsWith(PREFIXO)) sessionStorage.removeItem(chave)
    }
  } catch { /* nada a limpar */ }
}
