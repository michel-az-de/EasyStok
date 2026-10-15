import { nomeDoMeio } from '../../dominio/cobranca'
import { horaCurta, moeda } from '../../dominio/formato'
import { extratoDoPedido } from '../../dominio/pagamento'
import css from './ficha.module.css'

const quando = (ms) => {
  const iso = new Date(ms).toISOString()
  return `${new Date(ms).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', timeZone: 'America/Sao_Paulo' })} ${horaCurta(iso)}`
}

// #1839: extrato do pedido no topo da modal Pagamento: itens, frete, cada pagamento e o saldo.
export function ExtratoDoPedido({ pedido }) {
  const extrato = extratoDoPedido(pedido)
  if (!extrato) return null
  return (
    <section className={css.extrato} aria-label="Extrato do pedido">
      <h3>Extrato</h3>
      <dl>
        <div><dt>Itens</dt><dd>{moeda(extrato.itens)}</dd></div>
        {extrato.frete > 0 && <div><dt>Frete</dt><dd>{moeda(extrato.frete)}</dd></div>}
        <div className={css.extratoTotal}><dt>Total</dt><dd>{moeda(extrato.total)}</dd></div>
        {extrato.pagamentos.map((p) => (
          <div key={p.id}><dt>{p.em ? quando(p.em) : 'Pagamento'} · {nomeDoMeio(p.meio)}</dt><dd>− {moeda(p.valor)}</dd></div>
        ))}
        <div className={css.extratoTotal}><dt>{extrato.saldo > 0 ? 'A receber' : 'Quitado'}</dt><dd>{moeda(extrato.saldo)}</dd></div>
      </dl>
    </section>
  )
}
