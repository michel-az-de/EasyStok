// Foco ao abrir uma conversa pela lista do Balcão (#2135). Medido no desktop: abrir a conversa deixava o
// foco no cartão e cada resposta custava um clique a mais no campo. Sem pasta de domínio: é só DOM.

// O nome acessível do campo do Composer (`features/atendimento/Composer.jsx`). Feature não importa
// feature, então o nome mora aqui também; a prova `prova-2135-balcao-abre-certo.mjs` abre a tela de
// verdade e reprova se um dos dois mudar sozinho.
export const NOME_DO_CAMPO_DE_MENSAGEM = 'Mensagem para o cliente'

export function campoDeMensagem(documento = document) {
  return [...documento.querySelectorAll('textarea')]
    .find((campo) => campo.labels?.[0]?.textContent === NOME_DO_CAMPO_DE_MENSAGEM) ?? null
}

// No celular (e em qualquer toque) o teclado abriria sem ela pedir: lá o foco fica onde está.
export function focaOCampoAoAbrir({ celular, ponteiroFino }) {
  return !celular && ponteiroFino
}

export const temPonteiroFino = () => typeof window !== 'undefined' && window.matchMedia?.('(pointer: fine)').matches === true

export function focarCampoDeMensagem(documento = document) {
  const campo = campoDeMensagem(documento)
  if (!campo || campo.disabled) return false
  campo.focus()
  return documento.activeElement === campo
}
