import { createPortal } from 'react-dom'

// Portal de impressão (seção 3.4 e 5 da direção visual, passo zero): tudo
// que precisa ir para o papel (canhoto, resumo A4) nasce dentro deste
// componente, que sai direto no `body`, fora de #root. Em tela ele fica
// oculto (`estilos/base.css`); a `@media print` esconde o app inteiro e
// mostra só isto. Quem chama `window.print()` é o botão "Imprimir" de cada
// modal, nunca este componente.
export function FolhaImpressao({ children }) {
  if (typeof document === 'undefined') return null
  return createPortal(<div data-imprimir="true">{children}</div>, document.body)
}
