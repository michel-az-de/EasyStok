const SITUACOES = { Montando: 'Viagem em montagem', EmRota: 'Viagem em rota', Concluida: 'Viagem concluída', Desfeita: 'Viagem desfeita' }
const enderecoValido = (valor) => typeof valor === 'string' && valor.trim().length > 0

export const tituloDaViagem = (viagem) => SITUACOES[viagem?.situacao] ?? 'Viagem'

export function viagemDoPedido(dados, pedidoId) {
  if (!pedidoId || dados?.pedidoId !== pedidoId) throw new Error('A viagem não corresponde a este pedido.')
  if (dados.viagem === null) return null
  const viagem = dados.viagem
  if (!viagem || !SITUACOES[viagem.situacao] || !Array.isArray(viagem.paradas)
    || !viagem.paradas.some((p) => p.pedidoId === pedidoId)) throw new Error('A viagem não corresponde a este pedido.')
  return { ...viagem, paradas: [...viagem.paradas].sort((a, b) => a.ordem - b.ordem) }
}

// URLs oficiais do Google: endereços reais, sem coordenadas ou posição do entregador inferidas.
export function mapaDaViagem({ viagem, pedidoId, endereco, chaveEmbed = '' }) {
  const paradas = viagem?.paradas ?? []
  const enderecos = paradas.map((p) => p.endereco)
  const destino = viagem ? paradas.find((p) => p.pedidoId === pedidoId)?.endereco : endereco
  const completos = enderecos.length > 1 && enderecos.every(enderecoValido)
  const roteiro = completos && enderecos.length <= 22 // Embed: até 20 waypoints além de origem/destino.
  const q = enderecoValido(destino) ? destino.trim() : null
  let embedUrl = null
  if (typeof chaveEmbed === 'string' && chaveEmbed.trim() && (roteiro || q)) {
    const parametros = new URLSearchParams({ key: chaveEmbed.trim(), language: 'pt-BR' })
    if (roteiro) {
      parametros.set('origin', enderecos[0]); parametros.set('destination', enderecos.at(-1))
      parametros.set('mode', 'driving'); parametros.set('units', 'metric')
      if (enderecos.length > 2) parametros.set('waypoints', enderecos.slice(1, -1).join('|'))
    } else parametros.set('q', q)
    embedUrl = `https://www.google.com/maps/embed/v1/${roteiro ? 'directions' : 'place'}?${parametros}`
  }
  // Maps URLs suporta até 3 waypoints no navegador móvel. Não corta uma viagem maior em silêncio.
  const roteiroExterno = completos && enderecos.length <= 5
  let mapsUrl = null
  if (roteiroExterno) {
    const parametros = new URLSearchParams({ api: '1', origin: enderecos[0], destination: enderecos.at(-1), travelmode: 'driving' })
    if (enderecos.length > 2) parametros.set('waypoints', enderecos.slice(1, -1).join('|'))
    mapsUrl = `https://www.google.com/maps/dir/?${parametros}`
    if (mapsUrl.length > 2048) mapsUrl = null
  }
  if (!mapsUrl && q) {
    const url = `https://www.google.com/maps/search/?${new URLSearchParams({ api: '1', query: q })}`
    if (url.length <= 2048) mapsUrl = url
  }
  return { embedUrl, mapsUrl, roteiro, roteiroExterno: roteiroExterno && Boolean(mapsUrl?.includes('/maps/dir/')),
    temEndereco: Boolean(roteiro || q) }
}
