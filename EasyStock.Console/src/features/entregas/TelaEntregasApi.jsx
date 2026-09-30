import { ConteudoEntregasApi } from './ConteudoEntregasApi'
import css from './dashboard.module.css'

// Janela própria de Entregas no modo API (F04). Não espelha o Balcão: lê a
// API direto, como a Cozinha da F05.
export function TelaEntregasApi() {
  return (
    <div className={css.pagina}>
      <header className={css.topoPagina}>
        <h1>Entregas</h1>
      </header>
      <ConteudoEntregasApi />
    </div>
  )
}
