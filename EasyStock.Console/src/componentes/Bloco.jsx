import { useId } from 'react'
import { useRecolhido } from '../hooks/useRecolhido'
import { Icone } from './Icone'
import css from './Bloco.module.css'

// Seção titulada de painel. Existe para o título ficar consistente em todo lugar.
//
// #1442 (homologação de 07/10, "reduzir para a barra de rolagem não ficar
// extensa"): com `chave`, a seção recolhe pelo título e o navegador lembra a
// escolha. Recolhida, mostra o `resumo` ao lado do título (um número, um
// telefone) para ela não precisar abrir só para conferir.
export function Bloco({ titulo, destaque = false, chave, resumo, children }) {
  const [recolhido, alternar] = useRecolhido(chave)
  const idConteudo = useId()
  const classes = `${css.bloco} ${destaque ? css.destaque : ''}`

  if (!chave) {
    return (
      <section className={classes}>
        <h3>{titulo}</h3>
        {children}
      </section>
    )
  }

  return (
    <section className={`${classes} ${recolhido ? css.recolhido : ''}`}>
      <h3>
        <button
          type="button"
          className={css.gatilho}
          aria-expanded={!recolhido}
          aria-controls={idConteudo}
          onClick={alternar}
        >
          <span>{titulo}</span>
          {recolhido && resumo != null && resumo !== '' && <span className={css.resumo}>{resumo}</span>}
          <Icone nome="chevron-right" tamanho={16} className={recolhido ? css.seta : `${css.seta} ${css.setaAberta}`} />
        </button>
      </h3>
      <div id={idConteudo} hidden={recolhido}>
        {children}
      </div>
    </section>
  )
}
