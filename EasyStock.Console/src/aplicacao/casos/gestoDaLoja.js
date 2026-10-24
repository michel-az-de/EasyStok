import * as acao from '../acoes'

// Gesto de abrir ou fechar a loja (`ui.gestoLoja`). O modo API também despacha estes casos (`api/expediente.js`).

export const casosGestoDaLoja = {
  // #1443: qual gesto da loja está aberto ('abrir' | 'fechar'), ou nenhum.
  [acao.PEDIR_GESTO_LOJA]: (estado, { gesto }) => ({ ...estado, ui: { ...estado.ui, gestoLoja: gesto } }),

  [acao.FECHAR_GESTO_LOJA]: (estado) => ({ ...estado, ui: { ...estado.ui, gestoLoja: null } }),
}
