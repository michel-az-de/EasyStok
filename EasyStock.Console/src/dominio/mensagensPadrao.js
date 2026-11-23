// #3206: regras puras da lista de mensagens padrão do compositor. A tela só exibe.

// Ausente conta como Meta: o servidor antigo não manda `origem`.
export const origemDoModelo = (modelo) => modelo?.origem === 'Local' ? 'Local' : 'Meta'
export const soOrigemMeta = (modelos) => (modelos ?? []).filter((m) => origemDoModelo(m) === 'Meta')

// `boas_vindas_loja` vira "Boas vindas loja".
export function nomeLegivel(nome) {
  const texto = String(nome ?? '').replace(/_+/g, ' ').trim()
  return texto ? texto.charAt(0).toUpperCase() + texto.slice(1) : ''
}

// Números {{n}} que cabeçalho, corpo ou rodapé pedem; vale o maior entre eles e a contagem que o servidor informa.
export function numerosDosParametros(modelo) {
  const achados = []
  for (const trecho of [modelo?.cabecalho, modelo?.corpo, modelo?.rodape]) {
    for (const m of String(trecho ?? '').matchAll(/\{\{([1-9][0-9]?)\}\}/g)) achados.push(Number(m[1]))
  }
  const total = Math.max(Number(modelo?.quantidadeParametros) || 0, ...achados)
  return Array.from({ length: total }, (_, i) => i + 1)
}

const substituir = (texto, parametros) => String(texto ?? '').replace(/\{\{([1-9][0-9]?)\}\}/g,
  (original, n) => parametros?.[Number(n) - 1]?.trim() || original)

// Prévia ao vivo: parâmetro vazio mantém o {{n}} à vista.
export const previaDoModelo = (modelo, parametros) => ({
  cabecalho: substituir(modelo?.cabecalho, parametros),
  corpo: substituir(modelo?.corpo, parametros),
  rodape: substituir(modelo?.rodape, parametros),
})

export const parametrosCompletos = (modelo, parametros) => numerosDosParametros(modelo)
  .every((n) => { const v = (parametros?.[n - 1] ?? '').trim(); return v && !v.includes('{{') })

// Lead sem cadastro também envia: quem decide é o servidor.
export const podeEnviarModelo = ({ fonteApi, podeReabrir, aceitaModelo, encerrada, bloqueada }) =>
  Boolean(fonteApi && podeReabrir && aceitaModelo && !encerrada && !bloqueada)
