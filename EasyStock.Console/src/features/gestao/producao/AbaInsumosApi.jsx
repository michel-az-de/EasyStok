import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
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
      </div>
      {podeEditar && (editando ? (
        <form className={css.topo} onSubmit={salvar}>
          <input className={css.busca} inputMode="numeric" aria-label={`Mínimo de ${insumo.nome}`} placeholder={`Mínimo (${insumo.unidade})`} value={minimo} onChange={(e) => setMinimo(e.target.value)} />
          <input className={css.busca} inputMode="decimal" aria-label={`Custo de ${insumo.nome}`} placeholder={`Custo por ${insumo.unidade}`} value={custo} onChange={(e) => setCusto(e.target.value)} />
          <Botao variante="texto" onClick={() => setEditando(false)}>Cancelar</Botao>
          <Botao tipo="submit" variante="primario">Salvar</Botao>
        </form>
      ) : (
        <div className={css.acoes}>
          <Botao variante="texto" onClick={() => setEditando(true)}>Ajustar</Botao>
        </div>
      ))}
    </li>
  )
}

export function AbaInsumosApi() {
  const { acoes } = useAcessoModulos()
  const podeEditar = acoes.editarProducao === true
  const gestao = useInsumosApi()
  const [novo, setNovo] = useState({ nome: '', unidade: 'G', minimo: '', custo: '' })

  if (gestao.estado === 'carregando') return <p className={css.descricao}>Carregando os insumos…</p>
  if (gestao.estado === 'erro' && gestao.insumos.length === 0) {
    return (
      <div className={css.aba} role="alert">
        <p>Não consegui ler os insumos: {gestao.erro}</p>
        <Botao onClick={gestao.recarregar}>Tentar de novo</Botao>
      </div>
    )
  }

  const comprar = gestao.insumos.filter((i) => i.comprar)
  const mudarNovo = (campo) => (e) => setNovo((atual) => ({ ...atual, [campo]: e.target.value }))
  const cadastrar = (evento) => {
    evento.preventDefault()
    Promise.resolve(gestao.criar(novo)).then((ok) => { if (ok !== false) setNovo({ nome: '', unidade: novo.unidade, minimo: '', custo: '' }) })
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

      {podeEditar ? <form className={css.topo} onSubmit={cadastrar} aria-label="Cadastrar insumo">
        <input className={css.busca} aria-label="Nome do insumo" placeholder="Novo insumo (ex.: molho sugo)" maxLength={180} value={novo.nome} onChange={mudarNovo('nome')} />
        <select className={css.busca} aria-label="Unidade" value={novo.unidade} onChange={mudarNovo('unidade')}>
          {UNIDADES_INSUMO.map((u) => <option key={u} value={u}>{u}</option>)}
        </select>
        <input className={css.busca} inputMode="numeric" aria-label="Mínimo" placeholder="Mínimo" value={novo.minimo} onChange={mudarNovo('minimo')} />
        <input className={css.busca} inputMode="decimal" aria-label="Custo por unidade" placeholder="Custo por unidade" value={novo.custo} onChange={mudarNovo('custo')} />
        <Botao tipo="submit" variante="primario" disabled={!novo.nome.trim()}>Cadastrar insumo</Botao>
      </form> : <p className={css.descricao}>Você pode consultar os insumos. O cadastro e os ajustes ficam com a dona ou gerente.</p>}
    </div>
  )
}
