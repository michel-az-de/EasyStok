// #3294: a faixa de indicadores de Pedidos. Quatro perguntas da dona lado a lado: quanto vendi, quanto falta receber,
// o que pede ação agora e quem voltou a comprar. Os números vêm da API sobre os filtros da lista, sem a situação; os que
// são uma situação filtram a lista ao clicar (e clicar de novo tira o filtro). Falhar aqui não derruba a lista.
import { useEffect, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Icone } from '../../../componentes/Icone'
import { moeda, plural } from '../../../dominio/formato'
import { clientesQueVoltaram, textoDoItem, variacaoDoVendido } from '../../../dominio/pedidosCadastro'
import css from './faixaDePedidos.module.css'

const ROTULO = 'Indicadores dos pedidos'
const LARGA = '(min-width: 701px)'

// No celular a faixa começa recolhida numa linha (vendido, a receber, atrasados) para a lista aparecer sem rolar; na mesa
// fica aberta. Quem abre ou fecha manda até trocar de largura.
function useLarga() {
  const [larga, definir] = useState(() => globalThis.matchMedia?.(LARGA).matches ?? true)
  useEffect(() => {
    const midia = globalThis.matchMedia?.(LARGA)
    if (!midia) return undefined
    const mudar = () => definir(midia.matches)
    midia.addEventListener('change', mudar)
    return () => midia.removeEventListener('change', mudar)
  }, [])
  return larga
}

export function FaixaDePedidos({ resumo, carregando, erro, recorte, periodo, situacao, aoFiltrar, aoTentar }) {
  const larga = useLarga()
  const [escolha, escolher] = useState({ larga, aberta: larga })
  const aberta = escolha.larga === larga ? escolha.aberta : larga
  if (!resumo) {
    return <section className={css.faixa} aria-label={ROTULO} aria-busy={carregando}>
      {erro ? <div className={css.falha} role="alert"><p>Os indicadores não carregaram. A lista abaixo continua valendo.</p><Botao onClick={aoTentar}>Tentar de novo</Botao></div>
        : <p className={css.recorte}>Calculando os indicadores…</p>}
    </section>
  }
  const variacao = variacaoDoVendido(resumo, periodo)
  const voltaram = clientesQueVoltaram(resumo)
  const maisVendidos = Array.isArray(resumo.maisVendidos) ? resumo.maisVendidos : []
  const aReceber = resumo.aReceber ?? { valor: 0, pedidos: 0 }
  const cancelados = resumo.cancelados ?? { valor: 0, pedidos: 0 }
  const filtro = { situacao, aoFiltrar }
  return <section className={css.faixa} aria-label={ROTULO} aria-busy={carregando}>
    <p className={css.recorte}>{recorte} · <strong>{plural(resumo.pedidos, 'pedido', 'pedidos')}</strong></p>
    <details className={css.detalhes} open={aberta} onToggle={(e) => { if (e.currentTarget.open !== aberta) escolher({ larga, aberta: e.currentTarget.open }) }}>
    <summary className={css.resumoMovel}>
      <span>Indicadores: {moeda(resumo.vendido)} vendido · {moeda(aReceber.valor)} a receber</span>
      {resumo.atrasados > 0 && <span className={css.perigo}>{resumo.atrasados === 1 ? '1 atrasado' : `${resumo.atrasados} atrasados`}</span>}
      <span className={css.abrir}>{aberta ? 'Fechar' : 'Ver tudo'}<Icone nome={aberta ? 'chevron-up' : 'chevron-down'} tamanho={16} /></span>
    </summary>
    <div className={css.grupos}>
      <div className={css.grupo}>
        <h3>Vendido</h3>
        <strong className={css.numero}>{moeda(resumo.vendido)}</strong>
        <small>{resumo.vendas ? `${plural(resumo.vendas, 'venda', 'vendas')} · ticket médio ${moeda(resumo.ticketMedio)}` : 'Nenhuma venda neste recorte'}</small>
        {variacao && <small className={css[variacao.tom]}>{variacao.texto}</small>}
      </div>
      <div className={css.grupo}>
        <h3>A receber</h3>
        <strong className={`${css.numero} ${aReceber.valor > 0 ? css.atencao : ''}`.trim()}>{moeda(aReceber.valor)}</strong>
        <small>{aReceber.pedidos ? plural(aReceber.pedidos, 'compra com saldo', 'compras com saldo') : 'Nenhuma compra com saldo'}</small>
        <Filtro {...filtro} valor="cancelado" quantidade={cancelados.pedidos}>
          {cancelados.pedidos === 1 ? 'cancelado' : 'cancelados'}{cancelados.valor > 0 ? ` · ${moeda(cancelados.valor)}` : ''}
        </Filtro>
      </div>
      <div className={css.grupo}>
        <h3>Pede ação agora</h3>
        <ul className={css.acao}>
          <li><Filtro {...filtro} valor="aguardando_pagamento" quantidade={resumo.aguardandoPagamento} tom="atencao">aguardando pagamento</Filtro></li>
          <li><Filtro {...filtro} valor="aguardando_aprovacao_baba" quantidade={resumo.aguardandoAprovacao} tom="atencao">aguardando aprovação</Filtro></li>
          <li className={resumo.atrasados > 0 ? css.perigo : css.zero}><span className={css.n}>{resumo.atrasados}</span> {resumo.atrasados === 1 ? 'atrasado' : 'atrasados'}</li>
          <li className={resumo.emAndamento > 0 ? undefined : css.zero}><span className={css.n}>{resumo.emAndamento}</span> na cozinha ou na rua</li>
        </ul>
      </div>
      <div className={css.grupo}>
        <h3>Clientes e itens</h3>
        {voltaram ? <>
          <strong className={css.numero}>{voltaram.percentual}%</strong>
          <small>voltaram: {voltaram.voltaram} de {voltaram.voltaram + voltaram.primeira} compras · {voltaram.primeira} na 1ª compra</small>
        </> : <small>Nenhuma compra com cliente neste recorte</small>}
        {maisVendidos.length > 0 && <small className={css.itens}>Mais pedidos: {maisVendidos.map(textoDoItem).join(', ')}</small>}
      </div>
    </div>
    </details>
    <p className={css.legenda}>Vendido e ticket contam só compras: sem cancelado, rascunho e aguardando pagamento. A receber é o saldo das compras ainda não quitadas; o Financeiro conta pelo vencimento. Estes números não mudam com o filtro de situação.</p>
  </section>
}

// Número que é situação: filtra a lista. Zero não filtra (a lista viria vazia), salvo para tirar o filtro já ligado.
function Filtro({ valor, situacao, aoFiltrar, quantidade = 0, tom, children }) {
  const ligado = situacao === valor
  const classes = [css.filtro, quantidade > 0 && tom ? css[tom] : '', quantidade > 0 ? '' : css.zero].filter(Boolean).join(' ')
  return <button type="button" className={classes} aria-pressed={ligado} disabled={!quantidade && !ligado}
    title={ligado ? 'Mostrar todas as situações' : 'Mostrar só estes na lista'} onClick={() => aoFiltrar(ligado ? '' : valor)}>
    <span className={css.n}>{quantidade}</span> {children}
  </button>
}
