// #3005: a dona dispensa a faixa de som e notificações bloqueados e ela não volta a cada recarga. Dispensa por
// aparelho, no localStorage: guarda a chave dos avisos que estavam de pé (ex.: "som|notificacao"). A faixa só
// reaparece se o conjunto mudar. Leitura e escrita em try/catch (modo privado, cota cheia): sem armazenamento a
// dispensa vale só na visita e a tela segue de pé. Mesmo desenho de `preferenciaDoComplemento.js`.
const CHAVE = 'easystok.avisoDoAparelho.v1'
let daVisita = ''

export function lerDispensa(armazenamento) {
  try {
    const guardado = (armazenamento ?? globalThis.localStorage).getItem(CHAVE)
    return typeof guardado === 'string' ? guardado : daVisita
  } catch { return daVisita }
}

export function gravarDispensa(chave, armazenamento) {
  daVisita = chave
  try {
    const alvo = armazenamento ?? globalThis.localStorage
    if (chave) alvo.setItem(CHAVE, chave); else alvo.removeItem(CHAVE)
  } catch { /* sem armazenamento: vale só na visita */ }
}
