// Vigia de uma conexão que deveria falar sempre (#1577). O SSE de operação manda `: heartbeat` a
// cada 25 s de fila parada (`OperacaoEventosController.IntervaloHeartbeat`). Conexão meio-aberta
// (o Wi-Fi caiu sem avisar) não dá erro nem termina: a tela ficava "Ao vivo" sem atualizar. Passou
// de 2,5 intervalos sem chegar nada, nem heartbeat, ela é dada como caída.
const HEARTBEAT_DA_API_MS = 25000
export const SILENCIO_MAXIMO_MS = 2.5 * HEARTBEAT_DA_API_MS

export function criarVigiaDeSilencio({ aoCalar, prazoMs = SILENCIO_MAXIMO_MS }) {
  let prazo = null
  return {
    // Chegou alguma coisa, ou a espera começou: o prazo recomeça.
    sinal() {
      clearTimeout(prazo)
      prazo = setTimeout(aoCalar, prazoMs)
    },
    parar() {
      clearTimeout(prazo)
      prazo = null
    },
  }
}
