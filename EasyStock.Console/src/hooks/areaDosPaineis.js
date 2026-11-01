const retangulo = no => {
  const r = no?.getBoundingClientRect?.()
  return r?.width > 0 && r.height > 0 ? { x: r.left, y: r.top, largura: r.width, altura: r.height } : null
}

export function janelaDosPaineis() {
  const largura = window.innerWidth, altura = window.innerHeight
  const visivel = window.visualViewport
  const esquerda = visivel?.offsetLeft ?? 0, topo = visivel?.offsetTop ?? 0
  const direita = Math.min(largura, esquerda + (visivel?.width ?? largura))
  const fundo = Math.min(altura, topo + (visivel?.height ?? altura))
  const buscar = seletor => globalThis.document?.querySelector?.(seletor)
  const main = retangulo(buscar('#conteudo-principal'))
  const cabecalho = retangulo(buscar('[data-cabecalho-conversa]')) ?? retangulo(buscar('[data-cabecalho-ficha]'))
  const x = Math.max(esquerda, main?.x ?? 0)
  const y = Math.max(topo, main?.y ?? 0, cabecalho ? cabecalho.y + cabecalho.altura : 0)
  const area = { x, y, largura: Math.max(0, Math.min(direita, main ? main.x + main.largura : direita) - x), altura: Math.max(0, Math.min(fundo, main ? main.y + main.altura : fundo) - y) }
  const envio = buscar('[data-area-envio]')
  const partes = [envio, ...(envio?.querySelectorAll?.('details[open] > div') ?? [])].map(retangulo).filter(Boolean)
  const reserva = partes.length ? { x: Math.min(...partes.map(r => r.x)), y: Math.min(...partes.map(r => r.y)) } : null
  if (reserva) {
    reserva.largura = Math.max(...partes.map(r => r.x + r.largura)) - reserva.x
    reserva.altura = Math.max(...partes.map(r => r.y + r.altura)) - reserva.y
  }
  return { largura, altura, area, reserva }
}

export function observarAreaDosPaineis(aoMudar, painel) {
  const observador = typeof ResizeObserver === 'function' ? new ResizeObserver(agendar) : null
  const observados = new Set()
  let quadro = null
  function sincronizar() {
    const main = globalThis.document?.querySelector?.('#conteudo-principal')
    const envio = globalThis.document?.querySelector?.('[data-area-envio]')
    for (const no of [painel, main, envio].filter(Boolean)) {
      if (!observados.has(no)) { observados.add(no); observador?.observe(no) }
    }
    for (const no of observados) if (no.isConnected === false) { observador?.unobserve?.(no); observados.delete(no) }
  }
  function agendar() {
    if (quadro !== null) return
    if (!globalThis.requestAnimationFrame) { sincronizar(); aoMudar(); return }
    quadro = requestAnimationFrame(() => { quadro = null; sincronizar(); aoMudar() })
  }
  sincronizar()
  const mudancas = typeof MutationObserver === 'function' ? new MutationObserver(agendar) : null
  const main = globalThis.document?.querySelector?.('#conteudo-principal')
  if (main) mudancas?.observe(main, { childList: true, subtree: true })
  window.addEventListener('resize', agendar)
  window.visualViewport?.addEventListener('resize', agendar)
  window.visualViewport?.addEventListener('scroll', agendar)
  globalThis.document?.addEventListener?.('toggle', agendar, true)
  return () => {
    if (quadro !== null) globalThis.cancelAnimationFrame?.(quadro)
    observador?.disconnect(); mudancas?.disconnect()
    window.removeEventListener('resize', agendar)
    window.visualViewport?.removeEventListener('resize', agendar)
    window.visualViewport?.removeEventListener('scroll', agendar)
    globalThis.document?.removeEventListener?.('toggle', agendar, true)
  }
}
