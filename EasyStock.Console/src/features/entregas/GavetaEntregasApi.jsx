import { useEffect, useRef } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { useEscape } from '../../hooks/useEscape'
import { HASH_MODULO_ENTREGAS } from '../../dominio/rota'
import { JANELA_ENTREGAS } from '../../dominio/janelaAvulsa'
import { ConteudoEntregasApi } from './ConteudoEntregasApi'
import css from './dashboard.module.css'

// #1582: a rota do módulo marca "Painel de entregas" no menu da janela (o apelido `#/entregas`
// abria sem tela marcada). Popup bloqueado cai para a mesma aba, como a Cozinha já faz.
function abrirEmOutraJanela() {
  const janela = window.open(HASH_MODULO_ENTREGAS, JANELA_ENTREGAS, 'width=1280,height=900')
  if (!janela) window.location.hash = HASH_MODULO_ENTREGAS
}

// Gaveta de Entregas no modo API (F04). Mesma moldura da demonstração
// (`GavetaEntregas`), com o conteúdo lido da API real.
export function GavetaEntregasApi({ aoFechar }) {
  const tituloRef = useRef(null)
  useEscape(true, () => {
    if (!document.querySelector('dialog[open], [role="menu"]')) aoFechar()
  })
  useEffect(() => {
    const anterior = document.activeElement
    tituloRef.current?.focus()
    return () => { if (anterior?.isConnected) anterior.focus() }
  }, [])

  return (
    <>
      <button type="button" className={css.cortina} aria-label="Fechar entregas" onClick={aoFechar} />
      <aside className={css.gaveta} aria-label="Entregas de hoje">
        <header className={css.topoGaveta}>
          <Icone nome="moto" />
          <h2 tabIndex={-1} ref={tituloRef}>Entregas</h2>
          <Botao variante="secundario" icone="external-link" onClick={abrirEmOutraJanela}>
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
