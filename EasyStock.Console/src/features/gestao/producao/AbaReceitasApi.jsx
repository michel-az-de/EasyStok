import { useEffect, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { useReceitasApi } from '../../../aplicacao/useReceitasApi'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { lerReceita, UNIDADES_RECEITA } from '../../../aplicacao/receitas'
import { lerInsumos } from '../../../aplicacao/insumos'
import { moeda } from '../../../dominio/formato'
import { hashDoModulo } from '../../../dominio/modulos'
import css from '../cardapio/abaCardapio.module.css'

// M2 › Receitas (M2.4a, #1498). Por prato do cardápio ligado ao estoque: quanto rende e os insumos
// com quantidade e unidade. O custo por porção vem do EasyStok, com a unidade convertida (a receita
// pode pedir 1,2 kg de um molho que custa por grama). Na API "ficha técnica" é a nutricional; aqui
// é a receita. Gravar é do Gerente. D-M2-01 (#1499): o prato marcado baixa os insumos ao produzir.
const custoPor = (r) => (r.custoPorRendimento == null
  ? (r.linhas === 0 ? 'sem receita' : 'custo incompleto (insumo sem custo)')
  : `${moeda(r.custoPorRendimento)} por ${r.unidadeRendimento === 'Un' ? 'porção' : r.unidadeRendimento}`)

function EditorReceita({ prato, aoSalvar, aoFechar, podeEditar, aviso, aoFecharAviso }) {
  const [detalhe, setDetalhe] = useState(null)
  const [insumos, setInsumos] = useState([])
  const [rendimento, setRendimento] = useState('')
  const [linhas, setLinhas] = useState([])
  const [salvando, setSalvando] = useState(false)
  const [erro, setErro] = useState(null)
  const [tentativa, setTentativa] = useState(0)

  useEffect(() => {
    let vivo = true
    Promise.all([lerReceita(prato.produtoId), podeEditar ? lerInsumos() : []]).then(([d, i]) => {
      if (!vivo) return
      setDetalhe(d)
      setInsumos(i)
      setRendimento(String(d.rendimento))
      setLinhas(d.linhas.map((l) => ({ ...l, quantidade: String(l.quantidade) })))
    }).catch((e) => { if (vivo) setErro(e.message) })
    return () => { vivo = false }
  }, [prato.produtoId, podeEditar, tentativa])

  if (erro) return <div className={css.aba} role="alert">
    <p className={css.descricao}>Não foi possível carregar a receita{podeEditar ? ' e os insumos' : ''}. {erro}</p>
    <div className={css.acoesReceita}>
      <Botao icone="refresh-cw" onClick={() => { setErro(null); setTentativa((n) => n + 1) }}>Tentar de novo</Botao>
      <Botao variante="texto" onClick={aoFechar}>Fechar receita</Botao>
    </div>
  </div>

  if (!detalhe) return <output className={css.descricao}>Carregando a receita…</output>

  if (!podeEditar) return (
    <section className={css.aba} aria-label={`Receita de ${prato.nome}`}>
      <p className={css.nome}>Rende {detalhe.rendimento} {detalhe.unidadeRendimento === 'Un' ? 'porções' : detalhe.unidadeRendimento}</p>
      {detalhe.linhas.length === 0 ? <p className={css.descricao}>Receita ainda sem insumos cadastrados.</p> : (
        <ul className={css.lista}>
          {detalhe.linhas.map((l) => <li key={l.insumoId} className={css.linha}>
            <span className={css.nome}>{l.insumo}</span>
            <span>{l.quantidade} {l.unidade}</span>
          </li>)}
        </ul>
      )}
      <Botao variante="texto" onClick={aoFechar}>Fechar receita</Botao>
    </section>
  )

  const mudar = (i, campo) => (e) => setLinhas((atual) => atual.map((l, j) => (j === i ? { ...l, [campo]: e.target.value } : l)))
  const salvar = (evento) => {
    evento.preventDefault()
    if (salvando) return
    setSalvando(true)
    Promise.resolve(aoSalvar(prato.produtoId, {
      rendimento, unidadeRendimento: detalhe.unidadeRendimento, unidadeMedidaBase: detalhe.unidadeMedidaBase, linhas,
    })).then((ok) => { if (ok !== false) aoFechar() }).finally(() => setSalvando(false))
  }

  return (
    <form className={`${css.aba} ${css.editorReceita}`} onSubmit={salvar} aria-label={`Receita de ${prato.nome}`}>
      <div className={css.campoQuantidade}>
        <CampoTexto rotulo="Rendimento" dica={detalhe.unidadeRendimento === 'Un' ? 'Em porções' : `Em ${detalhe.unidadeRendimento}`}
          disabled={salvando} inputMode="decimal" value={rendimento} onChange={(e) => setRendimento(e.target.value)} />
      </div>
      <ul className={css.listaFormularios}>
        {linhas.map((l, i) => (
          <li key={`${l.insumoId}-${i}`} className={`${css.campos} ${css.formulario} ${css.linhaReceita}`}>
            <CampoSelecao disabled={salvando} rotulo="Insumo" value={l.insumoId} onChange={mudar(i, 'insumoId')}
              opcoes={[{ valor: '', rotulo: 'Escolha o insumo' }, ...insumos.map((s) => ({ valor: s.id, rotulo: s.nome })),
                ...(l.insumoId && !insumos.some((s) => s.id === l.insumoId) ? [{ valor: l.insumoId, rotulo: l.insumo }] : [])]} />
            <CampoTexto disabled={salvando} rotulo="Quantidade" inputMode="decimal" value={l.quantidade} onChange={mudar(i, 'quantidade')} />
            <CampoSelecao disabled={salvando} rotulo="Unidade" value={l.unidade} onChange={mudar(i, 'unidade')}
              opcoes={UNIDADES_RECEITA.map((u) => ({ valor: u, rotulo: u }))} />
            <div className={css.rodapeForm}>
              <span className={css.detalhe}>{l.custo != null ? `Custo: ${moeda(l.custo)}` : ''}</span>
              <Botao variante="texto" icone="lixeira" disabled={salvando} aria-label={`Remover insumo ${i + 1} da receita`} onClick={() => setLinhas((atual) => atual.filter((_, j) => j !== i))}>Remover insumo</Botao>
            </div>
          </li>
        ))}
      </ul>
      {aviso && <p className={css.aviso} role="alert">
        Não foi possível salvar a receita. {aviso}
        {' '}<Botao variante="texto" onClick={aoFecharAviso}>Fechar aviso</Botao>
      </p>}
      <div className={css.acoesReceita}>
        <Botao icone="plus" disabled={salvando} onClick={() => setLinhas((atual) => [...atual, { insumoId: '', quantidade: '', unidade: 'G', custo: null }])}>Mais um insumo</Botao>
        <Botao variante="texto" disabled={salvando} onClick={aoFechar}>Cancelar</Botao>
        <Botao tipo="submit" variante="primario" icone="check" disabled={salvando}>{salvando ? 'Salvando…' : 'Salvar receita'}</Botao>
      </div>
      {insumos.length === 0 && <p className={css.descricao}>Para montar a receita, cadastre os insumos em <a className={css.link} href={hashDoModulo('producao', 'insumos')}>Insumos</a>.</p>}
    </form>
  )
}

export function AbaReceitasApi() {
  const { acoes } = useAcessoModulos()
  const podeEditar = acoes.editarProducao === true
  const gestao = useReceitasApi()
  const [editando, setEditando] = useState(null)
  const [salvando, setSalvando] = useState(false)
  const [avisoDaEdicao, setAvisoDaEdicao] = useState(null)
  const avisoNoEditor = editando !== null && editando === avisoDaEdicao

  const trocarEditor = (produtoId) => {
    setAvisoDaEdicao(null)
    setEditando(produtoId)
  }

  const salvarReceita = async (...args) => {
    gestao.fecharAviso()
    setAvisoDaEdicao(args[0])
    setSalvando(true)
    try { return await gestao.salvar(...args) } finally { setSalvando(false) }
  }

  if (gestao.estado === 'carregando') return <output className={css.descricao}>Carregando as receitas…</output>
  if (gestao.estado === 'erro' && gestao.receitas.length === 0) {
    return (
      <Vazio icone="alerta" role="alert" titulo="Não consegui ler as receitas"
        acao={<Botao icone="refresh-cw" onClick={gestao.recarregar}>Tentar de novo</Botao>}>
        {gestao.erro}
      </Vazio>
    )
  }

  return (
    <div className={css.aba}>
      {gestao.estado === 'erro' && <p className={css.aviso} role="alert">
        As receitas não foram atualizadas. A lista abaixo pode estar desatualizada. {gestao.erro}
        {' '}<Botao variante="texto" icone="refresh-cw" onClick={gestao.recarregar}>Recarregar receitas</Botao>
      </p>}
      <p className={css.descricao}>
        A receita de cada prato: quanto rende e os insumos. O custo por porção usa o custo dos insumos. Prato aparece
        aqui depois da primeira produção (é ela que liga o prato ao estoque).
      </p>
      {!podeEditar && <p className={css.descricao}>Você pode consultar as receitas. A edição e a baixa automática ficam com a dona ou gerente.</p>}
      {gestao.aviso && !avisoNoEditor && (
        <p className={css.aviso} role="alert">
          {gestao.aviso} <Botao variante="texto" onClick={gestao.fecharAviso}>Fechar</Botao>
        </p>
      )}
      {gestao.receitas.length === 0 ? (
        <Vazio icone="nota" titulo="Nenhum prato ligado ao estoque ainda.">
          A primeira produção liga o prato ao estoque. Depois, a receita aparece aqui.
        </Vazio>
      ) : (
        <ul className={css.lista}>
          {gestao.receitas.map((r) => (
            <li key={r.produtoId} className={`${css.linha} ${css.receita}`}>
              <div className={css.resumo}>
                <span className={css.nome}>{r.nome}</span>
                <span className={css.detalhe}>
                  {r.linhas === 0 ? 'sem insumos' : `${r.linhas} ${r.linhas === 1 ? 'insumo' : 'insumos'} · rende ${r.rendimento} ${r.unidadeRendimento === 'Un' ? 'porções' : r.unidadeRendimento}`}
                </span>
              </div>
              <div className={css.estados}>
                <Pilula tom={r.custoPorRendimento == null ? 'neutro' : 'ok'} fina>{custoPor(r)}</Pilula>
                {r.baixaAutomatica && <Pilula tom="ok" fina>baixa insumos ao produzir</Pilula>}
              </div>
              <div className={`${css.acoes} ${css.acoesDaReceita}`}>
                <Botao variante="texto" disabled={salvando} icone={podeEditar ? 'lapis' : 'nota'} aria-expanded={editando === r.produtoId} onClick={() => trocarEditor(editando === r.produtoId ? null : r.produtoId)}>
                  {!podeEditar ? 'Ver receita' : r.linhas === 0 ? 'Montar receita' : 'Editar receita'}
                </Botao>
                {podeEditar && r.linhas > 0 && (
                  <Botao variante="texto" disabled={salvando} onClick={() => { setAvisoDaEdicao(null); gestao.marcarBaixa(r.produtoId, !r.baixaAutomatica) }}>
                    {r.baixaAutomatica ? 'Parar de baixar insumos' : 'Baixar insumos ao produzir'}
                  </Botao>
                )}
              </div>
              {editando === r.produtoId && (
                <EditorReceita prato={r} aoSalvar={salvarReceita} aoFechar={() => trocarEditor(null)} podeEditar={podeEditar}
                  aviso={avisoNoEditor ? gestao.aviso : null} aoFecharAviso={gestao.fecharAviso} />
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
