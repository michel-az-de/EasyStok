// Rascunho do campo de escrever (#1577). A tecla fica no campo; o estado global (e o sessionStorage que o
// segue) recebe o texto na pausa da digitação, ou na hora em blur, envio, troca de conversa e saída da tela.
// Antes cada tecla despachava no estado global e o Balcão inteiro re-renderizava.
//
// Quem escreve de fora do campo (sugestão do agente, assistente, o limpar do envio) soma um carimbo por
// conversa em `escritasDeFora`. É pelo carimbo, não pelo texto, que o campo sabe que tem de adotar o global:
// o eco do próprio despacho não mexe no carimbo, então nunca pisa nas letras digitadas depois dele.

export const ATRASO_DO_RASCUNHO_MS = 400

const carimboDe = (estado, id) => estado.escritasDeFora?.[id] ?? 0

export const comRascunhoDeFora = (estado, id, texto) => ({
  ...estado,
  rascunhos: { ...estado.rascunhos, [id]: texto },
  escritasDeFora: { ...estado.escritasDeFora, [id]: carimboDe(estado, id) + 1 },
})

// Despacho do campo que não viu a última escrita de fora perde para ela. Texto que o global já tem
// devolve o mesmo estado, e a tela não re-renderiza.
export function comRascunhoDoCampo(estado, { id, texto, escritaVista = 0 }) {
  if (escritaVista !== carimboDe(estado, id) || (estado.rascunhos[id] ?? '') === texto) return estado
  return { ...estado, rascunhos: { ...estado.rascunhos, [id]: texto } }
}

// O campo fora do React: guarda o texto digitado, arma o atraso e decide o que vai ao estado global.
// `aoDespachar({ id, texto, escritaVista }, opcoes)` leva o texto; `assinar` avisa a cada mudança do campo.
export function criarRascunhoLocal({
  conversaId, rascunho = '', escritaDeFora = 0, aoDespachar, atrasoMs = ATRASO_DO_RASCUNHO_MS,
}) {
  let campo = { conversaId, texto: rascunho, despachado: rascunho, escritaVista: escritaDeFora }
  let atraso = null
  const ouvintes = new Set()

  const desarmar = () => {
    clearTimeout(atraso)
    atraso = null
  }
  const mudar = (texto) => {
    if (texto === campo.texto) return
    campo = { ...campo, texto }
    ouvintes.forEach((ouvinte) => ouvinte())
  }
  // `opcoes.ja`: a tela está saindo e o despacho pode não render mais (ver `descarregarRascunho`).
  const descarregar = (opcoes) => {
    desarmar()
    if (campo.texto === campo.despachado) return
    campo = { ...campo, despachado: campo.texto }
    aoDespachar({ id: campo.conversaId, texto: campo.texto, escritaVista: campo.escritaVista }, opcoes)
  }

  return {
    texto: () => campo.texto,
    assinar(ouvinte) {
      ouvintes.add(ouvinte)
      return () => ouvintes.delete(ouvinte)
    },
    // Tecla: só o campo muda; o estado global espera a pausa.
    digitar(texto) {
      mudar(texto)
      desarmar()
      atraso = setTimeout(descarregar, atrasoMs)
    },
    // Texto posto de uma vez (resposta pronta, limpar o campo): vai ao estado global na hora.
    escrever(texto) {
      mudar(texto)
      descarregar()
    },
    descarregar,
    // O global de cada render. Outra conversa: descarrega a anterior e carrega a dela. Escrita de fora:
    // vence o que estava no campo, e o atraso armado morre com ele. Global já visto não muda nada.
    ver({ conversaId: id, rascunho: doGlobal = '', escritaDeFora: carimbo = 0 }) {
      if (id === campo.conversaId && carimbo === campo.escritaVista) return
      if (id !== campo.conversaId) descarregar()
      desarmar()
      const mudou = doGlobal !== campo.texto
      campo = { conversaId: id, texto: doGlobal, despachado: doGlobal, escritaVista: carimbo }
      if (mudou) ouvintes.forEach((ouvinte) => ouvinte())
    },
  }
}
