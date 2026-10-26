// Alertas de estoque (#2070, DM7-1, só no modo API): validade próxima, produto parado, mínimo e
// crítico padrão e a ordem de saída dos lotes. Moeda, fuso, cozinha e contas automáticas ficam no Web.
import { useId, useState } from 'react'
import { carregarAlertasDeEstoque, salvarAlertasDeEstoque } from '../../../aplicacao/alertasDeEstoque'
import { useEnvio, useLeitura, usePendenciaAvisada } from '../../../aplicacao/formularioDeAjuste'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import {
  LIMITES_DOS_ALERTAS, ORDENS_DE_SAIDA, alertasMudaram, errosDosAlertas, formularioDosAlertas,
} from '../../../dominio/alertasDeEstoque'
import { Erros, FalhaDeLeitura, Pendencia, Retorno } from '../PartesDoAjuste'
import css from '../formularioDeAjuste.module.css'

const ROTULOS = {
  diasAlertaValidade: 'Validade próxima (dias)', diasAlertaParado: 'Produto parado (dias sem saída)',
  quantidadeMinimaPadrao: 'Quantidade mínima padrão', quantidadeCriticaPadrao: 'Quantidade crítica padrão',
  ordemDeSaida: 'Ordem de saída do estoque',
}
const DICAS = {
  diasAlertaValidade: 'Produto que vence em menos dias que isso fica em destaque.',
  diasAlertaParado: 'Produto sem saída há mais dias que isso fica marcado como parado.',
  quantidadeMinimaPadrao: 'Vale para o produto sem mínimo próprio. Abaixo disso o item pede atenção.',
  quantidadeCriticaPadrao: 'Igual ou abaixo disso o item fica crítico. Precisa ser menor que o mínimo.',
}

export function AbaAlertasDeEstoque({ aoMudarPendencia, aoSalvo, compacto = false }) {
  const [carga, recarregar] = useLeitura(carregarAlertasDeEstoque)
  if (carga.estado === 'carregando') return <output className={css.descricao}>Carregando os alertas de estoque…</output>
  if (carga.estado === 'erro') return <FalhaDeLeitura mensagem={`Os alertas de estoque não carregaram: ${carga.mensagem}`} aoTentarDeNovo={recarregar} />
  return <FormularioDosAlertas lido={carga.dados} compacto={compacto} aoMudarPendencia={aoMudarPendencia} aoSalvo={aoSalvo} />
}

function FormularioDosAlertas({ lido, compacto, aoMudarPendencia, aoSalvo }) {
  const id = useId()
  const [gravado, setGravado] = useState(lido.configuracao)
  const [form, setForm] = useState(() => formularioDosAlertas(lido.configuracao))
  const [erros, setErros] = useState({})
  const [situacao, enviar, limpar] = useEnvio()
  const salvando = situacao.estado === 'salvando'
  const pendente = alertasMudaram(form, gravado)
  usePendenciaAvisada(pendente, aoMudarPendencia)

  const mudar = (campo) => (evento) => {
    const valor = evento.target.value
    limpar(); setErros({})
    setForm((atual) => ({ ...atual, [campo]: valor }))
  }

  async function salvar(evento) {
    evento.preventDefault()
    if (!pendente || salvando) return
    const encontrados = errosDosAlertas(form)
    setErros(encontrados)
    if (Object.keys(encontrados).length > 0) return
    const { ok, resultado } = await enviar(async () => {
      const confirmado = await salvarAlertasDeEstoque({ loja: lido.loja, configuracao: gravado }, form)
      if (!confirmado) throw new Error('O EasyStok não confirmou os alertas. Tente de novo.')
      return confirmado
    })
    if (!ok) return
    setGravado(resultado)
    setForm(formularioDosAlertas(resultado))
    aoSalvo?.()
  }

  function desfazer() {
    setForm(formularioDosAlertas(gravado))
    setErros({})
    limpar()
  }

  const numero = (campo) => {
    const [minimo, maximo] = LIMITES_DOS_ALERTAS[campo]
    return <CampoTexto rotulo={ROTULOS[campo]} dica={DICAS[campo]} tipo="number" inputMode="numeric" min={minimo} max={maximo} step={1}
      required value={form[campo]} onChange={mudar(campo)} />
  }

  return (
    <form className={css.secao} aria-labelledby={`${id}-titulo`} onSubmit={salvar}>
      <h3 id={`${id}-titulo`}>{compacto ? 'Alertas' : 'Alertas de estoque'}</h3>
      {!compacto && <p className={css.descricao}>Quando o estoque avisa você e de qual lote a saída sai primeiro.</p>}
      <Pendencia visivel={pendente && !salvando} />
      <fieldset className={css.campos} disabled={salvando}>
        <legend className="sr">Alertas de estoque</legend>
        {numero('diasAlertaValidade')}
        {numero('diasAlertaParado')}
        {numero('quantidadeMinimaPadrao')}
        {numero('quantidadeCriticaPadrao')}
        <div className={css.inteiro}>
          <CampoSelecao rotulo={ROTULOS.ordemDeSaida} opcoes={ORDENS_DE_SAIDA} value={form.ordemDeSaida} onChange={mudar('ordemDeSaida')} />
        </div>
      </fieldset>
      <Erros erros={erros} rotulos={ROTULOS} />
      <div className={css.acoes}>
        <Botao variante="primario" tipo="submit" className={css.toque} disabled={!pendente || salvando}>Salvar alertas</Botao>
        <Botao variante="texto" className={css.toque} disabled={!pendente || salvando} onClick={desfazer}>Desfazer edição</Botao>
        <Retorno situacao={situacao} />
      </div>
    </form>
  )
}
