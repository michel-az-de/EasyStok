import { useState } from 'react'
import { NomeInsumoPesquisado } from './PesquisaProducao'
import { Modal } from '../../../componentes/Modal'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { useInsumosApi } from '../../../aplicacao/useInsumosApi'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { ondeOInsumoEUsado, rotuloDaEmbalagem, UNIDADES_INSUMO } from '../../../aplicacao/insumos'
import { moeda } from '../../../dominio/formato'
import css from '../cardapio/abaCardapio.module.css'

// M2 › Insumos (M2.3, #1496). O intermediário (molho, recheio, massa laminada) e a embalagem, com o
// saldo, o mínimo e em quantas receitas entra. Abaixo do mínimo aparece em "Comprar". Farinha e ovo
// ficam fora (US-067). A Cozinha consulta; cadastrar e ajustar exigem a capacidade da sessão.
const formatar = (n) => (Number.isInteger(n) ? String(n) : n.toLocaleString('pt-BR', { maximumFractionDigits: 2 }))

// #1585: o aviso sai junto do formulário que o causou, como no Estoque do dia e nas Receitas.
// `avisarEm(origem)` fecha o aviso anterior e diz onde sai o próximo: no cadastro, no ajuste do
// insumo (o id dele) ou, sem origem, no topo.
const CADASTRO = 'cadastro'

function LinhaInsumo({ insumo, gestao, podeEditar, aviso, avisarEm }) {
  const [editando, setEditando] = useState(false)
  const [minimo, setMinimo] = useState(insumo.minimo ?? '')
  const [custo, setCusto] = useState(insumo.custo ?? '')
  const [ocupado, setOcupado] = useState(false)

  const fazer = async (acao) => {
    if (ocupado) return false
    setOcupado(true)
    try { return await acao() } finally { setOcupado(false) }
  }

  const salvar = (evento) => {
    evento.preventDefault()
    avisarEm(insumo.id)
    fazer(() => gestao.atualizar(insumo.id, { minimo, custo })).then((ok) => { if (ok !== false) setEditando(false) })
  }

  return (
    <li className={css.linha} aria-busy={ocupado}>
      <div className={css.resumo}>
        <span className={css.nome}>{insumo.nome}</span>
        <span className={css.detalhe}>
          {ondeOInsumoEUsado(insumo)}
          {insumo.custo != null ? ` · custo ${moeda(insumo.custo)} por ${insumo.unidade}` : ''}
        </span>
      </div>
      <span className={css.preco}>{formatar(insumo.saldo)} {insumo.unidade}</span>
      <div className={css.estados}>
        {insumo.comprar && <Pilula tom="aviso" fina>Abaixo do mínimo ({insumo.minimo} {insumo.unidade})</Pilula>}
        {!insumo.comprar && insumo.minimo != null && <Pilula tom="neutro" fina>Mínimo {insumo.minimo} {insumo.unidade}</Pilula>}
        {insumo.embalagem && <Pilula tom="marca" fina>{rotuloDaEmbalagem(insumo)}</Pilula>}
      </div>
      {podeEditar && editando && (
        <Modal titulo={`Ajustar ${insumo.nome}`} aoFechar={ocupado ? undefined : () => { avisarEm(null); setEditando(false) }}>
        <form className={`${css.campos} ${css.formulario} ${css.ajusteInsumo}`} onSubmit={salvar}>
          <CampoTexto disabled={ocupado} inputMode="numeric" rotulo={`Mínimo (${insumo.unidade})`} aria-label={`Mínimo de ${insumo.nome}`} value={minimo} onChange={(e) => setMinimo(e.target.value)} />
          <CampoTexto disabled={ocupado} inputMode="decimal" rotulo={`Custo por ${insumo.unidade} (R$)`} aria-label={`Custo de ${insumo.nome}`} value={custo} onChange={(e) => setCusto(e.target.value)} />
          {aviso && <p className={`${css.aviso} ${css.linhaCompleta}`} role="alert">
            Não foi possível salvar o ajuste. {aviso}
            {' '}<Botao variante="texto" onClick={() => avisarEm(null)}>Fechar aviso</Botao>
          </p>}
          <div className={css.rodapeForm}>
            <Botao variante="texto" disabled={ocupado} onClick={() => { if (aviso) avisarEm(null); setEditando(false) }}>Cancelar</Botao>
            <Botao tipo="submit" variante="primario" icone="check" disabled={ocupado}>{ocupado ? 'Salvando…' : 'Salvar ajuste'}</Botao>
          </div>
        </form>
        </Modal>
      )}
      {podeEditar && (
        <div className={css.acoes}>
          <Botao variante="texto" icone="lapis" disabled={ocupado} aria-label={`Ajustar ${insumo.nome}`} aria-haspopup="dialog" onClick={() => {
            setMinimo(insumo.minimo ?? ''); setCusto(insumo.custo ?? ''); setEditando(true)
          }}>Ajustar</Botao>
          <Botao variante="texto" disabled={ocupado} onClick={() => { avisarEm(null); fazer(() => gestao.marcarEmbalagem(insumo.id, !insumo.embalagem)) }}>
            {insumo.embalagem ? 'Não é embalagem' : 'É embalagem'}
          </Botao>
        </div>
      )}
    </li>
  )
}

export function AbaInsumosApi() {
  const { acoes } = useAcessoModulos()
  const podeEditar = acoes.editarProducao === true
  const gestao = useInsumosApi()
  const [novo, setNovo] = useState({ nome: '', unidade: 'G', minimo: '', custo: '', embalagem: false })
  const [criando, setCriando] = useState(false)
  const [cadastroAberto, setCadastroAberto] = useState(false)
  const [origemDoAviso, setOrigemDoAviso] = useState(null)

  if (gestao.estado === 'carregando') return <output className={css.descricao}>Carregando os insumos…</output>
  if (gestao.estado === 'erro' && gestao.insumos.length === 0) {
    return (
      <Vazio icone="alerta" role="alert" titulo="Não consegui ler os insumos"
        acao={<Botao icone="refresh-cw" onClick={gestao.recarregar}>Tentar de novo</Botao>}>
        {gestao.erro}
      </Vazio>
    )
  }

  const comprar = gestao.insumos.filter((i) => i.comprar)
  const avisarEm = (origem) => {
    gestao.fecharAviso()
    setOrigemDoAviso(origem)
  }
  // Sem formulário para receber o aviso (o insumo saiu da lista), ele volta ao topo.
  const avisoNoFormulario = origemDoAviso === CADASTRO ? podeEditar && cadastroAberto : gestao.insumos.some((i) => i.id === origemDoAviso)
  const mudarNovo = (campo) => (e) => setNovo((atual) => ({ ...atual, [campo]: e.target.value }))
  const cadastrar = (evento) => {
    evento.preventDefault()
    if (criando) return
    avisarEm(CADASTRO)
    setCriando(true)
    Promise.resolve(gestao.criar(novo)).then((ok) => { if (ok !== false) { setNovo({ nome: '', unidade: novo.unidade, minimo: '', custo: '', embalagem: novo.embalagem }); setCadastroAberto(false) } }).finally(() => setCriando(false))
  }

  return (
    <div className={css.aba}>
      {gestao.estado === 'erro' && <p className={css.aviso} role="alert">
        Os insumos não foram atualizados. A lista abaixo pode estar desatualizada. {gestao.erro}
        {' '}<Botao variante="texto" icone="refresh-cw" onClick={gestao.recarregar}>Recarregar insumos</Botao>
      </p>}
      {gestao.aviso && !avisoNoFormulario && (
        <p className={css.aviso} role="alert">
          {gestao.aviso} <Botao variante="texto" onClick={gestao.fecharAviso}>Fechar</Botao>
        </p>
      )}

      <section className={css.secao} aria-labelledby="insumos-comprar">
        <h3 id="insumos-comprar" className={css.tituloSecao}>Comprar</h3>
        {comprar.length === 0
          ? <p className={css.descricao}>Nenhum insumo abaixo do mínimo.</p>
          : <p className={css.descricao}>{comprar.map((i) => `${i.nome} (tem ${formatar(i.saldo)} ${i.unidade}, mínimo ${i.minimo})`).join(' · ')}</p>}
      </section>

      <section className={css.secao} aria-labelledby="insumos-lista">
        <h3 id="insumos-lista" className={css.tituloSecao}>Insumos cadastrados</h3>
        {gestao.insumos.length === 0 ? (
          <p className={css.descricao}>Nenhum insumo cadastrado.{podeEditar && ' Use Cadastrar insumo para incluir molho, recheio ou embalagem.'}</p>
        ) : (
          <ul className={css.lista}>
            {gestao.insumos.map((i) => <LinhaInsumo key={i.id} insumo={i} gestao={gestao} podeEditar={podeEditar}
              aviso={origemDoAviso === i.id ? gestao.aviso : null} avisarEm={avisarEm} />)}
          </ul>
        )}
      </section>

      {podeEditar ? <><Botao variante="primario" icone="mais" aria-haspopup="dialog" onClick={() => { avisarEm(CADASTRO); setCadastroAberto(true) }}>Cadastrar insumo</Botao>
      {cadastroAberto && <Modal titulo="Cadastrar insumo" aoFechar={criando ? undefined : () => { avisarEm(null); setCadastroAberto(false) }} largura="min(720px, calc(100vw - 24px))"><form className={`${css.campos} ${css.formulario} ${css.cadastroInsumo}`} onSubmit={cadastrar} aria-label="Cadastrar insumo">
        <NomeInsumoPesquisado disabled={criando} value={novo.nome} onChange={mudarNovo('nome')} />
        <CampoSelecao disabled={criando} rotulo="Unidade" value={novo.unidade} onChange={mudarNovo('unidade')} opcoes={UNIDADES_INSUMO.map((u) => ({ valor: u, rotulo: u }))} />
        <CampoTexto disabled={criando} inputMode="numeric" rotulo="Mínimo (opcional)" value={novo.minimo} onChange={mudarNovo('minimo')} />
        <CampoTexto disabled={criando} inputMode="decimal" rotulo="Custo por unidade (R$)" aria-label="Custo por unidade" value={novo.custo} onChange={mudarNovo('custo')} />
        {origemDoAviso === CADASTRO && gestao.aviso && <p className={`${css.aviso} ${css.linhaCompleta}`} role="alert">
          Não foi possível cadastrar o insumo. {gestao.aviso}
          {' '}<Botao variante="texto" onClick={gestao.fecharAviso}>Fechar aviso</Botao>
        </p>}
        <div className={css.rodapeForm}>
          <label className={css.chave}>
            <input type="checkbox" disabled={criando} checked={novo.embalagem} onChange={(e) => setNovo((atual) => ({ ...atual, embalagem: e.target.checked }))} />
            <span>É embalagem<span className={css.ajudaEmbalagem}>Bandeja, selo ou pote. Conta por unidade e é descontada em toda produção.</span></span>
          </label>
          <Botao tipo="submit" variante="primario" icone="plus" disabled={criando || !novo.nome.trim()}>{criando ? 'Cadastrando…' : 'Cadastrar insumo'}</Botao>
        </div>
      </form></Modal>}</> : <p className={css.descricao}>Você pode consultar os insumos. O cadastro e os ajustes ficam com a dona ou gerente.</p>}
    </div>
  )
}
