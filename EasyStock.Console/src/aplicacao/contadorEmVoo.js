// Ações em voo (#2379): um boolean compartilhado soltava os botões quando a primeira ação terminava, com
// outra ainda viajando. `iniciar` devolve o `soltar` daquela ação; soltar duas vezes não desconta a outra.
export function criarContadorEmVoo(aoMudar = () => {}) {
  let total = 0
  return {
    iniciar() {
      total += 1
      aoMudar(total)
      let solta = false
      return () => {
        if (solta) return
        solta = true
        total -= 1
        aoMudar(total)
      }
    },
    ocupado: () => total > 0,
  }
}
