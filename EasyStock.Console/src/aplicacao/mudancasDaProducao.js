// #2647 (A1): gravar insumo ou receita avisa o checklist da Produção, que relê sem trocar de aba.
const ouvintes = new Set()

export function avisarMudancaNaProducao() {
  ouvintes.forEach((ouvinte) => {
    try { ouvinte() } catch { /* ouvinte com defeito não derruba a gravação */ }
  })
}

export function assinarMudancaNaProducao(ouvinte) {
  ouvintes.add(ouvinte)
  return () => { ouvintes.delete(ouvinte) }
}
