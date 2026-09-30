// Sessão do console no sessionStorage: some ao fechar a aba, não vaza para outra
// pessoa que abrir o navegador depois. Sem refresh nesta fatia (F01): o refresh
// da API recalcula a empresa e perde a escolha de quem tem mais de uma.
const CHAVE = 'easystok.sessao'

export function lerSessao() {
  try {
    const bruta = sessionStorage.getItem(CHAVE)
    if (!bruta) return null
    const sessao = JSON.parse(bruta)
    return sessao.expiraEm > Date.now() ? sessao : null
  } catch {
    return null
  }
}

export function gravarSessao(sessao) {
  try { sessionStorage.setItem(CHAVE, JSON.stringify(sessao)) } catch { /* sem storage: vale só nesta carga */ }
}

export function limparSessao() {
  try { sessionStorage.removeItem(CHAVE) } catch { /* nada a limpar */ }
}
