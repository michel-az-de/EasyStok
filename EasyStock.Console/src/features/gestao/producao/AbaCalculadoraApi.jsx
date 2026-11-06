import { useRef, useState } from 'react'
import { useReceitasApi } from '../../../aplicacao/useReceitasApi'
import { calcularReceita, numeroDaReceita, quantidadeDaReceita as qtd } from '../../../aplicacao/calculosReceita'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Modal } from '../../../componentes/Modal'
import { Vazio } from '../../../componentes/Vazio'
import { hashDoModulo } from '../../../dominio/modulos'
import { moeda } from '../../../dominio/formato'
import css from '../cardapio/abaCardapio.module.css'

// #2235 (banca C3): unidade por extenso; "Un" é sigla do sistema.
const UNIDADES = [
  { valor: 'Un', rotulo: 'unidades (porções)' }, { valor: 'G', rotulo: 'gramas' }, { valor: 'Kg', rotulo: 'quilos' },
  { valor: 'Ml', rotulo: 'mililitros' }, { valor: 'L', rotulo: 'litros' },
]

export function AbaCalculadoraApi() {
  const catalogo = useReceitasApi()
  const [produtoId, setProdutoId] = useState('')
  const [quantidade, setQuantidade] = useState('')
  const [tentou, setTentou] = useState(false)
  const [unidade, setUnidade] = useState('Un')
  const [resultado, setResultado] = useState(null)
  const [erro, setErro] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  const enviando = useRef(false)
  const calcular = async (evento) => {
    evento.preventDefault()
    // #2235 (banca C4): o botão fica ativo; o que falta aparece no próprio campo.
    setTentou(true)
    if (!produtoId || !(numeroDaReceita(quantidade) > 0)) {
      // O foco vai ao primeiro campo que falta; o leitor de tela lê a dica ligada a ele.
      const form = evento.currentTarget
      setTimeout(() => form?.querySelector?.('[aria-invalid="true"]')?.focus(), 0)
      return
    }
    if (enviando.current) return
    enviando.current = true; setOcupado(true); setErro(null)
    try { setResultado(await calcularReceita(produtoId, numeroDaReceita(quantidade), unidade)) }
    catch (e) { setErro(e.message) }
    finally { enviando.current = false; setOcupado(false) }
  }
  if (catalogo.estado === 'carregando') return <output>Carregando as receitas…</output>
  if (catalogo.estado === 'erro') return <div role="alert">{catalogo.erro}<Botao onClick={catalogo.recarregar}>Tentar de novo</Botao></div>
  const montadas = catalogo.receitas.filter((r) => r.linhas > 0)
  // #2235 (banca C2): sem receita montada não há o que escolher; a tela diz o que falta e leva lá.
  if (montadas.length === 0) return <div className={css.aba}>
    <Vazio icone="nota" titulo="Nenhuma receita montada"
      acao={<Botao variante="primario" icone="mais" onClick={() => { window.location.hash = hashDoModulo('producao', 'receitas') }}>Montar receita</Botao>}>
      A calculadora usa a receita do prato: quanto rende e quanto leva de cada insumo. Monte a primeira receita e volte para calcular.
    </Vazio>
  </div>
  const faltaReceita = tentou && !produtoId
  const faltaQuantidade = tentou && !(numeroDaReceita(quantidade) > 0)
  return <div className={css.aba}>
    <p className={css.descricao}>Calcule insumos e custo estimado sem alterar o estoque.</p>
    <form className={`${css.campos} ${css.formulario}`} onSubmit={calcular} aria-label="Calculadora da produção" noValidate>
      <CampoSelecao rotulo="Receita" aria-required="true" disabled={ocupado} value={produtoId} onChange={(e) => setProdutoId(e.target.value)}
        aria-invalid={faltaReceita || undefined} dica={faltaReceita ? 'Escolha a receita para calcular.' : undefined}
        opcoes={[{ valor: '', rotulo: 'Escolha uma receita' }, ...montadas.map((r) => ({ valor: r.produtoId, rotulo: r.nome }))]} />
      <CampoTexto rotulo="Quantidade desejada" aria-required="true" inputMode="decimal" placeholder="Ex.: 12" disabled={ocupado} value={quantidade} onChange={(e) => setQuantidade(e.target.value)}
        aria-invalid={faltaQuantidade || undefined} dica={faltaQuantidade ? 'Diga quanto quer produzir, maior que zero.' : undefined} />
      <CampoSelecao rotulo="Unidade desejada" disabled={ocupado} value={unidade} onChange={(e) => setUnidade(e.target.value)} opcoes={UNIDADES} />
      <div className={`${css.rodapeForm} ${css.acoesDireita}`}>
        <Botao tipo="submit" variante="primario" icone="nota" className={css.semQuebra} disabled={ocupado}>{ocupado ? 'Calculando…' : 'Calcular insumos e custo'}</Botao>
      </div>
    </form>
    {erro && <p className={css.aviso} role="alert">{erro}</p>}
    {/* #2235 (banca C5): antes de calcular, a tela mostra o que o cálculo vai trazer. */}
    {!resultado && (
      <section className={css.secao} aria-label="O que o cálculo mostra">
        <h3 className={css.tituloSecao}>O cálculo mostra</h3>
        <ul className={css.descricao}>
          <li><strong>Cada insumo da receita:</strong> quanto precisa para a quantidade pedida e quanto tem no estoque.</li>
          <li><strong>O que falta comprar:</strong> insumo com saldo menor do que a receita pede.</li>
          <li><strong>Custo estimado:</strong> pelo custo cadastrado de cada insumo. Nada muda no estoque.</li>
        </ul>
      </section>
    )}
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
