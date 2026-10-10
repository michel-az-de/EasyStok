import { useEffect, useRef } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useEscape } from '../../hooks/useEscape'
import { horaCurta } from '../../dominio/formato'
import { ConteudoEntregas } from './ConteudoEntregas'
import css from './dashboard.module.css'

// Gaveta pela direita (seção 6, "Dois jeitos de abrir, o mesmo componente").
// Vive dentro do `AtendimentoProvider` (é o Balcão que a abre), então lê o
// estado por hook, igual a qualquer outra feature. `TelaEntregas.jsx` é o
// outro jeito de abrir: mesmo `ConteudoEntregas`, sem o Provider por perto.
export function GavetaEntregas({ aoFechar }) {
  const { conversas, agora } = useAtendimento()
  const {
    janelas, cardapio, enderecoDaCasa, minutosTrecho, minutosChegadaEntregador, minutosConferir, entregadores,
    integracoesLogistica,
  } = useCatalogo()
  const acoesContexto = useAcoes()
  const tituloRef = useRef(null)

  useEscape(true, () => {
    if (!document.querySelector('dialog[open], [role="menu"]')) aoFechar()
  })
  useEffect(() => {
    const anterior = document.activeElement
    tituloRef.current?.focus()
    return () => { if (anterior?.isConnected) anterior.focus() }
  }, [])

  // "Abrir conversa" fecha a gaveta além de selecionar: na janela própria
  // isso não existe, porque não há gaveta para fechar (`TelaEntregas.jsx`
  // monta o mesmo `acoes.selecionar` sem esse passo extra).
  const acoes = { ...acoesContexto, selecionar: (id) => { acoesContexto.selecionar(id); aoFechar() } }

  const abrirOutraJanela = () => {
    window.open('#/entregas', 'cdb-entregas', 'width=1280,height=900')
  }

  return (
    <>
      <button type="button" className={css.cortina} aria-label="Fechar entregas" onClick={aoFechar} />
      <aside className={css.gaveta} aria-label="Entregas de hoje">
        <header className={css.topoGaveta}>
          <Icone nome="moto" />
          <h2 tabIndex={-1} ref={tituloRef}>Entregas de hoje</h2>
          <span className={css.horaTopo}>{horaCurta(new Date(agora).toISOString())}</span>
          <Botao variante="secundario" icone="external-link" onClick={abrirOutraJanela}>
            Abrir em outra janela
          </Botao>
          <Botao variante="texto" onClick={aoFechar}>
            <Icone nome="fechar" /> Fechar
          </Botao>
        </header>
        <ConteudoEntregas
          conversas={conversas} janelas={janelas} agora={agora} cardapio={cardapio}
          enderecoDaCasa={enderecoDaCasa}
          entregadores={entregadores}
          integracoesLogistica={integracoesLogistica}
          constantes={{ minutosTrecho, minutosChegadaEntregador, minutosConferir }}
          acoes={acoes}
        />
      </aside>
    </>
  )
}
