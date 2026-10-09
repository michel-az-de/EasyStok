import { useEffect, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import { useReceitasApi } from '../../../aplicacao/useReceitasApi'
import { lerReceita, UNIDADES_RECEITA } from '../../../aplicacao/receitas'
import { lerInsumos } from '../../../aplicacao/insumos'
import { moeda } from '../../../dominio/formato'
import css from '../cardapio/abaCardapio.module.css'

// M2 › Receitas (M2.4a, #1498). Por prato do cardápio ligado ao estoque: quanto rende e os insumos
// com quantidade e unidade. O custo por porção vem do EasyStok, com a unidade convertida (a receita
// pode pedir 1,2 kg de um molho que custa por grama). Na API "ficha técnica" é a nutricional; aqui
// é a receita. Gravar é do Gerente. D-M2-01 (#1499): o prato marcado baixa os insumos ao produzir.
const custoPor = (r) => (r.custoPorRendimento == null
  ? (r.linhas === 0 ? 'sem receita' : 'custo incompleto (insumo sem custo)')
  : `${moeda(r.custoPorRendimento)} por ${r.unidadeRendimento === 'Un' ? 'porção' : r.unidadeRendimento}`)

function EditorReceita({ prato, aoSalvar, aoFechar }) {
  const [detalhe, setDetalhe] = useState(null)
  const [insumos, setInsumos] = useState([])
  const [rendimento, setRendimento] = useState('')
  const [linhas, setLinhas] = useState([])
  const [salvando, setSalvando] = useState(false)

  useEffect(() => {
    let vivo = true
    Promise.all([lerReceita(prato.produtoId), lerInsumos().catch(() => [])]).then(([d, i]) => {
      if (!vivo) return
      setDetalhe(d)
      setInsumos(i)
      setRendimento(String(d.rendimento))
      setLinhas(d.linhas.map((l) => ({ ...l, quantidade: String(l.quantidade) })))
    })
    return () => { vivo = false }
  }, [prato.produtoId])

  if (!detalhe) return <p className={css.descricao}>Carregando a receita…</p>

  const mudar = (i, campo) => (e) => setLinhas((atual) => atual.map((l, j) => (j === i ? { ...l, [campo]: e.target.value } : l)))
  const salvar = (evento) => {
    evento.preventDefault()
    setSalvando(true)
    Promise.resolve(aoSalvar(prato.produtoId, {
      rendimento, unidadeRendimento: detalhe.unidadeRendimento, unidadeMedidaBase: detalhe.unidadeMedidaBase, linhas,
    })).then((ok) => { if (ok !== false) aoFechar() }).finally(() => setSalvando(false))
  }

  return (
    <form className={css.aba} onSubmit={salvar} aria-label={`Receita de ${prato.nome}`}>
      <label className={css.topo}>
        <span className={css.nome}>Rende</span>
        <input className={css.busca} inputMode="decimal" aria-label="Rendimento" value={rendimento} onChange={(e) => setRendimento(e.target.value)} />
        <span className={css.detalhe}>{detalhe.unidadeRendimento === 'Un' ? 'porções' : detalhe.unidadeRendimento}</span>
      </label>
      <ul className={css.lista}>
        {linhas.map((l, i) => (
          <li key={`${l.insumoId}-${i}`} className={css.linha}>
            <select className={css.busca} aria-label="Insumo" value={l.insumoId} onChange={mudar(i, 'insumoId')}>
              <option value="">Escolha o insumo</option>
              {insumos.map((s) => <option key={s.id} value={s.id}>{s.nome}</option>)}
              {l.insumoId && !insumos.some((s) => s.id === l.insumoId) && <option value={l.insumoId}>{l.insumo}</option>}
            </select>
            <input className={css.busca} inputMode="decimal" aria-label="Quantidade" value={l.quantidade} onChange={mudar(i, 'quantidade')} />
            <select className={css.busca} aria-label="Unidade" value={l.unidade} onChange={mudar(i, 'unidade')}>
              {UNIDADES_RECEITA.map((u) => <option key={u} value={u}>{u}</option>)}
            </select>
            {l.custo != null && <span className={css.detalhe}>{moeda(l.custo)}</span>}
            <Botao variante="texto" onClick={() => setLinhas((atual) => atual.filter((_, j) => j !== i))}>Tirar</Botao>
          </li>
        ))}
      </ul>
      <div className={css.topo}>
        <Botao onClick={() => setLinhas((atual) => [...atual, { insumoId: '', quantidade: '', unidade: 'G', custo: null }])}>Mais um insumo</Botao>
        <Botao variante="texto" onClick={aoFechar}>Cancelar</Botao>
        <Botao tipo="submit" variante="primario" disabled={salvando}>{salvando ? 'Salvando…' : 'Salvar receita'}</Botao>
      </div>
      {insumos.length === 0 && <p className={css.descricao}>Cadastre os insumos em M2 › Insumos para montar a receita.</p>}
    </form>
  )
}

export function AbaReceitasApi() {
  const gestao = useReceitasApi()
  const [editando, setEditando] = useState(null)

  if (gestao.estado === 'carregando') return <p className={css.descricao}>Carregando as receitas…</p>
  if (gestao.estado === 'erro' && gestao.receitas.length === 0) {
    return (
      <div className={css.aba} role="alert">
        <p>Não consegui ler as receitas: {gestao.erro}</p>
        <Botao onClick={gestao.recarregar}>Tentar de novo</Botao>
      </div>
    )
  }

  return (
    <div className={css.aba}>
      <p className={css.descricao}>
        A receita de cada prato: quanto rende e os insumos. O custo por porção usa o custo dos insumos. Prato aparece
        aqui depois da primeira produção (é ela que liga o prato ao estoque).
      </p>
      {gestao.aviso && (
        <p className={css.aviso} role="alert">
          {gestao.aviso} <Botao variante="texto" onClick={gestao.fecharAviso}>Fechar</Botao>
        </p>
      )}
      {gestao.receitas.length === 0 ? (
        <p className={css.descricao}>Nenhum prato ligado ao estoque ainda.</p>
      ) : (
        <ul className={css.lista}>
          {gestao.receitas.map((r) => (
            <li key={r.produtoId} className={css.linha}>
              <div className={css.resumo}>
                <span className={css.nome}>{r.nome}</span>
                <span className={css.detalhe}>
                  {r.linhas === 0 ? 'sem insumos' : `${r.linhas} insumo(s) · rende ${r.rendimento} ${r.unidadeRendimento === 'Un' ? 'porções' : r.unidadeRendimento}`}
                </span>
              </div>
              <div className={css.estados}>
                <Pilula tom={r.custoPorRendimento == null ? 'neutro' : 'ok'} fina>{custoPor(r)}</Pilula>
                {r.baixaAutomatica && <Pilula tom="ok" fina>baixa insumos ao produzir</Pilula>}
              </div>
              <div className={css.acoes}>
                <Botao variante="texto" onClick={() => setEditando(editando === r.produtoId ? null : r.produtoId)}>
                  {r.linhas === 0 ? 'Montar receita' : 'Editar receita'}
                </Botao>
                {r.linhas > 0 && (
                  <Botao variante="texto" onClick={() => gestao.marcarBaixa(r.produtoId, !r.baixaAutomatica)}>
                    {r.baixaAutomatica ? 'Parar de baixar insumos' : 'Baixar insumos ao produzir'}
                  </Botao>
                )}
              </div>
              {editando === r.produtoId && (
                <EditorReceita prato={r} aoSalvar={gestao.salvar} aoFechar={() => setEditando(null)} />
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
