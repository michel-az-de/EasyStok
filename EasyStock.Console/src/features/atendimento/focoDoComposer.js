// #3339 (R1C-07): depois de "Enviar link" o modal do cardápio fecha, o botão que o abriu pode já não existir (menu ⋮,
// ficha) e o foco ia para o `main`. Quem atende volta ao campo de mensagem, como depois de salvar a nota (#3317).
// Feature não importa feature: o nome do campo (o rótulo do Composer) mora aqui também, ao lado do de
// `caixa-de-entrada/focoDoCampo.js`; a `prova-3339-foco-apos-cardapio.mjs` reprova se o Composer mudar o rótulo.

const NOME_DO_CAMPO_DE_MENSAGEM = 'Mensagem para o cliente'

const temPonteiroFino = () => typeof window !== 'undefined' && window.matchMedia?.('(pointer: fine)').matches === true

// No toque o teclado abriria sem ela pedir: lá o foco fica onde está.
export function devolverFocoAoCampoDeMensagem({ documento = document, ponteiroFino = temPonteiroFino() } = {}) {
  if (!ponteiroFino) return false
  const campo = [...documento.querySelectorAll('textarea')].find((c) => c.labels?.[0]?.textContent === NOME_DO_CAMPO_DE_MENSAGEM)
  if (!campo || campo.disabled) return false
  campo.focus()
  return documento.activeElement === campo
}

// O Modal devolve o foco ao acionador quando desmonta; este pedido tem de vir depois do commit, não antes.
export function devolverFocoAoCampoDepoisDeFechar() {
  requestAnimationFrame(() => requestAnimationFrame(() => devolverFocoAoCampoDeMensagem()))
}
