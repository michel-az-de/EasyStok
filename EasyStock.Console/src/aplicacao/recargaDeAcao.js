const ESPERA_RECARGA_MS = 300
// Folga para o evento da própria ação chegar; passou disso, o SSE está vivo e mudo e a ação relê.
const PRAZO_DO_EVENTO_MS = 2000

// Recarga de uma tela que ouve o SSE da operação e também age (#1577). A ação relia na resposta e o
// evento que ela mesma provoca relia de novo 300 ms depois: dois lotes de GET por toque. Com o SSE
// vivo quem relê é o evento, e a ação só espera a recarga dele para soltar os botões. Com o SSE caído
// nada muda: a ação relê na resposta. Evento em rajada vira uma recarga só.
// `recarregar(completa)`: `completa` quando algum evento da rajada pediu a leitura inteira (`ready`).
export function criarRecargaDeAcao({
  recarregar, aoVivo, esperaMs = ESPERA_RECARGA_MS, prazoMs = PRAZO_DO_EVENTO_MS,
}) {
  let ligado = true
  let eventos = 0
  let espera = null
  let completa = false
  let ultima = Promise.resolve()
  let fila = []
  const prazos = new Set()

  // A recarga da tela já guarda o próprio erro; aqui ela só não pode prender quem espera.
  const reler = (inteira) => Promise.resolve(recarregar(inteira)).catch(() => {})

  const relerPeloEvento = () => {
    espera = null
    const quem = fila
    fila = []
    const inteira = completa
    completa = false
    ultima = reler(inteira)
    ultima.then(() => quem.forEach((soltar) => soltar()))
  }

  return {
    aoEvento(inteira = false) {
      eventos += 1
      completa = completa || inteira
      // Quem esperava o evento agora espera a recarga dele, sem reler por conta própria.
      prazos.forEach(clearTimeout)
      prazos.clear()
      clearTimeout(espera)
      espera = setTimeout(relerPeloEvento, esperaMs)
    },
    // Tirada antes de enviar a ação: evento que chegar dali em diante já conta como dela (a API
    // publica o evento antes de responder, e a resposta pode demorar mais que a recarga).
    marcar: () => eventos,
    // Resolve quando a tela já reflete a ação.
    depoisDaAcao(marca) {
      if (!ligado) return Promise.resolve()
      if (!aoVivo()) return reler(false)
      const chegou = eventos > marca
      if (chegou && espera === null) return ultima
      return new Promise((soltar) => {
        fila.push(soltar)
        if (chegou) return
        const prazo = setTimeout(() => {
          prazos.delete(prazo)
          fila = fila.filter((outro) => outro !== soltar)
          reler(false).then(soltar)
        }, prazoMs)
        prazos.add(prazo)
      })
    },
    desligar() {
      ligado = false
      clearTimeout(espera)
      prazos.forEach(clearTimeout)
      prazos.clear()
      fila.forEach((soltar) => soltar())
      fila = []
    },
  }
}
