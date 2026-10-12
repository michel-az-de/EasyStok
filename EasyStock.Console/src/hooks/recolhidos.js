// Memória das seções recolhidas da Ficha (#1442). Puro, sem React nem
// localStorage: o hook `useRecolhido` guarda e lê, aqui só a forma do dado.
// Mapa `{ [chave]: true }` por seção recolhida; valor que não é booleano (outra
// versão, edição na mão) é descartado em vez de quebrar a ficha.

export const CHAVE_RECOLHIDOS = 'cdb.ficha.recolhidos.v1'

export function lerRecolhidos(bruto) {
  try {
    const valor = JSON.parse(bruto ?? '{}')
    if (!valor || typeof valor !== 'object' || Array.isArray(valor)) return {}
    return Object.fromEntries(Object.entries(valor).filter(([, v]) => typeof v === 'boolean'))
  } catch {
    return {}
  }
}

export const alternarRecolhido = (mapa, chave) => ({ ...mapa, [chave]: !mapa[chave] })
