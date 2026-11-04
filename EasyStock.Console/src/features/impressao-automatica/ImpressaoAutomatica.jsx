// Impressão automática do canhoto (#2096): montada uma vez no App (modo API), em qualquer tela.
// Só faz algo no aparelho em que foi ligada em Configurações › Impressão; ali mostra quantos
// canhotos saíram sozinhos hoje.
import { useImpressaoAutomatica } from '../../aplicacao/useImpressaoAutomatica'
import { imprimirHtml } from '../../componentes/htmlNoNavegador'
import { textoDaContagem } from '../../dominio/impressaoAutomatica'
import css from './impressaoAutomatica.module.css'

const imprimirSemClique = (html, { sinal }) => imprimirHtml(html, { sinal, semAbaNova: true })

export function ImpressaoAutomatica() {
  const { ligada, nestaAba, contagem, falhas } = useImpressaoAutomatica({ imprimir: imprimirSemClique })
  if (!ligada) return null
  return (
    <output className={css.indicador} aria-live="polite">
      <span className={nestaAba ? css.ponto : `${css.ponto} ${css.espera}`} aria-hidden="true" />
      <span>
        Impressão automática ligada · {textoDaContagem(contagem)}
        {!nestaAba && ' · imprimindo em outra aba'}
        {falhas > 0 && <span className={css.falhou}> · {falhas === 1 ? '1 falhou' : `${falhas} falharam`}</span>}
      </span>
    </output>
  )
}
