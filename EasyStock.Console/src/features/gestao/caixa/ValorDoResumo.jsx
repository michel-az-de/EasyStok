import { moeda } from '../../../dominio/formato'
import css from './abaCaixa.module.css'

// #2258 (F3): função (não componente) para a árvore da tela continuar enxergando o texto. Valor do resumo do caixa que ainda não chegou mostra um esqueleto, nunca "—" ou linha vazia.
export function valorDoResumo(valor) {
  if (valor != null) return moeda(valor)
  return (
    <>
      <span className={css.esqueletoValor} aria-hidden="true" />
      <span className="sr">Carregando o valor</span>
    </>
  )
}
