import { useEffect, useRef } from 'react'
import { focarInicioDaTela } from './focoDaNavegacao'
import css from './navegacao.module.css'

export function NavegacaoDoConsole({ hash }) {
  const anterior = useRef(hash)
  useEffect(() => {
    if (hash === anterior.current) return
    anterior.current = hash
    focarInicioDaTela(document)
  }, [hash])
  return <a className={css.pular} data-atalho-conteudo href="#conteudo-principal" onClick={evento => {
    evento.preventDefault()
    focarInicioDaTela(document)
  }}>Pular para o conteúdo</a>
}
