// Cartão do hall (#2070): enquanto os Primeiros passos estão pendentes, quanto da Parte 1 já foi e o caminho de volta.
// Quem compõe é o App (o hall não importa outra feature) e só para quem tem `configurarLoja`.
import { useId } from 'react'
import { PARTE_ESSENCIAL, passoDaRetomada, progressoDaParte } from '../../../dominio/configuracaoInicial'
import { hashDoPasso } from '../../../dominio/rota'
import css from './configuracaoInicial.module.css'

export function CartaoPrimeirosPassos({ estado }) {
  const id = useId()
  if (estado?.pendente !== true) return null
  const { feitos, total } = progressoDaParte(estado, PARTE_ESSENCIAL)
  return (
    <section className={css.cartaoHall} aria-labelledby={id}>
      <p id={id} className={css.textoDoCartao}>
        <strong>Primeiros passos: {feitos} de {total}</strong> · <a className={css.continuar} href={hashDoPasso(passoDaRetomada(estado))}>Continuar</a>
      </p>
    </section>
  )
}
