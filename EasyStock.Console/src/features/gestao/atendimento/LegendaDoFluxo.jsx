import { useId, useState } from 'react'
import css from './legendaDoFluxo.module.css'

// #2857 (I3): o que cada linha do desenho quer dizer. A amostra repete o traço, a cor e o rótulo da linha de verdade.
const LINHAS_DA_LEGENDA = [
  { chave: 'turno', cor: 'var(--linha-media)', espessura: 2, texto: 'Segue o turno' },
  { chave: 'barra', cor: 'var(--marca)', espessura: 2.5, rotulo: 'barra', texto: 'Barra a conversa (equipe)' },
  { chave: 'ajuda', cor: 'var(--marca)', espessura: 2.5, rotulo: 'pede ajuda', texto: 'Pede ajuda (equipe)' },
  { chave: 'usa', cor: 'var(--linha-media)', espessura: 2, tracejada: true, texto: 'Consulta ou usa' },
]

export function LegendaDoFluxo() {
  const [aberta, setAberta] = useState(false)
  const idLista = useId()
  return (
    <div className={`${css.legenda} nodrag nopan`}>
      <button type="button" data-legenda className={css.legendaBotao} aria-expanded={aberta} aria-controls={idLista} onClick={() => setAberta(!aberta)}>Legenda</button>
      {aberta && (
        <ul id={idLista} className={css.legendaLista}>
          {LINHAS_DA_LEGENDA.map((l) => (
            <li key={l.chave}>
              <svg width="48" height="16" viewBox="0 0 48 16" aria-hidden="true" focusable="false">
                <line x1="2" y1="8" x2="38" y2="8" style={{ stroke: l.cor, strokeWidth: l.espessura, strokeDasharray: l.tracejada ? '5 4' : undefined }} />
                <polygon points="36,3.5 46,8 36,12.5" style={{ fill: l.cor }} />
              </svg>
              <span>{l.rotulo && <b className={css.legendaRotulo}>{l.rotulo}</b>}{l.texto}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
