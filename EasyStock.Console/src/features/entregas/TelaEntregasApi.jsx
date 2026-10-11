import { ConteudoEntregasApi } from './ConteudoEntregasApi'
import { AgendaEncomendasApi } from './AgendaEncomendasApi'
import css from './dashboard.module.css'

// Janela própria de Entregas no modo API (F04). Não espelha o Balcão: lê a
// API direto, como a Cozinha da F05.
export function TelaEntregasApi({ tela }) {
  const agenda = tela === 'agenda'
  return (
    <div className={`${css.pagina} ${css.paginaApi}`}>
      <header className={css.topoPagina}>
        <h1>{agenda ? 'Agenda de encomendas' : 'Entregas'}</h1>
      </header>
      {agenda ? <AgendaEncomendasApi /> : <ConteudoEntregasApi />}
    </div>
  )
}
