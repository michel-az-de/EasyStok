// Entrada direta com Google (#1810, revisão gravada de 14/10): quem já escolheu a conta Google
// neste navegador entra sem ver a tela de login (One Tap com `auto_select`). Depois de "Sair"
// nesta aba, a tela fica parada até a pessoa escolher de novo; senão sair viraria entrar.
const CHAVE = 'easystok.google-sem-entrada-sozinha'

const travada = () => {
  try { return sessionStorage.getItem(CHAVE) === '1' } catch { return false }
}

export function opcoesDaEntradaGoogle() {
  const sozinha = !travada()
  return { auto_select: sozinha, oferecer: sozinha }
}

export function travarEntradaSozinha() {
  try { sessionStorage.setItem(CHAVE, '1') } catch { /* armazenamento indisponível */ }
  try { window.google?.accounts?.id?.disableAutoSelect?.() } catch { /* script do Google ausente */ }
}

export function liberarEntradaSozinha() {
  try { sessionStorage.removeItem(CHAVE) } catch { /* armazenamento indisponível */ }
}

// #2225: no Chrome do Android o popup do botão Google ficava em branco em /gsi/transform e o
// login não voltava. Lá o botão usa o seletor de conta do próprio Chrome (FedCM, 132 ou mais).
// Nos demais fica o popup: o Google não avisa quando o FedCM falha, e um Chromium sem a tela do
// FedCM (WebView, Electron e afins) deixaria o botão mudo.
const OUTRO_NAVEGADOR = /; wv\)|EdgA?\/|OPR\/|SamsungBrowser\/|YaBrowser\//

export function fedcmNoBotaoGoogle(agente = globalThis.navigator?.userAgent) {
  if (typeof agente !== 'string' || !agente.includes('Android') || OUTRO_NAVEGADOR.test(agente)) return false
  const versao = /Chrome\/(\d+)\./.exec(agente)
  return versao !== null && Number(versao[1]) >= 132
}
