// #3156: "Fechar aviso" do aviso persistente da loja (desligada, cardápio sem ler) vale para o aparelho até o estado
// mudar. Guarda o texto do aviso fechado POR EMPRESA (a dispensa da empresa A não esconde o aviso da B no mesmo
// aparelho, como em `preferenciasHall.js`): texto novo aparece de novo, e o cardápio lido apaga a dispensa. Leitura e
// escrita em try/catch (modo privado, cota cheia): sem armazenamento a dispensa vale só na visita. Mesmo desenho de
// `dispensaDoAviso.js`.
const daVisita = new Map()
const chaveDa = (empresa) => `easystok.avisoDaLoja.v1:${encodeURIComponent(empresa ?? '')}`

export function lerDispensaDaLoja(empresa, armazenamento) {
  const chave = chaveDa(empresa)
  try {
    const guardado = (armazenamento ?? globalThis.localStorage).getItem(chave)
    return typeof guardado === 'string' ? guardado : daVisita.get(chave) ?? ''
  } catch { return daVisita.get(chave) ?? '' }
}

export function gravarDispensaDaLoja(empresa, texto, armazenamento) {
  const chave = chaveDa(empresa)
  daVisita.set(chave, texto)
  try {
    const alvo = armazenamento ?? globalThis.localStorage
    if (texto) alvo.setItem(chave, texto); else alvo.removeItem(chave)
  } catch { /* sem armazenamento: vale só na visita */ }
}
