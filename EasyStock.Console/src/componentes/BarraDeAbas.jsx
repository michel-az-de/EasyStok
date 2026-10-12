import { useState } from 'react'
import { Icone } from './Icone'
import css from './BarraDeAbas.module.css'

// Ícone de cada aba (banca estética, item 6): mesmo desenho do trilho do desktop.
const ICONE_DA_ABA = { balcao: 'inbox', atendimento: 'conversa', ficha: 'ficha' }

// Navegação entre painéis quando a tela não comporta as três colunas.
export function BarraDeAbas({ abas, ativa, aoTrocar, idPainel = 'painel-da-aba', prefixoId = 'aba', desabilitada = false }) {
  const [focada, setFocada] = useState(null)
  const entrada = abas.some((aba) => aba.id === focada) ? focada : ativa

  const navegar = (evento, indice) => {
    if (evento.altKey || evento.ctrlKey || evento.metaKey) return
    let destino
    switch (evento.key) {
      case 'ArrowRight': destino = (indice + 1) % abas.length; break
      case 'ArrowLeft': destino = (indice - 1 + abas.length) % abas.length; break
      case 'Home': destino = 0; break
      case 'End': destino = abas.length - 1; break
      default: return
    }
    evento.preventDefault()
    evento.currentTarget.parentElement.querySelectorAll('[role="tab"]')[destino]?.focus()
  }

  return (
    <nav className={css.barra} aria-label="Painéis">
      {/* role tab mais aria-selected: leitor de tela anuncia "aba 2 de 3,
          selecionada", que é o que a barra realmente é. */}
      <div className={css.trilho} role="tablist" aria-label="Painéis"
        onBlur={(evento) => { if (!evento.currentTarget.contains(evento.relatedTarget)) setFocada(null) }}>
        {abas.map((aba, indice) => (
          <button
            type="button"
            key={aba.id}
            role="tab"
            disabled={desabilitada}
            id={`${prefixoId}-${aba.id}`}
            aria-selected={aba.id === ativa}
            aria-controls={idPainel}
            tabIndex={aba.id === entrada ? 0 : -1}
            className={`${css.aba} ${aba.id === ativa ? css.ativa : ''}`}
            onClick={() => aoTrocar(aba.id)}
            onFocus={() => setFocada(aba.id)}
            onKeyDown={(evento) => navegar(evento, indice)}
          >
            {ICONE_DA_ABA[aba.id] && <Icone nome={ICONE_DA_ABA[aba.id]} tamanho={24} />}
            <span>{aba.rotulo}</span>
            {aba.contador != null && <span className={css.contador}>{aba.contador}</span>}
          </button>
        ))}
      </div>
    </nav>
  )
}
