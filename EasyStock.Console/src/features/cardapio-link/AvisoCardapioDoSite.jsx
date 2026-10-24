import { Icone } from '../../componentes/Icone'
import css from './cardapioLink.module.css'

// #1241 (F11, S48): no modo API o cardápio do cliente é a página do site. O link que vai pelo
// WhatsApp abre a vitrine da Casa da Baba, e o pedido feito lá entra no EasyStok e aparece na
// conversa (pedido e mensagem do sistema) na sincronização seguinte. Esta tela era a simulação
// local dessa página; no modo API ela só explica onde está o real, em vez de falhar.
export function AvisoCardapioDoSite() {
  return (
    <PaginaAviso titulo="O cardápio do cliente agora é o do site">
      O link que o cliente recebe pelo WhatsApp abre a página da Casa da Baba. O pedido feito lá
      aparece sozinho na conversa, com o resumo. Gere o link pela conversa, no botão do cardápio.
    </PaginaAviso>
  )
}

export function PaginaAviso({ titulo, children }) {
  return (
    <div className={css.pagina}>
      <div className={css.avisoCartao}>
        <Icone nome="cardapio" tamanho={32} />
        <h1>{titulo}</h1>
        <p>{children}</p>
      </div>
    </div>
  )
}
