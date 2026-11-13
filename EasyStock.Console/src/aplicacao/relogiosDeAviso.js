// Relógios dos avisos na tela (#1807, #2185 F28). Cada aviso tem a sua contagem; com o ponteiro ou o foco
// em cima (`pausar`) todas param e, ao sair (`retomar`), seguem do que faltava. Aviso que chega durante a
// pausa espera junto. O `Aviso` (snackbar de ação) já fazia assim; o toast sumia em 6 s com a mão em cima.
export function criarRelogiosDeAviso({ duracaoMs, aoVencer }) {
  const relogios = new Map()
  let pausado = false

  const armar = (id, restante) => {
    const relogio = relogios.get(id)
    if (!relogio) return
    clearTimeout(relogio.timer)
    relogio.restante = restante
    relogio.inicio = Date.now()
    relogio.corre = true
    relogio.timer = setTimeout(() => {
      relogios.delete(id)
      aoVencer(id)
    }, restante)
  }

  return {
    iniciar(id) {
      relogios.set(id, { restante: duracaoMs, inicio: 0, corre: false, timer: null })
      if (!pausado) armar(id, duracaoMs)
    },
    cancelar(id) {
      clearTimeout(relogios.get(id)?.timer)
      relogios.delete(id)
    },
    pausar() {
      if (pausado) return
      pausado = true
      const agora = Date.now()
      for (const relogio of relogios.values()) {
        if (!relogio.corre) continue
        clearTimeout(relogio.timer)
        relogio.restante = Math.max(0, relogio.restante - (agora - relogio.inicio))
        relogio.corre = false
      }
    },
    retomar() {
      if (!pausado) return
      pausado = false
      for (const [id, relogio] of relogios) armar(id, relogio.restante)
    },
    limpar() {
      for (const relogio of relogios.values()) clearTimeout(relogio.timer)
      relogios.clear()
    },
  }
}
