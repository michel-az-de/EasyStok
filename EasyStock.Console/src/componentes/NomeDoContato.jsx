import { useLayoutEffect, useRef } from 'react'
import css from './NomeDoContato.module.css'

// #2258 (auditoria de UX, T4 e T5): o nome do contato com a cápsula que explica o que falta nele
// ("Completar nome", "Sem nome", "#6"). Recebe o que `apresentacaoDoNome` (dominio/nomeDoContato.js)
// devolve. Com largura, mostra o nome inteiro; sem ela, a forma curta ("Thais R."), nunca o corte
// no meio da palavra. O nome inteiro segue no `title` e no texto do leitor de tela.
//
// A largura não se decide só com CSS: o nome inteiro é medido (`scrollWidth` contra `clientWidth`)
// no layout e de novo quando o contêiner muda de tamanho, e o resultado vai direto para o
// `data-abreviado` do elemento, sem renderizar de novo.

function useAbreviacao(ativo, completo, curto) {
  const raiz = useRef(null)
  useLayoutEffect(() => {
    const elemento = raiz.current
    if (!elemento) return undefined
    // #2258 (banca a11y): sem forma curta não há o que abreviar; o atributo de outra conversa não fica.
    if (!ativo) { elemento.removeAttribute('data-abreviado'); return undefined }
    const medir = () => {
      const inteiro = elemento.firstElementChild
      elemento.setAttribute('data-abreviado', 'false')
      if (inteiro && inteiro.scrollWidth > inteiro.clientWidth) elemento.setAttribute('data-abreviado', 'true')
    }
    medir()
    if (typeof ResizeObserver === 'undefined') return undefined
    const observador = new ResizeObserver(medir)
    observador.observe(elemento.parentElement ?? elemento)
    return () => observador.disconnect()
  }, [ativo, completo, curto])
  return raiz
}

export function NomeDoContato({ completo, curto, capsula, titulo, complemento = null, aoCompletar }) {
  const temCurto = Boolean(curto) && curto !== completo
  const raiz = useAbreviacao(temCurto, completo, curto)
  const comAcao = capsula?.acao === 'completar-nome' && typeof aoCompletar === 'function'
  const classeDaCapsula = capsula ? `${css.capsula} ${css[`capsula_${capsula.tom}`] ?? css.capsula_neutro}` : ''
  return (
    <span ref={raiz} className={css.nome} title={titulo ?? completo}>
      <span className={css.completo}>{completo}</span>
      {temCurto && <span className={css.curto} aria-hidden="true">{curto}</span>}
      {complemento && <span className={css.complemento}>{complemento}</span>}
      {capsula && (comAcao
        ? <button type="button" className={classeDaCapsula} onClick={aoCompletar}>{capsula.texto}</button>
        : <span className={classeDaCapsula}>{capsula.texto}</span>)}
    </span>
  )
}
