// #3394: regras puras do painel de Avisos junto ao sino. O dado chega por parâmetro; quem mede a tela e
// quem chama a API ficam em `features/notificacoes` e `aplicacao/useNotificacoesDaSessao.js`.

const GRUPOS = ['Hoje', 'Ontem', 'Anteriores']

const inicioDoDia = (instante) => {
  const dia = new Date(instante)
  dia.setHours(0, 0, 0, 0)
  return dia.getTime()
}

// Como no Instagram e no Facebook, a lista se lê por dia. A ordem que a API mandou (mais novo primeiro) fica
// dentro de cada grupo, e grupo vazio não aparece. Data que não se lê vai para Anteriores, nunca para Hoje.
export function agruparAvisos(avisos, agora) {
  const hoje = inicioDoDia(agora)
  const ontem = new Date(hoje)
  ontem.setDate(ontem.getDate() - 1)
  const grupoDe = (aviso) => {
    const instante = aviso.createdAt ? new Date(aviso.createdAt).getTime() : NaN
    if (Number.isNaN(instante)) return 'Anteriores'
    if (instante >= hoje) return 'Hoje'
    return instante >= ontem.getTime() ? 'Ontem' : 'Anteriores'
  }
  const porGrupo = new Map(GRUPOS.map((titulo) => [titulo, []]))
  for (const aviso of avisos) porGrupo.get(grupoDe(aviso)).push(aviso)
  return GRUPOS.map((titulo) => ({ titulo, avisos: porGrupo.get(titulo) })).filter((grupo) => grupo.avisos.length > 0)
}

const VAO = 8 // distância entre o sino e o painel
const MARGEM = 12 // distância mínima entre o painel e a borda da janela
const LARGURA = 400
const CELULAR = 760

// Onde o painel abre, em coordenadas da janela. Três lugares, um por casa do sino:
//   folha   (celular): largura da tela, logo abaixo do sino do topo;
//   direita (trilho do desktop): ao lado do trilho, à altura do sino, sem passar do pé (o Instagram abre
//           as notificações ao lado da barra lateral);
//   abaixo  (topo do tablet e do hall): embaixo do sino, alinhado pela direita dele (o Facebook faz assim),
//           sem sair da janela.
export function posicaoDoPainel({ ancora, janela, alturaDoPainel }) {
  if (janela.largura < CELULAR) {
    const top = ancora.bottom + VAO
    return { lado: 'folha', top, left: VAO, right: VAO, maxHeight: Math.max(1, janela.altura - top - VAO) }
  }
  const largura = Math.min(LARGURA, janela.largura - 2 * MARGEM)
  const maxHeight = janela.altura - 2 * MARGEM
  const noTrilho = (ancora.left + ancora.right) / 2 < janela.largura / 3 && ancora.bottom > janela.altura / 4
  if (noTrilho) {
    const altura = Math.min(alturaDoPainel, maxHeight)
    const top = Math.max(MARGEM, Math.min(ancora.top, janela.altura - MARGEM - altura))
    return { lado: 'direita', top, left: ancora.right + VAO, width: largura, maxHeight: janela.altura - MARGEM - top }
  }
  // Cresce para a esquerda a partir da borda direita do sino; se assim sairia da tela (sino no começo do topo,
  // como no tablet), encosta na margem, como o Popover faz.
  const top = ancora.bottom + VAO
  const left = Math.max(MARGEM, Math.min(ancora.right - largura, janela.largura - largura - MARGEM))
  return { lado: 'abaixo', top, left, width: largura, maxHeight: Math.max(1, janela.altura - top - MARGEM) }
}
