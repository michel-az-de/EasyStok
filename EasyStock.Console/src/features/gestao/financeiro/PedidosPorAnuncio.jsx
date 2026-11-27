import { useCallback, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Vazio } from '../../../componentes/Vazio'
import { EsqueletoDeLista } from '../../../componentes/Esqueleto'
import { useConsultaFinanceira, obterPedidosPorAnuncio } from '../../../aplicacao/useFinanceiroApi'
import { nomeDaPlataforma } from '../../../dominio/pedidosPorAnuncio'
import { mesFinanceiro } from '../../../dominio/financeiro'
import { moeda } from '../../../dominio/formato'
import css from './financeiro.module.css'

// #2928 (S5): de qual anúncio vieram os pedidos. Tabela simples, sem gráfico; pedido cancelado e rascunho ficam de fora.
export function PedidosPorAnuncio() {
  const [rascunho, setRascunho] = useState(mesFinanceiro)
  const [periodo, setPeriodo] = useState(rascunho)
  const carregar = useCallback(() => obterPedidosPorAnuncio(periodo), [periodo])
  const consulta = useConsultaFinanceira(carregar)
  const linhas = consulta.dados ?? []
  return <section className={`${css.painel} ${css.secaoSeguinte}`} aria-label="Pedidos por anúncio">
    <h2>Pedidos por anúncio</h2>
    <form className={css.filtros} onSubmit={(e) => { e.preventDefault(); setPeriodo(rascunho) }}>
      <CampoTexto rotulo="De" tipo="date" required value={rascunho.inicio} onChange={(e) => setRascunho({ ...rascunho, inicio: e.target.value })} />
      <CampoTexto rotulo="Até" tipo="date" required min={rascunho.inicio} value={rascunho.fim} onChange={(e) => setRascunho({ ...rascunho, fim: e.target.value })} />
      <Botao tipo="submit">Consultar anúncios</Botao>
    </form>
    {consulta.erro && <div className={css.painel}><p role="alert">{consulta.erro}</p><Botao icone="refresh-cw" onClick={consulta.recarregar}>Tentar de novo</Botao></div>}
    {consulta.carregando && !consulta.dados && <EsqueletoDeLista linhas={3} altura="var(--toque)">Carregando anúncios…</EsqueletoDeLista>}
    {consulta.dados && (linhas.length
      ? <div className={css.tabelaRolavel} aria-busy={consulta.carregando}><table className={css.tabela}>
        <caption className="sr">Pedidos por plataforma e campanha de {periodo.inicio} a {periodo.fim}</caption>
        <thead><tr>{['Plataforma', 'Campanha', 'Pedidos', 'Valor', 'Ticket médio'].map((nome) => <th scope="col" key={nome}>{nome}</th>)}</tr></thead>
        <tbody>{linhas.map((linha) => <tr key={`${linha.plataforma}|${linha.campanha}`}>
          <th scope="row">{nomeDaPlataforma(linha.plataforma)}</th><td>{linha.campanha}</td><td>{linha.totalPedidos}</td><td>{moeda(linha.valorTotal)}</td><td>{moeda(linha.ticketMedio)}</td>
        </tr>)}</tbody></table></div>
      : <Vazio titulo="Sem pedidos no período">Escolha outro período para ver de qual anúncio vieram os pedidos.</Vazio>)}
  </section>
}
