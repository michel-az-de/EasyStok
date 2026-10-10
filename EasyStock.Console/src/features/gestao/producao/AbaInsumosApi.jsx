import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { useInsumosApi } from '../../../aplicacao/useInsumosApi'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { UNIDADES_INSUMO } from '../../../aplicacao/insumos'
import { moeda } from '../../../dominio/formato'
import css from '../cardapio/abaCardapio.module.css'

// M2 › Insumos (M2.3, #1496). O intermediário (molho, recheio, massa laminada) e a embalagem, com o
// saldo, o mínimo e em quantas receitas entra. Abaixo do mínimo aparece em "Comprar". Farinha e ovo
// ficam fora (US-067). A Cozinha consulta; cadastrar e ajustar exigem a capacidade da sessão.
const formatar = (n) => (Number.isInteger(n) ? String(n) : n.toLocaleString('pt-BR', { maximumFractionDigits: 2 }))

function LinhaInsumo({ insumo, gestao, podeEditar }) {
  const [editando, setEditando] = useState(false)
  const [minimo, setMinimo] = useState(insumo.minimo ?? '')
  const [custo, setCusto] = useState(insumo.custo ?? '')

  const salvar = (evento) => {
    evento.preventDefault()
    Promise.resolve(gestao.atualizar(insumo.id, { minimo, custo })).then((ok) => { if (ok !== false) setEditando(false) })
  }

  return (
    <li className={css.linha}>
      <div className={css.resumo}>
        <span className={css.nome}>{insumo.nome}</span>
        <span className={css.detalhe}>
          {insumo.receitas === 0 ? 'não entra em receita' : insumo.receitas === 1 ? 'usado em 1 receita' : `usado em ${insumo.receitas} receitas`}
          {insumo.custo != null ? ` · custo ${moeda(insumo.custo)} por ${insumo.unidade}` : ''}
        </span>
      </div>
      <span className={css.preco}>{formatar(insumo.saldo)} {insumo.unidade}</span>
      <div className={css.estados}>
        {insumo.comprar && <Pilula tom="aviso" fina>Abaixo do mínimo ({insumo.minimo} {insumo.unidade})</Pilula>}
        {!insumo.comprar && insumo.minimo != null && <Pilula tom="neutro" fina>Mínimo {insumo.minimo} {insumo.unidade}</Pilula>}
        {insumo.embalagem && <Pilula tom="marca" fina>Embalagem: desce em toda produção</Pilula>}
      </div>
      {podeEditar && (editando ? (
        <form className={`${css.campos} ${css.formulario} ${css.ajusteInsumo}`} onSubmit={salvar}>
          <CampoTexto inputMode="numeric" rotulo={`Mínimo (${insumo.unidade})`} aria-label={`Mínimo de ${insumo.nome}`} value={minimo} onChange={(e) => setMinimo(e.target.value)} />
          <CampoTexto inputMode="decimal" rotulo={`Custo por ${insumo.unidade} (R$)`} aria-label={`Custo de ${insumo.nome}`} value={custo} onChange={(e) => setCusto(e.target.value)} />
          <div className={css.rodapeForm}>
            <Botao variante="texto" onClick={() => setEditando(false)}>Cancelar</Botao>
            <Botao tipo="submit" variante="primario" icone="check">Salvar</Botao>
          </div>
        </form>
      ) : (
        <div className={css.acoes}>
          <Botao variante="texto" icone="lapis" onClick={() => setEditando(true)}>Ajustar</Botao>
          <Botao variante="texto" onClick={() => gestao.marcarEmbalagem(insumo.id, !insumo.embalagem)}>
            {insumo.embalagem ? 'Não é embalagem' : 'É embalagem'}
          </Botao>
        </div>
      ))}
    </li>
  )
}

export function AbaInsumosApi() {
  const { acoes } = useAcessoModulos()
  const podeEditar = acoes.editarProducao === true
  const gestao = useInsumosApi()
  const [novo, setNovo] = useState({ nome: '', unidade: 'G', minimo: '', custo: '', embalagem: false })

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
  const mudarNovo = (campo) => (e) => setNovo((atual) => ({ ...atual, [campo]: e.target.value }))
  const cadastrar = (evento) => {
    evento.preventDefault()
    Promise.resolve(gestao.criar(novo)).then((ok) => { if (ok !== false) setNovo({ nome: '', unidade: novo.unidade, minimo: '', custo: '', embalagem: novo.embalagem }) })
  }

  return (
    <div className={css.aba}>
      {gestao.aviso && (
        <p className={css.aviso} role="alert">
          {gestao.aviso} <Botao variante="texto" onClick={gestao.fecharAviso}>Fechar</Botao>
        </p>
      )}

      <section aria-labelledby="insumos-comprar">
        <h3 id="insumos-comprar" className={css.nome}>Comprar</h3>
        {comprar.length === 0
          ? <p className={css.descricao}>Nenhum insumo abaixo do mínimo.</p>
          : <p className={css.descricao}>{comprar.map((i) => `${i.nome} (tem ${formatar(i.saldo)} ${i.unidade}, mínimo ${i.minimo})`).join(' · ')}</p>}
      </section>

      <section aria-labelledby="insumos-lista">
        <h3 id="insumos-lista" className={css.nome}>Insumos</h3>
        {gestao.insumos.length === 0 ? (
          <p className={css.descricao}>Nenhum insumo cadastrado.{podeEditar && ' Cadastre o molho, o recheio ou a embalagem abaixo.'}</p>
        ) : (
          <ul className={css.lista}>
            {gestao.insumos.map((i) => <LinhaInsumo key={i.id} insumo={i} gestao={gestao} podeEditar={podeEditar} />)}
          </ul>
        )}
      </section>

      {podeEditar ? <form className={`${css.campos} ${css.formulario} ${css.cadastroInsumo}`} onSubmit={cadastrar} aria-label="Cadastrar insumo">
        <CampoTexto rotulo="Nome do insumo" placeholder="Ex.: molho sugo" maxLength={180} value={novo.nome} onChange={mudarNovo('nome')} />
        <CampoSelecao rotulo="Unidade" value={novo.unidade} onChange={mudarNovo('unidade')} opcoes={UNIDADES_INSUMO.map((u) => ({ valor: u, rotulo: u }))} />
        <CampoTexto inputMode="numeric" rotulo="Mínimo" value={novo.minimo} onChange={mudarNovo('minimo')} />
        <CampoTexto inputMode="decimal" rotulo="Custo por unidade (R$)" aria-label="Custo por unidade" value={novo.custo} onChange={mudarNovo('custo')} />
        <div className={css.rodapeForm}>
          <label className={css.chave}>
            <input type="checkbox" checked={novo.embalagem} onChange={(e) => setNovo((atual) => ({ ...atual, embalagem: e.target.checked }))} />
            <span>É embalagem<span className={css.ajudaEmbalagem}>Bandeja, selo ou pote. Conta por unidade e desce em toda produção.</span></span>
          </label>
          <Botao tipo="submit" variante="primario" icone="plus" disabled={!novo.nome.trim()}>Cadastrar insumo</Botao>
        </div>
      </form> : <p className={css.descricao}>Você pode consultar os insumos. O cadastro e os ajustes ficam com a dona ou gerente.</p>}
    </div>
  )
}
