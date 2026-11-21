// "Esconder o botão Simular" é preferência do aparelho (PainelSimulacoes grava, o trilho lê).
const CHAVE_SIMULAR_ESCONDIDO = 'casa-da-baba:simular-escondido'

export function simularEscondido() {
  try {
    return window.localStorage.getItem(CHAVE_SIMULAR_ESCONDIDO) === '1'
  } catch {
    return false
  }
}
