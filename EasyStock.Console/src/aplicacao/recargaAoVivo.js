import { conectarEventosOperacao } from '../infra/api/eventosOperacao'

const ESPERA_RECARGA_MS = 300
const POLLING_SEM_SSE_MS = 15000
// Com o SSE vivo a tela já recarrega a cada evento; a conferência lenta cobre o que não tem evento
// (pedido criado no balcão) e a conexão viva e muda.
const CONFERENCIA_AO_VIVO_MS = 30000
const RECONECTAR_MS = 10000

// Recarga de uma tela que ouve o SSE da operação (#1565). Evento em rajada vira uma recarga só; o polling
// de 15 s fica para o SSE caído e cede à conferência de 30 s quando ele volta. Devolve a função que desliga.
export function ligarRecargaAoVivo({ recarregar, recarregaCom, conectar = conectarEventosOperacao }) {
  let ligado = true
  let fechar = () => {}
  let espera = null
  let reconexao = null
  let polling = null

  const conferirACada = (ms) => {
    clearInterval(polling)
    polling = setInterval(recarregar, ms)
  }
  const agendarRecarga = () => {
    clearTimeout(espera)
    espera = setTimeout(recarregar, ESPERA_RECARGA_MS)
  }
  const ouvir = () => {
    fechar = conectar({
      aoEvento: ({ evento }) => {
        if (evento === 'ready') conferirACada(CONFERENCIA_AO_VIVO_MS)
        // Sem replay na API: a cada `ready` a lista é relida.
        if (evento === 'ready' || recarregaCom(evento)) agendarRecarga()
      },
      aoCair: () => {
        if (!ligado) return
        conferirACada(POLLING_SEM_SSE_MS)
        reconexao = setTimeout(ouvir, RECONECTAR_MS)
      },
    })
  }

  conferirACada(POLLING_SEM_SSE_MS)
  ouvir()
  return () => {
    ligado = false
    fechar()
    clearTimeout(espera)
    clearTimeout(reconexao)
    clearInterval(polling)
  }
}
