const PREFIXO = 'cdb:postit:v1:'
const chave = (conversaId, notaId) => `${PREFIXO}${encodeURIComponent(conversaId)}:${encodeURIComponent(notaId)}`
const valida = (p) => Number.isFinite(p?.x) && Number.isFinite(p?.y)

export function limitarPostit(posicao, nota, janela) {
  const margem = 8
  return {
    x: Math.min(Math.max(margem, posicao.x), Math.max(margem, janela.largura - nota.largura - margem)),
    y: Math.min(Math.max(margem, posicao.y), Math.max(margem, janela.altura - nota.altura - margem)),
  }
}

export function lerPosicaoPostit(conversaId, notaId, armazenamento) {
  if (!conversaId || !notaId) return null
  try {
    const valor = JSON.parse((armazenamento ?? globalThis.localStorage)?.getItem(chave(conversaId, notaId)) ?? 'null')
    return valida(valor) ? { x: valor.x, y: valor.y } : null
  } catch { return null }
}

// Preferência local: o conteúdo da nota continua vindo exclusivamente do cadastro.
export function guardarPosicaoPostit(conversaId, notaId, posicao, armazenamento) {
  if (!conversaId || !notaId || !valida(posicao)) return false
  try {
    const destino = armazenamento ?? globalThis.localStorage
    if (!destino) return false
    destino.setItem(chave(conversaId, notaId), JSON.stringify({ x: posicao.x, y: posicao.y }))
    return true
  } catch { return false }
}

export function recolherPostit(conversaId, notaId, armazenamento) {
  try {
    const destino = armazenamento ?? globalThis.localStorage
    if (!destino) return false
    destino.removeItem(chave(conversaId, notaId))
    return true
  } catch { return false }
}
