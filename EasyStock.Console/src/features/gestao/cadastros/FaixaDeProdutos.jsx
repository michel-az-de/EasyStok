// #3299: a faixa de Produtos. Quatro perguntas da dona lado a lado: quanto os produtos venderam, o que concentra a
// receita, o que pede atenção e o que falta no cadastro. Os números vêm da API (a faixa é a soma das linhas) e a base
// pequena é dita na tela. Falhar aqui não derruba a lista; no celular a faixa começa recolhida numa linha.
import { useEffect, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Icone } from '../../../componentes/Icone'
import { moedaCheia, plural } from '../../../dominio/formato'
import { numero, variacaoDaReceita } from '../../../dominio/desempenhoDoProduto'
import css from './faixaDeProdutos.module.css'

const ROTULO = 'Números dos produtos'
const LARGA = '(min-width: 701px)'

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

// #3303: contador com produto vira filtro da lista; apertado de novo, solta.
const Linha = ({ n, tom, children, sinal, atencao, aoFiltrar }) => <li className={n > 0 ? css[tom] : css.zero}>
  {n > 0 && sinal && aoFiltrar
    ? <button type="button" className={css.filtro} aria-pressed={atencao === sinal} onClick={() => aoFiltrar(atencao === sinal ? '' : sinal)}><span className={css.n}>{n}</span> {children}</button>
    : <><span className={css.n}>{n}</span> {children}</>}
</li>

export function FaixaDeProdutos({ desempenho, carregando, erro, aoTentar, atencao = '', aoFiltrar }) {
  const filtro = { atencao, aoFiltrar }
  const larga = useLarga()
  const [escolha, escolher] = useState({ larga, aberta: larga })
  const aberta = escolha.larga === larga ? escolha.aberta : larga
  const r = desempenho?.resumo
  if (!r) {
    return <section className={css.faixa} aria-label={ROTULO} aria-busy={carregando}>
      {erro ? <div className={css.falha} role="alert"><p>Os números dos produtos não carregaram. A lista abaixo continua valendo.</p><Botao onClick={aoTentar}>Tentar de novo</Botao></div>
        : <p className={css.recorte}>Calculando as vendas dos produtos…</p>}
    </section>
  }
  const variacao = variacaoDaReceita(r.receita, r.receitaAnterior)
  const curva = r.curva ?? {}
  const maisVendidos = Array.isArray(r.maisVendidos) ? r.maisVendidos : []
  const pedemAtencao = (r.parados ?? 0) + (r.nuncaVenderam ?? 0) + (r.abaixoDaTabela ?? 0)
  return <section className={css.faixa} aria-label={ROTULO} aria-busy={carregando}>
    <p className={css.recorte}>Últimos 90 dias · <strong>{plural(r.compras ?? 0, 'pedido', 'pedidos')}</strong> com {plural(r.produtosVendidos ?? 0, 'produto', 'produtos')} de {r.ativos ?? 0} ativos</p>
    <details className={css.detalhes} open={aberta} onToggle={(e) => { if (e.currentTarget.open !== aberta) escolher({ larga, aberta: e.currentTarget.open }) }}>
      <summary className={css.resumoMovel}>
        <span>{moedaCheia(r.receita)} em 90 dias</span>
        {pedemAtencao > 0 && <span className={css.perigo}>{plural(pedemAtencao, 'pede atenção', 'pedem atenção')}</span>}
        <span className={css.abrir}>{aberta ? 'Fechar' : 'Ver tudo'}<Icone nome={aberta ? 'chevron-up' : 'chevron-down'} tamanho={16} /></span>
      </summary>
      <div className={css.grupos}>
        <div className={css.grupo}>
          <h3>Vendido em produtos</h3>
          <strong className={css.numero}>{moedaCheia(r.receita)}</strong>
          {variacao ? <small className={css[variacao.tom]}>{variacao.texto}</small> : <small>Nenhuma venda nos últimos 90 dias</small>}
          {r.receitaSemProduto > 0 && <small>mais {moedaCheia(r.receitaSemProduto)} em itens sem cadastro</small>}
        </div>
        <div className={css.grupo}>
          <h3>Curva A do ano{curva.previa ? ' (prévia)' : ''}</h3>
          {curva.produtosA > 0 ? <>
            <strong className={css.numero}>{plural(curva.produtosA, 'produto', 'produtos')}</strong>
            <small>fazem {numero(curva.fatiaA)}% da receita do ano{curva.previa ? ` · base de ${plural(curva.comprasNoAno ?? 0, 'pedido', 'pedidos')}` : ''}</small>
          </> : <small>Sem venda no ano para classificar</small>}
          {maisVendidos.length > 0 && <small className={css.itens}>Mais vendidos em 90 dias: {maisVendidos.map(m => m.nome).join(', ')}</small>}
        </div>
        <div className={css.grupo}>
          <h3>Pede atenção</h3>
          <ul className={css.lista}>
            <Linha n={r.parados ?? 0} tom="perigo" sinal="Parado" {...filtro}>{(r.parados ?? 0) === 1 ? 'parado' : 'parados'}</Linha>
            <Linha n={r.nuncaVenderam ?? 0} tom="atencao" sinal="NuncaVendeu" {...filtro}>nunca {(r.nuncaVenderam ?? 0) === 1 ? 'vendeu' : 'venderam'}</Linha>
            <Linha n={r.abaixoDaTabela ?? 0} tom="atencao" sinal="AbaixoDaTabela" {...filtro}>vendido abaixo da tabela</Linha>
          </ul>
        </div>
        <div className={css.grupo}>
          <h3>Falta no cadastro</h3>
          <ul className={css.lista}>
            <Linha n={r.semCusto ?? 0} tom="atencao" sinal="SemCusto" {...filtro}>sem custo, sem margem</Linha>
            <Linha n={r.semPreco ?? 0} tom="atencao">sem preço</Linha>
          </ul>
          <small>de {plural(r.ativos ?? 0, 'produto ativo', 'produtos ativos')} de venda</small>
        </div>
      </div>
    </details>
    <details className={css.legenda}><summary>Como contamos</summary><p>Conta o pedido que virou venda: fora cancelado, rascunho e pagamento pendente, pelo dia de Brasília. Vendas e previsão olham 90 dias; curva e preço olham o ano, e com menos de 100 pedidos a curva é prévia. Parado: vendeu em 3 pedidos ou mais e está há mais de 60 dias sem vender, o dobro do seu intervalo comum. Insumo e embalagem ficam fora.</p></details>
  </section>
}
