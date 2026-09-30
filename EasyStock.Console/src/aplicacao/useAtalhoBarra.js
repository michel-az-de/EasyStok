import { useMemo } from 'react'
import { useAtendimento, useCatalogo } from './contextos'
import { filtrarBiblioteca, listarBiblioteca, resolverVariaveis } from '../dominio/respostas'

const LIMITE = 8

// Atalho "/" no campo de mensagem (rodada 7, pedido do dono 24/09/2026, e
// padrão de mercado nas ferramentas de atendimento: seção A de
// auditoria/pesquisa-funcional-itens-4-a-7.md). Campo com só "/" e um token
// sem espaço filtra a biblioteca inteira (pronta e automática); Enter insere
// o texto no campo, nunca envia direto. Sem realce por seta de propósito:
// ela vê a lista e clica, ou estreita a busca até sobrar uma.
//
// Mora em aplicacao/ (mesmo molde de useAvisoSonoro.js, useEspelhoDeCozinha.js):
// precisa dos contextos, e feature nenhuma importa outra feature
// (ferramentas/verificar-camadas.mjs), então não pode morar em features/respostas/.
export function useAtalhoBarra({ rascunho, conversa, aoInserir }) {
  const { respostasProntas } = useCatalogo()
  const { regras } = useAtendimento()

  const termo = /^\/(\S*)$/.exec(rascunho ?? '')?.[1] ?? null
  const aberto = termo !== null

  const itens = useMemo(
    () => listarBiblioteca({ respostasProntas, regras }),
    [respostasProntas, regras],
  )
  const filtrados = useMemo(
    () => (aberto ? filtrarBiblioteca(itens, termo).slice(0, LIMITE) : []),
    [aberto, termo, itens],
  )

  function inserir(item) {
    aoInserir(resolverVariaveis(item.texto, conversa).texto)
  }

  function aoTeclar(evento) {
    if (!aberto) return false
    if (evento.key === 'Escape') { evento.preventDefault(); aoInserir(''); return true }
    if (evento.key === 'Enter') {
      evento.preventDefault()
      if (filtrados[0]) inserir(filtrados[0])
      return true
    }
    return false
  }

  const grupos = [{
    titulo: `Atalho "/${termo}"`,
    itens: filtrados.map((item) => ({ chave: item.id, titulo: item.titulo, detalhe: item.texto })),
  }]

  return {
    aberto,
    semResultado: aberto && filtrados.length === 0,
    grupos,
    aoTeclar,
    aoEscolher: (i) => {
      const item = filtrados.find((it) => it.id === i.chave)
      if (item) inserir(item)
    },
  }
}
