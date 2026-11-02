const PREFIXO = 'cdb:painel:v1:'
export const chavePainelMovel = (escopoId, painelId) => `${PREFIXO}${encodeURIComponent(escopoId)}:${encodeURIComponent(painelId)}`

export function limitarPainelMovel(posicao, tamanho, janela) {
  const margem = 8
  const a = janela.area ?? { x: 0, y: 0, largura: janela.largura, altura: janela.altura }
  const esquerda = Math.max(0, a.x) + margem, topo = Math.max(0, a.y) + margem
  const direita = Math.min(janela.largura, a.x + a.largura) - margem
  const fundo = Math.min(janela.altura, a.y + a.altura) - margem
  const ajustar = (x, y, fimX, fimY) => {
    const largura = Math.max(1, Math.min(tamanho.largura, fimX - x))
    const altura = Math.max(1, Math.min(tamanho.altura, fimY - y))
    return { x: Math.min(Math.max(x, posicao.x), Math.max(x, fimX - largura)),
      y: Math.min(Math.max(y, posicao.y), Math.max(y, fimY - altura)), largura, altura }
  }
  const atual = ajustar(esquerda, topo, direita, fundo), r = janela.reserva
  const livre = p => !r || p.x + p.largura <= r.x || p.x >= r.x + r.largura
    || p.y + p.altura <= r.y || p.y >= r.y + r.altura
  if (livre(atual)) return atual
  const distancia = p => (p.x - posicao.x) ** 2 + (p.y - posicao.y) ** 2
  const opcoes = [[esquerda, topo, direita, Math.min(fundo, r.y - margem)],
    [esquerda, topo, Math.min(direita, r.x - margem), fundo],
    [Math.max(esquerda, r.x + r.largura + margem), topo, direita, fundo],
    [esquerda, Math.max(topo, r.y + r.altura + margem), direita, fundo]]
    .filter(([x, y, fimX, fimY]) => fimX > x && fimY > y).map(p => ajustar(...p)).filter(livre)
    .sort((a, b) => b.largura * b.altura - a.largura * a.altura || distancia(a) - distancia(b))
  return opcoes[0] ?? { ...atual, altura: 0 }
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
