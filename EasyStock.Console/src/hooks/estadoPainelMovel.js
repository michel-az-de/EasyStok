const PREFIXO = 'cdb:painel:v1:'
export const chavePainelMovel = (escopoId, painelId) => `${PREFIXO}${encodeURIComponent(escopoId)}:${encodeURIComponent(painelId)}`

export function limitarPainelMovel(posicao, tamanho, janela) {
  const margem = 8
  const largura = Math.max(1, Math.min(tamanho.largura, janela.largura - margem * 2))
  const altura = Math.max(1, Math.min(tamanho.altura, janela.altura - margem * 2))
  return {
    x: Math.min(Math.max(margem, posicao.x), Math.max(margem, janela.largura - largura - margem)),
    y: Math.min(Math.max(margem, posicao.y), Math.max(margem, janela.altura - altura - margem)),
    largura, altura,
  }
}

export function lerPainelMovel(escopoId, painelId, armazenamento) {
  if (!escopoId || !painelId) return null
  try {
    const p = JSON.parse((armazenamento ?? globalThis.localStorage)?.getItem(chavePainelMovel(escopoId, painelId)) ?? 'null')
    if (typeof p?.solto !== 'boolean') return null
    const resultado = { solto: p.solto }
    for (const campo of ['x', 'y', 'largura', 'altura']) {
      if (p[campo] !== undefined) {
        if (!Number.isFinite(p[campo]) || (['largura', 'altura'].includes(campo) && p[campo] <= 0)) return null
        resultado[campo] = p[campo]
      }
    }
    return resultado
  } catch { return null }
}

export function guardarPainelMovel(escopoId, painelId, estado, armazenamento) {
  if (!escopoId || !painelId || typeof estado?.solto !== 'boolean') return false
  const preferencia = { solto: estado.solto }
  for (const campo of ['x', 'y', 'largura', 'altura']) {
    if (estado[campo] !== undefined) {
      if (!Number.isFinite(estado[campo]) || (['largura', 'altura'].includes(campo) && estado[campo] <= 0)) return false
      preferencia[campo] = estado[campo]
    }
  }
  try {
    const destino = armazenamento ?? globalThis.localStorage
    if (!destino) return false
    destino.setItem(chavePainelMovel(escopoId, painelId), JSON.stringify(preferencia))
    return true
  } catch { return false }
}

// Ordem visual só em memória. Nenhum conteúdo ou histórico de foco é persistido.
let frente = 120
export const proximaFrentePainel = () => ++frente
