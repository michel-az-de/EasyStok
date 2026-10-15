import { useResumoDoCliente } from '../../aplicacao/useResumoDoCliente'
import { moeda } from '../../dominio/formato'
import css from './ficha.module.css'

// #1839: o que se vê sempre no Resumo, sem abrir nada: quem é o cliente (resumo da IA), quanto já
// comprou no total, o que costuma pedir, as tags e a última nota interna como post-it. O resto
// (endereço, histórico, conversas) segue em "Cliente e histórico".
export function CartaoDoCliente({ conversa, fonteApi, aoAbrir }) {
  const { cliente } = conversa
  const lead = conversa.conta === 'lead' || !conversa.clienteId
  const resumo = useResumoDoCliente(conversa.id, fonteApi && !lead)
  const pedidos = cliente.pedidos ?? 0
  const numeros = pedidos > 0
    ? [`${pedidos} ${pedidos === 1 ? 'pedido' : 'pedidos'} no total`,
      cliente.totalGasto > 0 ? `${moeda(cliente.totalGasto)} em compras` : null,
      cliente.favorito ? `costuma pedir ${cliente.favorito}` : null].filter(Boolean).join(' · ')
    : lead ? 'Lead: ainda sem cadastro' : 'Ainda não comprou com a casa'
  const nota = cliente.notas?.[0] ?? null
  const tags = (cliente.tags ?? []).slice(0, 4)
  // Lead sem tag nem nota: o Resumo já diz "primeiro o cadastro"; o cartão só repetiria.
  if (lead && !nota && tags.length === 0) return null
  return (
    <section className={css.cartaoCliente} aria-label={`Cliente ${conversa.nome}`}>
      <button type="button" className={css.cartaoClienteAbrir} onClick={aoAbrir}>
        {resumo.texto && <p className={css.resumoIa}>{resumo.texto}</p>}
        {resumo.carregando && <p className={css.resumoIaCarregando}>Lendo o histórico do cliente…</p>}
        <p className={css.numerosCliente}>{numeros}</p>
      </button>
      {tags.length > 0 && (
        <ul className={css.tagsCliente} aria-label="Tags do cliente">
          {tags.map((t) => <li key={t}>{t}</li>)}
        </ul>
      )}
      {nota && (
        <p className={css.postitResumo}>
          <span className={css.rotuloPostit}>Nota{nota.autor ? ` · ${nota.autor}` : ''}</span>
          {nota.texto}
        </p>
      )}
    </section>
  )
}
