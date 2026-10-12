// Persistência da chave "Som da cozinha" (US-010). Mesmo padrão do tema em
// app/Moldura.jsx: uma chave no localStorage, lida na entrada e gravada a
// cada troca, com try/catch porque o navegador pode negar o storage.
const CHAVE = 'casa-da-baba:som-da-cozinha'

export function lerPreferenciaSom() {
  try {
    const salvo = window.localStorage.getItem(CHAVE)
    if (salvo === '0') return false
    if (salvo === '1') return true
  } catch {
    // sem storage, a chave começa ligada
  }
  return true
}

export function gravarPreferenciaSom(ligado) {
  try {
    window.localStorage.setItem(CHAVE, ligado ? '1' : '0')
  } catch {
    // sem persistência, a escolha vale só para esta sessão
  }
}
