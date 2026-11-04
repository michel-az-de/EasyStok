import { useRef, useState } from 'react'
import { useReceitasApi } from '../../../aplicacao/useReceitasApi'
import { calcularReceita, numeroDaReceita, quantidadeDaReceita as qtd } from '../../../aplicacao/calculosReceita'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Modal } from '../../../componentes/Modal'
import { moeda } from '../../../dominio/formato'
import css from '../cardapio/abaCardapio.module.css'

export function AbaCalculadoraApi() {
  const catalogo = useReceitasApi()
  const [produtoId, setProdutoId] = useState('')
  const [quantidade, setQuantidade] = useState('10')
  const [unidade, setUnidade] = useState('Un')
  const [resultado, setResultado] = useState(null)
  const [erro, setErro] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  const enviando = useRef(false)
  const calcular = async (evento) => {
    evento.preventDefault()
    if (enviando.current) return
    enviando.current = true; setOcupado(true); setErro(null)
    try { setResultado(await calcularReceita(produtoId, numeroDaReceita(quantidade), unidade)) }
    catch (e) { setErro(e.message) }
    finally { enviando.current = false; setOcupado(false) }
  }
  if (catalogo.estado === 'carregando') return <output>Carregando as receitas…</output>
  if (catalogo.estado === 'erro') return <div role="alert">{catalogo.erro}<Botao onClick={catalogo.recarregar}>Tentar de novo</Botao></div>
  return <div className={css.aba}>
    <p className={css.descricao}>Calcule insumos e custo estimado sem alterar o estoque.</p>
    <form className={`${css.campos} ${css.formulario}`} onSubmit={calcular} aria-label="Calculadora da produção">
      <CampoSelecao rotulo="Receita" disabled={ocupado} value={produtoId} onChange={(e) => setProdutoId(e.target.value)} opcoes={[{ valor: '', rotulo: 'Escolha uma receita' }, ...catalogo.receitas.filter((r) => r.linhas > 0).map((r) => ({ valor: r.produtoId, rotulo: r.nome }))]} />
      <CampoTexto rotulo="Quantidade desejada" inputMode="decimal" disabled={ocupado} value={quantidade} onChange={(e) => setQuantidade(e.target.value)} />
      <CampoSelecao rotulo="Unidade desejada" disabled={ocupado} value={unidade} onChange={(e) => setUnidade(e.target.value)} opcoes={['Un', 'G', 'Kg', 'Ml', 'L'].map((valor) => ({ valor, rotulo: valor }))} />
      <Botao tipo="submit" variante="primario" disabled={ocupado || !produtoId || !(numeroDaReceita(quantidade) > 0)}>{ocupado ? 'Calculando…' : 'Calcular insumos e custo'}</Botao>
    </form>
    {erro && <p role="alert">{erro}</p>}
    {resultado && <Modal titulo={`Cálculo de ${resultado.produtoFinalNome}`} aoFechar={() => setResultado(null)} largura="min(1000px, calc(100vw - 24px))" rodape={<Botao onClick={() => setResultado(null)}>Fechar cálculo</Botao>}>
      <p>{qtd(resultado.quantidadeDesejada)} {resultado.unidadeDesejada} · multiplicador {qtd(resultado.fatorMultiplicador)}</p>
      <p><strong>{resultado.custoEstimadoTotal == null ? 'Custo incompleto: confira os custos dos insumos.' : `Custo estimado: ${moeda(resultado.custoEstimadoTotal)}`}</strong></p>
      <p>{resultado.tudoDisponivel ? 'Insumos disponíveis para esta simulação.' : 'Há faltas ou unidades que precisam ser conferidas.'}</p>
      <ul className={css.lista}>{resultado.insumos.map((insumo) => <li key={insumo.insumoId} className={css.linha}>
        <div className={css.resumo}><strong className={css.nome}>{insumo.insumoNome}</strong><span className={css.detalhe}>Precisa {qtd(insumo.quantidadeNecessaria)} {insumo.unidadeReceita} · saldo {qtd(insumo.saldoAtual)} {insumo.unidadeSaldo}</span>
          {insumo.aviso && <span role="status">{insumo.aviso}</span>}</div>
        <span>{insumo.conversaoFalhou ? 'Conferir unidade' : insumo.falta > 0 ? `Falta ${qtd(insumo.falta)} ${insumo.unidadeReceita}` : 'Saldo suficiente'}</span>
        <span>{insumo.custoEstimadoLinha == null ? 'Sem custo conhecido' : moeda(insumo.custoEstimadoLinha)}</span>
      </li>)}</ul>
    </Modal>}
  </div>
}
