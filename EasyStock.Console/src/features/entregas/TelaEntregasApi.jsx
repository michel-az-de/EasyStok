import { ConteudoEntregasApi } from './ConteudoEntregasApi'
import { AgendaEncomendasApi } from './AgendaEncomendasApi'
import css from './dashboard.module.css'

// Janela própria de Entregas no modo API (F04). Não espelha o Balcão: lê a
// API direto, como a Cozinha da F05.
export function TelaEntregasApi({ tela }) {
  const agenda = tela === 'agenda'
  return (
    <div className={`${css.pagina} ${css.paginaApi}`}>
      {/* #1870: a barra do módulo já diz Entregas e a tela aberta; o título fica para o leitor de tela. */}
      <h1 className="sr">{agenda ? 'Agenda de encomendas' : 'Entregas'}</h1>
      {agenda ? <AgendaEncomendasApi /> : <ConteudoEntregasApi />}
    </div>
  )
}
