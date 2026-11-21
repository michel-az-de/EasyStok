import { useEffect, useRef } from 'react'

// #2792: a tabela de Cadastros e a de Pedidos não rolam mais por conta própria (era uma segunda rolagem dentro
// da página). Quem rola é o contêiner da página, o primeiro ancestral com rolagem vertical; é nele que a posição
// é lida e devolvida ao voltar do editor.
const rolavelAcima = (no) => {
  for (let pai = no?.parentElement; pai; pai = pai.parentElement) {
    const rolagem = getComputedStyle(pai).overflowY
    if (rolagem === 'auto' || rolagem === 'scroll') return pai
  }
  return null
}

// `pronto` devolve a posição guardada quando a leitura termina; `comLista` religa o ouvinte quando a tabela entra ou sai.
export function useRolagemDaLista(tabela, { posicao, aoRolar, pronto, comLista }) {
  const ultimo = useRef(aoRolar)
  useEffect(() => { ultimo.current = aoRolar })
  useEffect(() => {
    const rolavel = rolavelAcima(tabela.current)
    if (rolavel && pronto) rolavel.scrollTop = posicao ?? 0
  }, [pronto])
  useEffect(() => {
    const rolavel = rolavelAcima(tabela.current)
    if (!rolavel) return undefined
    const aoMudar = () => ultimo.current(rolavel.scrollTop)
    rolavel.addEventListener('scroll', aoMudar, { passive: true })
    return () => rolavel.removeEventListener('scroll', aoMudar)
  }, [comLista])
}
