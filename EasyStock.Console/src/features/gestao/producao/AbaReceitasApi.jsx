import { useEffect, useState } from 'react'
import { Modal } from '../../../componentes/Modal'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Icone } from '../../../componentes/Icone'
import { useReceitasApi } from '../../../aplicacao/useReceitasApi'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { lerReceita, orientacoesDaReceita, progressoDasReceitas, receitaEmBranco, rotuloDoRendimento, temReceita, textoDoProgresso, UNIDADES_RECEITA, UNIDADES_RENDIMENTO } from '../../../aplicacao/receitas'
import { lerInsumos } from '../../../aplicacao/insumos'
import { custoDoRascunho, rascunhoDaSugestao } from '../../../aplicacao/calculosReceita'
import { PesquisaReceitaIa } from './PesquisaProducao'
import { moeda } from '../../../dominio/formato'
import { hashDoModulo } from '../../../dominio/modulos'
import css from '../cardapio/abaCardapio.module.css'
import tabela from './tabelaProducao.module.css'
import botaoCss from '../../../componentes/Botao.module.css'

// M2 › Receitas (M2.4a, #1498). Por prato do cardápio ligado ao estoque: quanto rende e os insumos
// com quantidade e unidade. O custo por porção vem do EasyStok, com a unidade convertida (a receita
// pode pedir 1,2 kg de um molho que custa por grama). Na API "ficha técnica" é a nutricional; aqui
// é a receita. Gravar é do Gerente. D-M2-01 (#1499): o prato marcado baixa os insumos ao produzir.
// M1.4e (#1552): a receita pode render em porções ou em peso; em peso, a baixa acompanha o tamanho da porção.
// #2235 (banca A1-A3): todo prato do cardápio aparece; o ainda sem estoque é ligado ao salvar a receita.
// Um estado só por prato: "Sem receita" ou o custo.
const custoPor = (r) => (r.custoPorRendimento == null
  ? 'Custo incompleto (insumo sem custo)'
  : `${moeda(r.custoPorRendimento)} por ${r.unidadeRendimento === 'Un' ? 'porção' : rotuloDoRendimento(r.unidadeRendimento)}`)

function EditorReceita({ prato, aoSalvar, aoFechar, podeEditar, aviso, aoFecharAviso }) {
  const [detalhe, setDetalhe] = useState(null)
  const [insumos, setInsumos] = useState([])
  const [rendimento, setRendimento] = useState('')
  const [unidade, setUnidade] = useState('Un')
  const [linhas, setLinhas] = useState([])
  const [salvando, setSalvando] = useState(false)
  const [erro, setErro] = useState(null)
  const [tentativa, setTentativa] = useState(0)
  // A receita é lida uma vez, pelo produto que o prato tinha ao abrir. Se o salvar ligar o prato e
  // falhar, a lista relida traz o produto novo sem apagar o que já foi digitado aqui.
  const [produtoAoAbrir] = useState(prato.produtoId)

  useEffect(() => {
    let vivo = true
    const receita = produtoAoAbrir ? lerReceita(produtoAoAbrir) : Promise.resolve(receitaEmBranco({ nome: prato.nome }))
    Promise.all([receita, podeEditar ? lerInsumos() : []]).then(([d, i]) => {
      if (!vivo) return
      setDetalhe(d)
      setInsumos(i)
      setRendimento(String(d.rendimento))
      setUnidade(d.unidadeRendimento)
      setLinhas(d.linhas.map((l) => ({ ...l, quantidade: String(l.quantidade) })))
    }).catch((e) => { if (vivo) setErro(e.message) })
    return () => { vivo = false }
  }, [prato.sku, prato.nome, produtoAoAbrir, podeEditar, tentativa])

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
      <p className={css.nome}>Rende {detalhe.rendimento} {detalhe.unidadeRendimento === 'Un' && Number(detalhe.rendimento) === 1 ? 'porção' : rotuloDoRendimento(detalhe.unidadeRendimento)}</p>
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
      rendimento, unidadeRendimento: unidade, unidadeMedidaBase: detalhe.unidadeMedidaBase, linhas,
    }, prato)).then((ok) => { if (ok !== false) aoFechar() }).finally(() => setSalvando(false))
  }
  // A receita que já rende em litro ou mililitro mantém a unidade dela na lista.
  const unidadesDoRendimento = UNIDADES_RENDIMENTO.some((u) => u.valor === unidade)
    ? UNIDADES_RENDIMENTO
    : [...UNIDADES_RENDIMENTO, { valor: unidade, rotulo: unidade || 'Conferir unidade do rendimento' }]
  const semInsumos = insumos.length === 0
  const orientacoes = orientacoesDaReceita({ prato, unidadeRendimento: unidade, linhas, insumos })
  const referencias = detalhe.linhas.filter((l) => !insumos.some((s) => s.id === l.insumoId))
    .map((l) => ({ id: l.insumoId, unidade: l.unidadeDoInsumo, custo: l.custoReferencia }))
  const custos = custoDoRascunho({ linhas, rendimento, unidadeRendimento: unidade }, [...insumos, ...referencias])
  const aplicarSugestao = (sugestao) => {
    const rascunho = rascunhoDaSugestao(sugestao, insumos)
    setRendimento(rascunho.rendimento); setUnidade(rascunho.unidadeRendimento); setLinhas(rascunho.linhas)
    aoFecharAviso()
  }

  return (
    <><PesquisaReceitaIa nome={prato.nome} aoAplicar={aplicarSugestao} disabled={salvando} />
    <form className={`${css.aba} ${css.editorReceita}`} onSubmit={salvar} aria-label={`Receita de ${prato.nome}`}>
      <div className={`${css.campos} ${css.rendimentoDaReceita}`}>
        <CampoTexto rotulo="Rendimento" dica={`Quanto a receita rende, em ${rotuloDoRendimento(unidade)}`}
          disabled={salvando} inputMode="decimal" value={rendimento} onChange={(e) => setRendimento(e.target.value)} />
        <CampoSelecao rotulo="Rende em" dica="Em peso, a baixa acompanha o tamanho da porção"
          disabled={salvando} value={unidade} onChange={(e) => setUnidade(e.target.value)}
          opcoes={unidadesDoRendimento} />
      </div>
      {orientacoes.map((texto) => <p key={texto} className={css.aviso}>{texto}</p>)}
      <section aria-label="Custo estimado do rascunho">
        <p className={css.descricao}>{custos.total == null ? 'Custo incompleto: escolha os insumos e confira as quantidades, unidades e custos cadastrados.' : `Custo estimado da receita: ${moeda(custos.total)}`}
          {custos.porRendimento != null && ` · ${moeda(custos.porRendimento)} por ${unidade === 'Un' ? 'porção' : rotuloDoRendimento(unidade)}`}</p>
      </section>
      <ul className={css.listaFormularios}>
        {linhas.map((l, i) => (
          <li key={`${l.insumoId}-${i}`} className={`${css.campos} ${css.formulario} ${css.linhaReceita}`}>
            <CampoSelecao disabled={salvando} rotulo={l.insumo && !l.insumoId ? `Insumo para ${l.insumo}` : 'Insumo'} value={l.insumoId} onChange={mudar(i, 'insumoId')}
              opcoes={[{ valor: '', rotulo: 'Escolha o insumo' }, ...insumos.map((s) => ({ valor: s.id, rotulo: s.nome })),
                ...(l.insumoId && !insumos.some((s) => s.id === l.insumoId) ? [{ valor: l.insumoId, rotulo: l.insumo }] : [])]} />
            <CampoTexto disabled={salvando} rotulo="Quantidade" inputMode="decimal" value={l.quantidade} onChange={mudar(i, 'quantidade')} />
            <CampoSelecao disabled={salvando} rotulo="Unidade" value={l.unidade} onChange={mudar(i, 'unidade')}
              opcoes={[{ valor: '', rotulo: 'Conferir unidade' }, ...UNIDADES_RECEITA.map((u) => ({ valor: u, rotulo: u }))]} />
            <div className={css.rodapeForm}>
              <span className={css.detalhe}>{custos.custos[i] == null ? 'Custo não conhecido' : `Custo estimado: ${moeda(custos.custos[i])}`}</span>
              <Botao variante="texto" icone="lixeira" disabled={salvando} aria-label={`Remover insumo ${i + 1} da receita`} onClick={() => setLinhas((atual) => atual.filter((_, j) => j !== i))}>Remover insumo</Botao>
            </div>
          </li>
        ))}
      </ul>
      {aviso && <p className={css.aviso} role="alert">
        Não foi possível salvar a receita. {aviso}
        {' '}<Botao variante="texto" onClick={aoFecharAviso}>Fechar aviso</Botao>
      </p>}
      {semInsumos && <p className={css.descricao}>Para montar a receita, cadastre primeiro os insumos.</p>}
      <div className={css.acoesReceita}>
        <Botao icone="plus" disabled={salvando} onClick={() => setLinhas((atual) => [...atual, { insumoId: '', quantidade: '', unidade: 'G', custo: null }])}>Mais um insumo</Botao>
        <Botao variante="texto" disabled={salvando} onClick={aoFechar}>Cancelar</Botao>
        {/* #2362: sem insumo, salvar não leva a nada; a ação principal é ir cadastrá-los. */}
        {semInsumos && <a className={`${botaoCss.botao} ${botaoCss.primario} ${css.linkBotao}`} href={hashDoModulo('producao', 'insumos')}>Cadastrar insumos</a>}
        <Botao tipo="submit" variante={semInsumos ? 'secundario' : 'primario'} icone="check" disabled={salvando}>{salvando ? 'Salvando…' : 'Salvar receita'}</Botao>
      </div>
    </form></>
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
  const progresso = progressoDasReceitas(gestao.receitas)

  const trocarEditor = (sku) => {
    setAvisoDaEdicao(null)
    setEditando(sku)
  }

  const salvarReceita = async (...args) => {
    gestao.fecharAviso()
    setAvisoDaEdicao(editando)
    setSalvando(true)
    try { return await gestao.salvar(...args) } finally { setSalvando(false) }
  }

  if (gestao.estado === 'carregando') return <output className={css.descricao}>Carregando as receitas…</output>
  if (gestao.estado === 'erro' && gestao.receitas.length === 0) {
    return (
      <Vazio icone="alerta" role="alert" titulo="Não foi possível carregar as receitas"
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
        Monte a receita de cada prato do cardápio: quanto rende e os insumos. Com ela, o sistema calcula o custo por porção e pode baixar os insumos ao produzir.
      </p>
      {progresso.total > 0 && <p className={css.nome} aria-live="polite">{textoDoProgresso(progresso)}</p>}
      {!podeEditar && <p className={css.descricao}>Seu perfil permite apenas consultar as receitas.</p>}
      {gestao.aviso && !avisoNoEditor && (
        <p className={css.aviso} role="alert">
          {gestao.aviso} <Botao variante="texto" onClick={gestao.fecharAviso}>Fechar</Botao>
        </p>
      )}
      {gestao.receitas.length === 0 ? (
        <Vazio icone="nota" titulo="Nenhum prato no cardápio">
          Cadastre os pratos em <a className={css.link} href={hashDoModulo('cardapio', 'itens')}>Itens do cardápio</a> e volte para montar a receita de cada um.
        </Vazio>
      ) : (
        // #2439: tabela com o rendimento, a situação e as ações alinhados em colunas.
        <div className={tabela.moldura}>
          <table className={tabela.tabela} aria-label="Receitas por prato">
            <thead><tr><th scope="col">Prato</th><th scope="col">Rendimento</th><th scope="col">Situação</th><th scope="col" className={tabela.colunaAcoes}>Ações</th></tr></thead>
            <tbody>
              {gestao.receitas.map((r) => (
                <tr key={r.sku}>
                  <td>
                    <span className={tabela.nome}>{r.nome}</span>
                    {temReceita(r) && <span className={tabela.apoio}>{`${r.linhas} ${r.linhas === 1 ? 'insumo' : 'insumos'}`}</span>}
                  </td>
                  <td data-rotulo="Rendimento" className={tabela.valor}>
                    {temReceita(r) ? `${r.rendimento} ${rotuloDoRendimento(r.unidadeRendimento)}` : <span className={tabela.tomNeutro}>—</span>}
                  </td>
                  <td data-rotulo="Situação">
                    <span>
                      {temReceita(r)
                        ? <span className={`${tabela.situacao} ${r.custoPorRendimento == null ? tabela.tomAtencao : tabela.tomOk}`}>
                          <Icone nome={r.custoPorRendimento == null ? 'alerta' : 'check'} />{custoPor(r)}
                        </span>
                        : <span className={`${tabela.situacao} ${tabela.tomNeutro}`}><Icone nome="nota" />Sem receita</span>}
                      {r.baixaAutomatica && <span className={tabela.apoio}>Baixa insumos ao produzir</span>}
                    </span>
                  </td>
                  <td className={tabela.colunaAcoes}>
                    <div className={tabela.acoes}>
                      {(podeEditar || temReceita(r)) && (
                        <Botao variante={podeEditar && r.sku === progresso.proximo ? 'primario' : 'secundario'} disabled={salvando}
                          icone={!podeEditar ? 'nota' : temReceita(r) ? 'lapis' : 'mais'} aria-expanded={editando === r.sku}
                          aria-label={`${!podeEditar ? 'Ver receita' : temReceita(r) ? 'Editar receita' : 'Montar receita'} de ${r.nome}`}
                          onClick={() => trocarEditor(editando === r.sku ? null : r.sku)}>
                          {!podeEditar ? 'Ver receita' : temReceita(r) ? 'Editar receita' : 'Montar receita'}
                        </Botao>
                      )}
                      {podeEditar && temReceita(r) && (
                        <Botao variante="texto" disabled={salvando} onClick={() => { setAvisoDaEdicao(null); gestao.marcarBaixa(r.produtoId, !r.baixaAutomatica) }}>
                          {r.baixaAutomatica ? 'Parar de baixar insumos' : 'Baixar insumos ao produzir'}
                        </Botao>
                      )}
                    </div>
                    {editando === r.sku && (
                      <Modal titulo={`Receita de ${r.nome}`} aoFechar={salvando ? undefined : () => trocarEditor(null)} largura="min(900px, calc(100vw - 24px))">
                      <EditorReceita prato={r} aoSalvar={salvarReceita} aoFechar={() => trocarEditor(null)} podeEditar={podeEditar}
                        aviso={avisoNoEditor ? gestao.aviso : null} aoFecharAviso={gestao.fecharAviso} />
                      </Modal>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
