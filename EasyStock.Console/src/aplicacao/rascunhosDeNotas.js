// Memória privada da visita. Não compartilha o armazenamento do compositor público.
//
// #2185 (F10): o rascunho também fica no sessionStorage da aba, por identidade (empresa e usuário), para o
// F5 ou o "voltar" do navegador não apagar a nota meio escrita. Sair ou trocar de conta descarta o da conta
// anterior (`descartar`), e o armazenamento que falha só tira essa rede: a memória continua valendo.
const chaveGuardada = (identidade) => `easystok:notas:v1:${identidade}`

const armazenamentoDaAba = () => {
  try { return globalThis.sessionStorage ?? null } catch { return null }
}

function lerGuardado(identidade, armazenamento) {
  try {
    const dados = JSON.parse(armazenamento?.getItem(chaveGuardada(identidade)) ?? 'null')
    if (!dados || !Array.isArray(dados.rascunhos)) return { rascunhos: [], sequencia: 0 }
    const rascunhos = dados.rascunhos
      .filter((n) => typeof n?.id === 'string' && typeof n.chave === 'string' && typeof n.texto === 'string')
      .map((n) => ({
        id: n.id, nome: String(n.nome ?? ''), chave: n.chave, texto: n.texto,
        inicio: Number.isFinite(n.inicio) ? n.inicio : n.texto.length, fim: Number.isFinite(n.fim) ? n.fim : n.texto.length,
        rolagem: 0, versao: 0, gravando: false, erro: null,
      }))
    return { rascunhos, sequencia: Number.isFinite(dados.sequencia) ? dados.sequencia : rascunhos.length }
  } catch { return { rascunhos: [], sequencia: 0 } }
}

export function criarRascunhosDeNotas(identidade, armazenamento = armazenamentoDaAba()) {
  const guardado = lerGuardado(identidade, armazenamento)
  let estado = { rascunhos: guardado.rascunhos, ativa: null }
  let sequencia = guardado.sequencia
  const ouvintes = new Set()
  const publicar = (proximo) => { estado = proximo; ouvintes.forEach((ouvinte) => ouvinte()) }
  const persistir = () => {
    try {
      if (estado.rascunhos.length === 0) { armazenamento?.removeItem(chaveGuardada(identidade)); return }
      armazenamento?.setItem(chaveGuardada(identidade), JSON.stringify({
        sequencia,
        rascunhos: estado.rascunhos.map(({ id, nome, chave, texto, inicio, fim }) => ({ id, nome, chave, texto, inicio, fim })),
      }))
    } catch { /* sem armazenamento: o rascunho segue só na memória */ }
  }
  const novaChave = (id) => `${identidade}:${id}:${++sequencia}`
  return {
    identidade,
    ler: () => estado,
    assinar(ouvinte) { ouvintes.add(ouvinte); return () => ouvintes.delete(ouvinte) },
    // #2185 (F29): proposta nova (nota sugerida pela IA) com rascunho já aberto soma ao que está escrito, em vez
    // de ser ignorada. `versao` sobe para o editor montado trocar o texto dele; a mesma proposta de novo não duplica.
    abrir(id, nome, textoInicial = '') {
      let nota = estado.rascunhos.find((item) => item.id === id)
      const proposta = textoInicial.trim()
      if (!nota) {
        nota = { id, nome, chave: novaChave(id), texto: textoInicial,
          inicio: textoInicial.length, fim: textoInicial.length, rolagem: 0, versao: 0, gravando: false, erro: null }
        estado = { ...estado, rascunhos: [...estado.rascunhos, nota] }
      } else if (proposta && !nota.texto.includes(proposta) && !nota.gravando) {
        const texto = nota.texto.trim() ? `${nota.texto.trimEnd()}\n\n${proposta}` : proposta
        Object.assign(nota, { texto, inicio: texto.length, fim: texto.length, rolagem: 0, versao: (nota.versao ?? 0) + 1 })
      }
      persistir()
      publicar({ ...estado, ativa: nota.chave })
      return nota
    },
    // Digitar não redesenha a composição. O editor guarda seu texto e o espelho da posição
    // serve ao remonte após navegar para outro módulo ou mudar o arranjo da tela.
    guardar(chave, { texto, inicio, fim, rolagem }) {
      const nota = estado.rascunhos.find((item) => item.chave === chave)
      if (nota) {
        Object.assign(nota, { texto, inicio, fim, rolagem })
        persistir()
      }
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
      persistir()
      return true
    },
    // Conta que saiu ou trocou: nada do rascunho dela fica no armazenamento da aba.
    descartar() {
      try { armazenamento?.removeItem(chaveGuardada(identidade)) } catch { /* nada a limpar */ }
    },
  }
}

let visita
export function rascunhosDeNotasDaVisita(identidade) {
  if (visita?.identidade !== identidade) {
    visita?.descartar()
    visita = criarRascunhosDeNotas(identidade)
  }
  return visita
}
