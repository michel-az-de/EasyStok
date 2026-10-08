import { useMemo, useState } from 'react'
import { useCatalogo } from './contextos'
import { moverDestaque, resolverVariaveis, respostasDoSeletor, termoDaBarra } from '../dominio/respostas'

// Seletor rápido de respostas do compositor (#1441, homologação de 07/10, comparando
// com o Lumina: "uma resposta rápida e já bem direta do que tem que ser feito").
// Abre de dois jeitos: "/" no começo do campo (o resto do token é a busca, o foco
// fica no campo) ou o botão Respostas (busca própria no topo da lista). Setas
// escolhem, Enter insere no campo, Esc fecha. Nunca envia direto: a atendente vê
// o texto antes. Gestão (criar, editar, arquivar) fica na Gestão, fora daqui.
//
// Mora em aplicacao/ porque precisa dos contextos e feature nenhuma importa outra
// feature (ferramentas/verificar-camadas.mjs).
export function useAtalhoBarra({ rascunho, conversa, aoInserir }) {
  const { respostasProntas } = useCatalogo()
  const termoBarra = termoDaBarra(rascunho)
  const pelaBarra = termoBarra !== null
  // A busca aberta pelo botão vale só para a conversa em que abriu; o destaque, só
  // para o termo em que foi escolhido (termo novo volta ao primeiro da lista).
  const [aberturaPeloBotao, setAberturaPeloBotao] = useState(null)
  const busca = aberturaPeloBotao?.conversaId === conversa?.id ? aberturaPeloBotao.texto : null
  const setBusca = (texto) => setAberturaPeloBotao(texto === null ? null : { conversaId: conversa?.id, texto })

  const aberto = pelaBarra || busca !== null
  const termo = termoBarra ?? busca ?? ''
  const [escolha, setEscolha] = useState({ termo: '', indice: 0 })
  const destaque = escolha.termo === termo ? escolha.indice : 0
  const setDestaque = (proximo) => setEscolha({ termo, indice: typeof proximo === 'function' ? proximo(destaque) : proximo })

  const itens = useMemo(
    () => (aberto ? respostasDoSeletor({ respostasProntas, termo }) : []),
    [aberto, termo, respostasProntas],
  )
  const temRespostas = useMemo(() => (respostasProntas ?? []).some((r) => !r.arquivada), [respostasProntas])

  function fechar() {
    setBusca(null)
    if (pelaBarra) aoInserir('')
  }

  // Pela barra, o texto substitui o "/termo"; pelo botão, entra depois do que já estava escrito.
  function inserir(item) {
    const texto = resolverVariaveis(item.texto, conversa).texto
    const antes = pelaBarra ? '' : (rascunho ?? '').trimEnd()
    aoInserir(antes ? `${antes} ${texto}` : texto)
    setBusca(null)
  }

  function aoTeclar(evento) {
    if (!aberto) return false
    if (evento.key === 'ArrowDown' || evento.key === 'ArrowUp') {
      evento.preventDefault()
      setDestaque((i) => moverDestaque(i, evento.key === 'ArrowDown' ? 1 : -1, itens.length))
      return true
    }
    if (evento.key === 'Enter' && !evento.shiftKey) {
      evento.preventDefault()
      if (itens[destaque]) inserir(itens[destaque])
      return true
    }
    if (evento.key === 'Escape') {
      evento.preventDefault()
      fechar()
      return true
    }
    return false
  }

  return {
    aberto,
    pelaBarra,
    busca: busca ?? '',
    mudarBusca: setBusca,
    itens,
    temRespostas,
    destaque,
    destacar: setDestaque,
    abrir: () => setBusca(''),
    fechar,
    inserir,
    aoTeclar,
  }
}
