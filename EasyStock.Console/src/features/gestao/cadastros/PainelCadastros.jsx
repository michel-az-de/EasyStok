import { useEffect, useRef, useState } from 'react'
import { useAcoes, useAtendimento } from '../../../aplicacao/contextos'
import { anunciarAberturaDeConversa } from '../../../aplicacao/aberturaDeConversa'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { useGravacaoDoAjuste, useRascunhoDoAjuste } from '../../../aplicacao/rascunhosDosAjustes'
import { contextoCadastro, obterCadastro, salvarCadastro, useContextoVisualCadastro, useListaCadastros } from '../../../aplicacao/cadastros'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { BuscaComSugestoes } from '../../../componentes/BuscaComSugestoes'
import { Combobox } from '../../../componentes/Combobox'
import { Icone } from '../../../componentes/Icone'
import { Pilula } from '../../../componentes/Pilula'
import { Vazio } from '../../../componentes/Vazio'
import { hashDoModulo } from '../../../dominio/modulos'
import { moeda } from '../../../dominio/formato'
import { abrirEdicaoCadastro, chaveContextoCadastro, emailCadastro, filtrarSemCadastro, ordemDoCabecalhoNome, ORDENS_CADASTRO, situacaoDaLinha, sortDoNome, telefoneCadastro, telefoneNaTela, temProximaPagina, TEXTOS_CADASTRO, TITULOS_CADASTROS } from '../../../dominio/cadastros'
import { EditorCadastro } from './EditorCadastro'
import { ModelosCadastro } from './ModelosCadastro'
import { MetodosCadastro } from './MetodosCadastro'
import css from './cadastros.module.css'

const FILTROS = [{ valor: '', rotulo: 'Todos os cadastros' }, { valor: 'ativos', rotulo: 'Ativos' }, { valor: 'inativos', rotulo: 'Inativos' }]
const ICONE = { clientes: 'usuarios', produtos: 'package', fornecedores: 'house', bancos: 'dollar-sign' }
const CHAVE_LEITURA = { produtos: 'verCadastrosProdutos', fornecedores: 'verCadastrosFornecedores', bancos: 'verCadastrosFinanceiros', pagamentos: 'verCadastrosFinanceiros' }
const CHAVE_EDICAO = { produtos: 'editarCadastrosProdutos', fornecedores: 'editarCadastrosFornecedores', bancos: 'gerenciarCadastrosFinanceiros' }
const nome = (c) => c.nome || c.contatoInformado?.nome || 'Contato sem nome'
// #2457 (U2): telefone na máscara da tela, não no E.164 que a API grava.
const telefone = (item) => telefoneNaTela(telefoneCadastro(item))

export function PainelCadastros({ tela, respostas, impressao, aoAbrirConversa }) {
  const { selecionada, conversas } = useAtendimento()
  const [contextoFixado, fixarContexto] = useState(undefined)
  const [contextoDaConsulta, fixarConsulta] = useState(undefined)
  const { acoes } = useAcessoModulos()
  const contexto = contextoCadastro()
  const permitida = !CHAVE_LEITURA[tela] || acoes[CHAVE_LEITURA[tela]] === true
  if (!permitida) return <Vazio titulo="Esta área precisa de outro acesso">Peça à pessoa responsável pelos cadastros para conferir estes dados.</Vazio>
  if (tela === 'modelos') return <ModelosCadastro contexto={contexto} respostas={respostas} impressao={impressao} />
  if (tela === 'pagamentos') return <MetodosCadastro contexto={contexto} />
  const contextoAtivo = contextoFixado === undefined ? contextoDaConsulta : contextoFixado
  const conversa = contextoAtivo === undefined || contextoAtivo?.id === selecionada?.id ? selecionada : contextoAtivo
  return <ListaCadastro key={`${tela}:${contexto.identidade}:${conversa?.id ?? 'geral'}`} tela={tela} contexto={contexto} conversa={conversa} conversas={conversas} acoes={acoes}
    aoFixarContexto={fixar => fixarContexto(fixar ? conversa ?? null : undefined)} aoFixarConsulta={() => fixarConsulta(conversa ?? null)} aoAbrirConversa={aoAbrirConversa} />
}

function ListaCadastro({ tela, contexto, conversa, conversas, acoes, aoFixarContexto, aoFixarConsulta, aoAbrirConversa }) {
  const { selecionar, carregarClienteDaConversa } = useAcoes()
  const chave = chaveContextoCadastro(tela, conversa?.id)
  const [editor, guardarEditor] = useRascunhoDoAjuste(chave)
  const [memoria, guardarVisual] = useContextoVisualCadastro(chave, contexto.identidade, { busca: '', pagina: 1, filtro: '', tipo: 'todos', ordem: '', posicao: 0 })
  const { busca, pagina, filtro, tipo, ordem = '' } = memoria
  const definir = (valores) => {
    const { editor: novoEditor, ...visuais } = valores
    if (Object.hasOwn(valores, 'editor')) guardarEditor(novoEditor)
    if (['busca', 'pagina', 'filtro', 'tipo', 'ordem'].some(campo => Object.hasOwn(visuais, campo))) aoFixarConsulta()
    if (Object.keys(visuais).length) guardarVisual({ ...memoria, ...visuais })
  }
  const soLeads = tela === 'clientes' && tipo === 'sem-cadastro'
  const consulta = useListaCadastros(tela, { busca, pagina, filtro, ordem, relacionamento: tela === 'clientes' && ['lead', 'cliente'].includes(tipo) ? tipo : '' }, contexto, !soLeads)
  const [aviso, avisar] = useState(null)
  const [reversao, prepararReversao] = useState(null)
  const [desfazendo, iniciarDesfazer] = useGravacaoDoAjuste(`cadastros:bancos:${reversao?.id}`)
  const [abrindo, abrir] = useState(null)
  const [erro, falhar] = useState(null)
  const pedido = useRef(null)
  const tabela = useRef(null)
  const podeEditar = !CHAVE_EDICAO[tela] || acoes[CHAVE_EDICAO[tela]] === true
  const leads = filtrarSemCadastro(conversas ?? [], busca)
  const lista = soLeads ? leads : consulta.itens
  // #2495 (S1, I6): a busca de produto usa outra rota, sem ordem; ali a escolha fica parada.
  const ordenavel = !soLeads && Boolean(ORDENS_CADASTRO[tela]) && !(tela === 'produtos' && busca.trim())
  const ordenar = (valor) => definir({ ordem: valor, pagina: 1, posicao: 0 })
  useEffect(() => () => pedido.current?.abort(), [])
  useEffect(() => { if (tabela.current && !consulta.carregando) tabela.current.scrollTop = memoria.posicao ?? 0 }, [consulta.carregando])

  const abrirConversa = (id) => { selecionar(id); window.location.hash = hashDoModulo('atendimento', 'balcao'); anunciarAberturaDeConversa(id); aoAbrirConversa?.(id) }
  async function editar(item) {
    if (abrindo || desfazendo) return
    prepararReversao(null)
    aoFixarContexto(true)
    falhar(null); abrir(item.id ?? item.produtoId)
    const abortar = new AbortController(); pedido.current = abortar
    try {
      const dados = tela === 'bancos' ? abrirEdicaoCadastro(tela, item) : await obterCadastro(tela, item.id ?? item.produtoId, contexto, abortar.signal)
      if (!abortar.signal.aborted) definir({ editor: { dados, inicial: dados } })
    } catch (e) { if (!abortar.signal.aborted) { falhar(e.message); aoFixarContexto(false) } }
    finally { if (!abortar.signal.aborted) abrir(null) }
  }
  const novo = () => {
    if (desfazendo) return
    prepararReversao(null)
    aoFixarContexto(true)
    pedido.current?.abort(); abrir(null)
    const dados = { nome: '', tipoPessoa: 'fisica', tipo: tela === 'bancos' ? 'corrente' : 'Alimento', status: 'Inativo', ativo: true, requestId: crypto.randomUUID() }
    definir({ editor: { dados, inicial: dados, ...(tela === 'clientes' && conversa && !conversa.clienteId ? { origemConversa: { id: conversa.id, vinculoRevisao: conversa.vinculoRevisao ?? 0 } } : {}) } })
  }
  const salvo = ({ anterior, atual } = {}) => {
    prepararReversao(tela === 'bancos' && anterior?.id && atual?.id === anterior.id && Number.isInteger(atual.revisao)
      ? { ...anterior, revisao: atual.revisao, requestId: crypto.randomUUID() } : null)
    definir({ editor: null }); aoFixarContexto(false); avisar(editor?.origemConversa ? 'Cadastro salvo. Volte à conversa e confira o vínculo com a pessoa.' : 'Cadastro salvo.'); consulta.recarregar()
    if (tela === 'clientes' && conversa?.clienteId === editor?.dados.id) carregarClienteDaConversa?.(conversa.id)
  }
  async function desfazer() {
    if (!reversao || !podeEditar) return
    const concluir = iniciarDesfazer(); if (!concluir) return
    falhar(null)
    try {
      await salvarCadastro('bancos', reversao, contexto)
      prepararReversao(null); avisar(`Edição desfeita somente na conta ${reversao.nome}. As alterações dos outros cadastros foram mantidas.`); consulta.recarregar()
    } catch (e) {
      if (e.status === 409) prepararReversao(null)
      falhar(e.status === 409 ? 'A conta mudou depois desta edição. Nada foi desfeito. Abra o cadastro para conferir a versão atual.' : e.message)
    } finally { concluir() }
  }
  return <section className={css.painel} aria-label={TITULOS_CADASTROS[tela]}>
    <header className={css.cabecalho}>
      <div><h2>{TITULOS_CADASTROS[tela]}</h2><p>{({ clientes: 'Pessoas, contatos e histórico de relacionamento.', produtos: 'Dados dos produtos usados no cardápio e na operação.', fornecedores: 'Contatos e condições de compra.', bancos: 'Contas usadas pela loja.' })[tela]}</p></div>
      {podeEditar && !soLeads && <Botao variante="primario" icone="mais" onClick={novo}>{tela === 'clientes' && conversa && !conversa.clienteId ? 'Cadastrar pessoa da conversa' : TEXTOS_CADASTRO[tela].novo}</Botao>}
    </header>
    {conversa && <div className={css.contexto}><Icone nome="conversa" tamanho={20} /><span>Atendimento de <strong>{nome(conversa)}</strong></span>
      <Botao variante="texto" onClick={() => abrirConversa(conversa.id)}>Voltar à conversa</Botao>
      {tela === 'clientes' && conversa.clienteId && <Botao disabled={Boolean(abrindo)} onClick={() => editar({ id: conversa.clienteId })}>Conferir cadastro</Botao>}
      {tela === 'clientes' && !conversa.clienteId && podeEditar && <Botao onClick={novo}>Preencher novo cadastro</Botao>}
    </div>}
    {aviso && <p className={css.sucesso} role="status">{aviso}</p>}
    {reversao && podeEditar && <div className={css.contexto}><span>Desfazer restaura somente a edição da conta <strong>{reversao.nome}</strong> e registra uma nova alteração.</span><Botao disabled={desfazendo} onClick={desfazer}>{desfazendo ? 'Desfazendo…' : 'Desfazer edição desta conta'}</Botao></div>}
    {(erro || consulta.erro) && <div className={css.erro} role="alert"><p>{erro || consulta.erro}</p><Botao onClick={() => { falhar(null); consulta.recarregar() }}>Tentar de novo</Botao></div>}
    <div className={css.superficie}>
      {tela === 'clientes' && <div className={css.abas} aria-label="Pessoas">{[['todos', 'Todos'], ['lead', 'Leads'], ['cliente', 'Clientes'], ['sem-cadastro', 'Sem cadastro']].map(([valor, rotulo]) => <Botao key={valor} aria-pressed={tipo === valor} onClick={() => definir({ tipo: valor, pagina: 1, filtro: '', posicao: 0 })}>{rotulo}</Botao>)}</div>}
      <div className={!soLeads && ORDENS_CADASTRO[tela] ? `${css.filtros} ${css.comOrdem}` : css.filtros}>
        {soLeads ? <CampoTexto rotulo="Buscar" placeholder="Nome ou telefone" value={busca} onChange={e => definir({ busca: e.target.value, pagina: 1, posicao: 0 })} /> : <BuscaComSugestoes placeholder={TEXTOS_CADASTRO[tela].busca} valor={busca} aoMudar={valor => definir({ busca: valor, pagina: 1, posicao: 0 })} carregando={consulta.carregando}
          opcoes={consulta.busca === busca ? lista.slice(0, 6).map(item => ({ id: item.id ?? item.produtoId, rotulo: nome(item), descricao: telefone(item) || emailCadastro(item) || item.marca || item.instituicao, item })) : []} aoEscolher={editar} />}
        {!soLeads && ['clientes', 'produtos', 'fornecedores'].includes(tela) && <Combobox rotulo="Situação" valor={filtro} opcoes={FILTROS} aoMudar={valor => definir({ filtro: valor, pagina: 1, posicao: 0 })} />}
        {!soLeads && ORDENS_CADASTRO[tela] && <Combobox rotulo="Ordem" valor={ordem} opcoes={ORDENS_CADASTRO[tela]} disabled={!ordenavel} aoMudar={ordenar} />}
        <Botao icone="refresh-cw" disabled={consulta.carregando || soLeads} onClick={consulta.recarregar}>Atualizar</Botao>
      </div>
      {consulta.carregando && !soLeads ? <Vazio titulo="Carregando cadastros">Aguarde um instante.</Vazio> : lista.length === 0 ? <Vazio titulo={busca ? 'Nenhum resultado para esta busca' : soLeads ? 'Nenhum contato sem cadastro nesta lista de conversas' : 'Nenhum cadastro encontrado'}
        acao={busca ? <Botao onClick={() => definir({ busca: '', pagina: 1, posicao: 0 })}>Limpar busca</Botao> : !soLeads && podeEditar ? <Botao variante="primario" icone="mais" onClick={novo}>{TEXTOS_CADASTRO[tela].novo}</Botao> : undefined}>
        {busca ? 'Tente outro nome ou contato.' : soLeads ? 'As pessoas que ainda não têm cadastro aparecem aqui ao chegar no Atendimento.' : TEXTOS_CADASTRO[tela].vazio}</Vazio> : <div ref={tabela} className={css.lista} onScroll={e => definir({ posicao: e.currentTarget.scrollTop })}>
        <table><thead><tr>{ordenavel ? <th aria-sort={sortDoNome(tela, ordem)}><button type="button" className={css.ordenar} onClick={() => ordenar(ordemDoCabecalhoNome(tela, ordem))}>{tela === 'produtos' ? 'Produto' : 'Nome'}<Icone nome={({ ascending: 'arrow-up', descending: 'arrow-down' })[sortDoNome(tela, ordem)] ?? 'arrow-up-down'} tamanho={16} /></button></th>
          : <th>{tela === 'produtos' ? 'Produto' : tela === 'bancos' ? 'Conta' : 'Nome'}</th>}<th>{tela === 'produtos' ? 'Preço' : tela === 'bancos' ? 'Instituição' : 'Contato'}</th><th>Situação</th><th><span className="sr">Ações</span></th></tr></thead><tbody>{lista.map(item => {
          const id = item.id ?? item.produtoId
          const c = soLeads ? item : (conversas ?? []).find(c => c.clienteId === id)
          const situacao = situacaoDaLinha(tela, item, soLeads)
          // #2457 (U9): a linha inteira abre o cadastro; o botão Editar continua sendo o alvo do teclado.
          return <tr key={id} className={soLeads ? undefined : css.clicavel} onClick={soLeads ? undefined : e => { if (!e.target.closest('button, a')) editar(item) }}>
            <td><div className={css.identidade}><span className={css.icone}><Icone nome={ICONE[tela]} tamanho={20} /></span><div><strong>{nome(item)}</strong><small>{soLeads ? item.canal : item.nomeFantasia || item.marca || item.contato || item.titular || (item.orderCount ? `${item.orderCount} pedido${item.orderCount === 1 ? '' : 's'}` : '')}</small><small className={css.contatoMovel}>{tela === 'produtos' ? moeda(item.precoReferencia ?? 0) : telefone(item) || emailCadastro(item) || item.instituicao}</small></div></div></td>
            <td>{tela === 'produtos' ? moeda(item.precoReferencia ?? 0) : tela === 'bancos' ? item.instituicao || 'Não informada' : <><span>{telefone(item) || emailCadastro(item) || 'Contato não informado'}</span>{telefone(item) && <small>{emailCadastro(item)}</small>}</>}</td>
            <td>{situacao.tom ? <Pilula tom={situacao.tom}>{situacao.texto}</Pilula> : <span className={css.situacao}>{situacao.texto}</span>}</td>
            <td><div className={css.acoes}>{!soLeads && <Botao disabled={Boolean(abrindo)} onClick={() => editar(item)} aria-label={`${podeEditar ? 'Editar' : 'Ver'} ${nome(item)}`}>{abrindo === id ? 'Abrindo…' : podeEditar ? 'Editar' : 'Ver'}</Botao>}{c && <Botao variante="texto" onClick={() => abrirConversa(c.id)}>Conversa</Botao>}</div></td>
          </tr>
        })}</tbody></table>
      </div>}
      <footer className={css.rodape}><span>{soLeads ? `${leads.length} contatos sem cadastro nas conversas carregadas` : consulta.pesquisaLimitada ? `${lista.length} resultados, até 100 por busca` : `${consulta.total} cadastros`}</span>{!soLeads && !consulta.pesquisaLimitada && tela !== 'bancos' && <div className={css.acoes}><Botao disabled={pagina <= 1 || consulta.carregando} onClick={() => definir({ pagina: pagina - 1, posicao: 0 })}>Anterior</Botao><span>Página {pagina}</span><Botao disabled={!temProximaPagina(pagina, consulta.total) || consulta.carregando} onClick={() => definir({ pagina: pagina + 1, posicao: 0 })}>Próxima</Botao></div>}</footer>
    </div>
    {tela === 'produtos' && <div className={css.links}><a href={hashDoModulo('cardapio', 'itens')}>Abrir itens do cardápio</a><a href={hashDoModulo('producao', 'estoque')}>Conferir estoque do dia</a></div>}
    {editor && <EditorCadastro tela={tela} editor={editor} aoMudar={valor => definir({ editor: valor })} contexto={contexto} conversa={tela === 'clientes' && ((editor.dados.id && conversa?.clienteId === editor.dados.id) || (!editor.dados.id && !conversa?.clienteId && editor.origemConversa?.id === conversa?.id && editor.origemConversa?.vinculoRevisao === (conversa?.vinculoRevisao ?? 0))) ? conversa : null} podeEditar={podeEditar} aoFechar={() => { definir({ editor: null }); aoFixarContexto(false) }} aoSalvo={salvo} />}
  </section>
}
