// #3149: dicionário da loja do Assistente (termos e gírias da loja e da clientela). Puro: a API é a autoridade das
// regras; a tela só as antecipa, para a dona ver o problema antes de salvar. Linha = { termo, significado }.
export const DICIONARIO_TERMOS_MAXIMO = 60
export const DICIONARIO_TERMO_MAXIMO = 40
export const DICIONARIO_SIGNIFICADO_MAXIMO = 120

const INVISIVEIS = new Set([0x00AD, 0x061C, 0x200B, 0x200E, 0x200F, 0x2028, 0x2029, 0x2060, 0x3164, 0xFEFF])

// Controle (C0 e C1), separador de linha ou parágrafo, marcas de direção, caracteres de tag e invisíveis que embaralham a leitura. U+200D fica de fora: monta emoji.
const proibido = (codigo) => codigo <= 0x1F || (codigo >= 0x7F && codigo <= 0x9F) || INVISIVEIS.has(codigo)
  || (codigo >= 0x202A && codigo <= 0x202E) || (codigo >= 0x2066 && codigo <= 0x2069) || (codigo >= 0xE0000 && codigo <= 0xE007F)
const temProibido = (texto) => [...texto].some((c) => proibido(c.codePointAt(0)))

// Igualdade do termo sem diferença de maiúscula nem de acento.
const chaveDoTermo = (termo) => termo.trim().normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

export function erroDoDicionario(linhas) {
  if (linhas.length > DICIONARIO_TERMOS_MAXIMO) return `O dicionário aceita até ${DICIONARIO_TERMOS_MAXIMO} termos.`
  const vistos = new Map()
  for (const [i, linha] of linhas.entries()) {
    const termo = (linha.termo ?? '').trim()
    const significado = (linha.significado ?? '').trim()
    const onde = termo ? `"${termo}"` : `A linha ${i + 1}`
    if (!termo) return `A linha ${i + 1} está sem termo. Escreva a palavra ou tire a linha.`
    if (!significado) return `Escreva o que "${termo}" quer dizer.`
    if (termo.length > DICIONARIO_TERMO_MAXIMO) return `O termo "${termo.slice(0, 20)}…" passa de ${DICIONARIO_TERMO_MAXIMO} caracteres (linha ${i + 1}).`
    if (significado.length > DICIONARIO_SIGNIFICADO_MAXIMO) return `O que "${termo}" quer dizer passa de ${DICIONARIO_SIGNIFICADO_MAXIMO} caracteres.`
    if (termo.includes('"')) return `O termo "${termo.replaceAll('"', '')}" não pode ter aspas (linha ${i + 1}). Tire as aspas.`
    if (temProibido(termo) || temProibido(significado)) return `${onde} tem quebra de linha ou caractere invisível. Escreva em uma linha só, sem copiar de outro lugar.`
    const chave = chaveDoTermo(termo)
    if (vistos.has(chave)) return `"${termo}" repete o termo da linha ${vistos.get(chave)} (maiúscula e acento contam como igual). Deixe só um.`
    vistos.set(chave, i + 1)
  }
  return null
}

export const dicionarioParaSalvar = (linhas) => linhas.map((l) => ({ termo: (l.termo ?? '').trim(), significado: (l.significado ?? '').trim() }))

export const mesmoDicionario = (a, b) => JSON.stringify(dicionarioParaSalvar(a ?? [])) === JSON.stringify(dicionarioParaSalvar(b ?? []))
