import { useSyncExternalStore } from 'react'
import { faixaDeFalha, TEXTO_DA_FALHA } from '../infra/falhaGlobal'
import { Botao } from '../componentes/Botao'
import css from './faixaDeFalha.module.css'

export function FaixaDeFalha() {
  const { falhou } = useSyncExternalStore(faixaDeFalha.assinar, faixaDeFalha.ver)
  if (!falhou) return null
  return (
    <div role="alert" className={css.faixa}>
      <span>{TEXTO_DA_FALHA}</span>
      <Botao variante="texto" onClick={faixaDeFalha.dispensar}>Dispensar</Botao>
    </div>
  )
}
