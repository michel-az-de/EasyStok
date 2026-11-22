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

// Igualdade do termo sem diferença de maiúscula nem de acento, igual à API: toda marca sem espaçamento (\p{Mn}) sai.
const chaveDoTermo = (termo) => termo.trim().normalize('NFD').replace(/\p{Mn}/gu, '').toLowerCase()

// Linha ainda sem termo ou sem significado: a tela mostra uma nota comum, não um alerta, enquanto a pessoa escreve.
export const temLinhaIncompleta = (linhas) => linhas.some((l) => !(l.termo ?? '').trim() || !(l.significado ?? '').trim())

export function erroDoDicionario(linhas) {
  if (linhas.length > DICIONARIO_TERMOS_MAXIMO) return `O dicionário aceita até ${DICIONARIO_TERMOS_MAXIMO} termos.`
  const vistos = new Map()
  for (const [i, linha] of linhas.entries()) {
    const termo = (linha.termo ?? '').trim()
    const significado = (linha.significado ?? '').trim()
    if (!termo) return `A linha ${i + 1} está sem termo. Escreva a palavra ou tire a linha.`
    if (!significado) return `Escreva o que "${termo}" quer dizer.`
    if (termo.length > DICIONARIO_TERMO_MAXIMO) return `O termo da linha ${i + 1} passa de ${DICIONARIO_TERMO_MAXIMO} caracteres.`
    if (significado.length > DICIONARIO_SIGNIFICADO_MAXIMO) return `O que a linha ${i + 1} quer dizer passa de ${DICIONARIO_SIGNIFICADO_MAXIMO} caracteres.`
    if (termo.includes('"')) return `O termo da linha ${i + 1} não pode ter aspas. Tire as aspas.`
    if (temProibido(termo) || temProibido(significado)) return `A linha ${i + 1} tem quebra de linha ou caractere invisível. Escreva em uma linha só, sem copiar de outro lugar.`
    const chave = chaveDoTermo(termo)
    if (vistos.has(chave)) return `"${termo}" repete o termo da linha ${vistos.get(chave)} (maiúscula e acento contam como igual). Deixe só um.`
    vistos.set(chave, i + 1)
  }
  return null
}

export const dicionarioParaSalvar = (linhas) => linhas.map((l) => ({ termo: (l.termo ?? '').trim(), significado: (l.significado ?? '').trim() }))

export const mesmoDicionario = (a, b) => JSON.stringify(dicionarioParaSalvar(a ?? [])) === JSON.stringify(dicionarioParaSalvar(b ?? []))
