import { useEffect, useRef } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { useEscape } from '../../hooks/useEscape'
import { ConteudoEntregasApi } from './ConteudoEntregasApi'
import css from './dashboard.module.css'

// Gaveta de Entregas no modo API (F04). Mesma moldura da demonstração
// (`GavetaEntregas`), com o conteúdo lido da API real.
export function GavetaEntregasApi({ aoFechar }) {
  const tituloRef = useRef(null)
  useEscape(true, () => {
    if (!document.querySelector('dialog[open], [role="menu"]')) aoFechar()
  })
  useEffect(() => { tituloRef.current?.focus() }, [])

  return (
    <>
      <button type="button" className={css.cortina} aria-label="Fechar entregas" onClick={aoFechar} />
      <aside className={css.gaveta} aria-label="Entregas de hoje">
        <header className={css.topoGaveta}>
          <Icone nome="moto" />
          <h2 tabIndex={-1} ref={tituloRef}>Entregas</h2>
          <Botao variante="secundario" icone="external-link" onClick={() => window.open('#/entregas', 'cdb-entregas', 'width=1280,height=900')}>
            Abrir em outra janela
          </Botao>
          <Botao variante="texto" onClick={aoFechar}>
            <Icone nome="fechar" /> Fechar
          </Botao>
        </header>
        <ConteudoEntregasApi />
      </aside>
    </>
  )
}
