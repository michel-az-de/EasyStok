// Memória privada da visita. Não compartilha o armazenamento do compositor público.
export function criarRascunhosDeNotas(identidade) {
  let estado = { rascunhos: [], ativa: null }
  let sequencia = 0
  const ouvintes = new Set()
  const publicar = (proximo) => { estado = proximo; ouvintes.forEach((ouvinte) => ouvinte()) }
  return {
    identidade,
    ler: () => estado,
    assinar(ouvinte) { ouvintes.add(ouvinte); return () => ouvintes.delete(ouvinte) },
    abrir(id, nome, textoInicial = '') {
      let nota = estado.rascunhos.find((item) => item.id === id)
      if (!nota) {
        nota = { id, nome, chave: `${identidade}:${id}:${++sequencia}`, texto: textoInicial,
          inicio: textoInicial.length, fim: textoInicial.length, rolagem: 0, gravando: false, erro: null }
        estado = { ...estado, rascunhos: [...estado.rascunhos, nota] }
      }
      publicar({ ...estado, ativa: nota.chave })
      return nota
    },
    // Digitar não redesenha a composição. O editor guarda seu texto e o espelho da posição
    // serve ao remonte após navegar para outro módulo ou mudar o arranjo da tela.
    guardar(chave, { texto, inicio, fim, rolagem }) {
      const nota = estado.rascunhos.find((item) => item.chave === chave)
      if (nota) Object.assign(nota, { texto, inicio, fim, rolagem })
    },
    pausar() { if (estado.ativa) publicar({ ...estado, ativa: null }) },
    iniciarGravacao(chave) {
      const nota = estado.rascunhos.find((item) => item.chave === chave)
      if (!nota || nota.gravando) return false
      Object.assign(nota, { gravando: true, erro: null })
      publicar({ ...estado })
      return true
    },
    falharGravacao(chave, erro) {
      const nota = estado.rascunhos.find((item) => item.chave === chave)
      if (nota) { Object.assign(nota, { gravando: false, erro }); publicar({ ...estado }) }
    },
    concluir(chave, confirmado) {
      if (confirmado !== true || !estado.rascunhos.some((item) => item.chave === chave)) return false
      publicar({ rascunhos: estado.rascunhos.filter((item) => item.chave !== chave),
        ativa: estado.ativa === chave ? null : estado.ativa })
      return true
    },
  }
}

let visita
export function rascunhosDeNotasDaVisita(identidade) {
  if (visita?.identidade !== identidade) visita = criarRascunhosDeNotas(identidade)
  return visita
}
