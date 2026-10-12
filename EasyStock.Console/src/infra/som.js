import { EVENTOS_DE_SOM, ROTULO_DO_SOM } from '../dominio/automatico'

// Som de verdade, sintetizado por Web Audio API (D6: ela está de costas para
// a tela, com as mãos na massa, e o som é o que avisa; áudio 05/06 pedem um
// timbre por tipo de evento, igual ao "chama corrida" do Uber, e o
// "barulhinho do dinheiro" no pagamento). Sem arquivo de áudio: osciladores
// puros evitam licença e peso no tablet da cozinha.
//
// Três timbres, cada um com forma de onda e desenho de notas diferente, para
// dar para reconhecer de ouvido, sem olhar:
//   dinheiro entrando (RN-25, o pico emocional da entrevista): três notas
//   subindo, triangular, o mais alegre dos três.
//   mensagem nova do cliente (UC-05 passo 1): duas notas suaves, senoidal,
//   parecido com notificação de celular.
//   precisa de ação (passagem, reclamação, Pix vencendo): três apitos curtos
//   alternados, quadrada, mais insistente sem virar irritante.
//
// Quem chama: src/aplicacao/useAvisoSonoro.js, nos três momentos do domínio,
// e os botões "Ouvir" do ModalAutomacoes, para ela reconhecer os três antes
// de confiar na chave.
//
// Limitação real, registrada aqui e em auditoria/decisoes/27-som.md: o
// navegador SUSPENDE o áudio quando a aba vai para segundo plano, e no
// tablet Android com a tela apagada a aba sempre fica em segundo plano. Este
// protótipo web não resolve isso; a fila já tem o app Android nativo, que
// tem canal de notificação sonora fora da aba.

export const EVENTOS = EVENTOS_DE_SOM

let contexto = null

function obterContexto() {
  if (contexto) return contexto
  if (typeof window === 'undefined') return null
  const Construtor = window.AudioContext || window.webkitAudioContext
  if (!Construtor) return null
  contexto = new Construtor()
  return contexto
}

// O navegador bloqueia áudio até o primeiro gesto do usuário na página.
// Chamado de dois lugares: do clique/toque global (AtendimentoProvider) e de
// cada botão "Ouvir" (que já É um gesto, então destrava de brinde).
export function destravarAudio() {
  const ctx = obterContexto()
  if (ctx && ctx.state === 'suspended') ctx.resume().catch(() => {})
}

// Para a tela decidir se mostra o aviso "Toque para ligar o som".
export function audioDestravado() {
  return Boolean(contexto) && contexto.state === 'running'
}

// Uma nota: oscilador com envelope curto (ataque rápido, decaimento
// exponencial), audível numa cozinha sem virar apito de alarme.
function nota(ctx, { freq, inicio, duracao, tipo, pico }) {
  const osc = ctx.createOscillator()
  const ganho = ctx.createGain()
  osc.type = tipo
  osc.frequency.setValueAtTime(freq, ctx.currentTime + inicio)
  ganho.gain.setValueAtTime(0.0001, ctx.currentTime + inicio)
  ganho.gain.linearRampToValueAtTime(pico, ctx.currentTime + inicio + 0.012)
  ganho.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + inicio + duracao)
  osc.connect(ganho)
  ganho.connect(ctx.destination)
  osc.start(ctx.currentTime + inicio)
  osc.stop(ctx.currentTime + inicio + duracao + 0.03)
}

const TIMBRES = {
  // Dinheiro entrando: dó-mi-sol subindo, triangular, o mais alegre.
  [EVENTOS_DE_SOM.PAGAMENTO_CONFIRMADO]: (ctx) => {
    nota(ctx, { freq: 1046.5, inicio: 0, duracao: 0.13, tipo: 'triangle', pico: 0.22 })
    nota(ctx, { freq: 1318.5, inicio: 0.09, duracao: 0.13, tipo: 'triangle', pico: 0.22 })
    nota(ctx, { freq: 1568.0, inicio: 0.18, duracao: 0.24, tipo: 'triangle', pico: 0.24 })
  },
  // Mensagem nova do cliente: duas notas senoidais descendo, tipo "ding-dong".
  [EVENTOS_DE_SOM.NOVO_ATENDIMENTO]: (ctx) => {
    nota(ctx, { freq: 880.0, inicio: 0, duracao: 0.12, tipo: 'sine', pico: 0.18 })
    nota(ctx, { freq: 659.25, inicio: 0.11, duracao: 0.18, tipo: 'sine', pico: 0.16 })
  },
  // Precisa de ação: três apitos curtos alternados, quadrada, mais insistente.
  [EVENTOS_DE_SOM.PASSOU_PARA_VOCE]: (ctx) => {
    nota(ctx, { freq: 600, inicio: 0, duracao: 0.09, tipo: 'square', pico: 0.13 })
    nota(ctx, { freq: 450, inicio: 0.13, duracao: 0.09, tipo: 'square', pico: 0.13 })
    nota(ctx, { freq: 600, inicio: 0.26, duracao: 0.11, tipo: 'square', pico: 0.13 })
  },
}

function tocarTimbre(evento) {
  const ctx = obterContexto()
  if (!ctx) return
  if (ctx.state === 'suspended') ctx.resume().catch(() => {})
  TIMBRES[evento]?.(ctx)
}

// Toca o aviso do evento quando a chave está ligada. Devolve se tocou (ou
// teria tocado), para quem quiser medir sem depender do alto-falante.
export function tocarAviso(evento, ligado = true) {
  if (!ligado) return false
  if (!ROTULO_DO_SOM[evento]) return false
  tocarTimbre(evento)
  return true
}

// Amostra sob demanda, para ela reconhecer o som antes de confiar na chave.
// Toca mesmo com "Som da cozinha" desligado: é um pedido explícito de ouvir,
// não um aviso automático.
export function tocarAmostra(evento) {
  if (!ROTULO_DO_SOM[evento]) return false
  destravarAudio()
  tocarTimbre(evento)
  return true
}
