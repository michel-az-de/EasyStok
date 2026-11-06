// #2258 (A9/G4): o que o menu da conta (avatar) e o menu ⋮ compacto têm em comum, num lugar só:
// as opções de tema, na ordem em que aparecem, e o rótulo do Sair.
export const OPCOES_DE_TEMA = [
  { valor: 'light', rotulo: 'Claro', icone: 'sun' },
  { valor: 'dark', rotulo: 'Escuro', icone: 'moon' },
]

export const rotuloDoSair = (sessaoPersistente) => (sessaoPersistente ? 'Sair deste aparelho' : 'Sair')
