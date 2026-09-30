import { Icone } from './Icone'
import css from './BarraDeAbas.module.css'

// Ícone de cada aba (banca estética, item 6): mesmo desenho do trilho do desktop.
const ICONE_DA_ABA = { balcao: 'inbox', atendimento: 'conversa', ficha: 'ficha' }

// Navegação entre painéis quando a tela não comporta as três colunas.
export function BarraDeAbas({ abas, ativa, aoTrocar }) {
  return (
    <nav className={css.barra} aria-label="Painéis">
      {/* role tab mais aria-selected: leitor de tela anuncia "aba 2 de 3,
          selecionada", que é o que a barra realmente é. */}
      <div className={css.trilho} role="tablist" aria-label="Painéis">
        {abas.map((aba) => (
          <button
            type="button"
            key={aba.id}
            role="tab"
            id={`aba-${aba.id}`}
            aria-selected={aba.id === ativa}
            aria-controls="painel-da-aba"
            className={`${css.aba} ${aba.id === ativa ? css.ativa : ''}`}
            onClick={() => aoTrocar(aba.id)}
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
