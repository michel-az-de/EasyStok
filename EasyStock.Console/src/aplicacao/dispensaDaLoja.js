// #3156: "Fechar aviso" do aviso persistente da loja (desligada, cardápio sem ler) vale para o aparelho até o estado
// mudar. Guarda o texto do aviso fechado: texto novo aparece de novo, e o cardápio lido apaga a dispensa. Leitura e
// escrita em try/catch (modo privado, cota cheia): sem armazenamento a dispensa vale só na visita. Mesmo desenho de
// `dispensaDoAviso.js`.
const CHAVE = 'easystok.avisoDaLoja.v1'
let daVisita = ''

export function lerDispensaDaLoja(armazenamento) {
  try {
    const guardado = (armazenamento ?? globalThis.localStorage).getItem(CHAVE)
    return typeof guardado === 'string' ? guardado : daVisita
  } catch { return daVisita }
}

export function gravarDispensaDaLoja(texto, armazenamento) {
  daVisita = texto
  try {
    const alvo = armazenamento ?? globalThis.localStorage
    if (texto) alvo.setItem(CHAVE, texto); else alvo.removeItem(CHAVE)
  } catch { /* sem armazenamento: vale só na visita */ }
}
