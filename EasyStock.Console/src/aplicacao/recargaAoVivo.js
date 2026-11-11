import { conectarEventosOperacao } from '../infra/api/eventosOperacao'

const ESPERA_RECARGA_MS = 300
// #2005 (console-20): as três constantes são de todas as telas que ouvem o SSE da operação (Agenda, Cozinha,
// Entregas), para a regra não divergir de uma para outra.
export const POLLING_SEM_SSE_MS = 15000
// Com o SSE vivo a tela já recarrega a cada evento; a conferência lenta cobre o que não tem evento
// (pedido criado no balcão) e a conexão viva e muda.
export const CONFERENCIA_AO_VIVO_MS = 30000
export const RECONECTAR_MS = 10000
// #2287: teto da espera entre tentativas quando o SSE cai em seguida (503, deploy, rede).
export const RECONECTAR_TETO_MS = 60000
const VARIACAO_DA_ESPERA = 0.25

// #2287: espera para reconectar o SSE caído. Começa em `base`, dobra a cada queda seguida até o teto e varia
// de 75% a 100% do valor, para as abas abertas não baterem juntas no endpoint depois de um deploy. `zerar` vai
// no `ready`: a queda depois de uma conexão boa volta a tentar na base. É a regra de todo ponto que abre o SSE.
export function criarEsperaDeReconexao({ base, teto = RECONECTAR_TETO_MS, aleatorio = Math.random }) {
  let cheia = base
  return {
    proxima() {
      const espera = Math.round(cheia * (1 - VARIACAO_DA_ESPERA * aleatorio()))
      cheia = Math.min(teto, cheia * 2)
      return espera
    },
    zerar() { cheia = base },
  }
}

// Recarga de uma tela que ouve o SSE da operação (#1565). Evento em rajada vira uma recarga só; o polling
// de 15 s fica para o SSE caído e cede à conferência de 30 s quando ele volta. Devolve a função que desliga.
export function ligarRecargaAoVivo({ recarregar, recarregaCom, conectar = conectarEventosOperacao, aleatorio }) {
  const esperaDeReconexao = criarEsperaDeReconexao({ base: RECONECTAR_MS, aleatorio })
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
        if (evento === 'ready') {
          esperaDeReconexao.zerar()
          conferirACada(CONFERENCIA_AO_VIVO_MS)
        }
        // Sem replay na API: a cada `ready` a lista é relida.
        if (evento === 'ready' || recarregaCom(evento)) agendarRecarga()
      },
      aoCair: () => {
        if (!ligado) return
        conferirACada(POLLING_SEM_SSE_MS)
        reconexao = setTimeout(ouvir, esperaDeReconexao.proxima())
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
