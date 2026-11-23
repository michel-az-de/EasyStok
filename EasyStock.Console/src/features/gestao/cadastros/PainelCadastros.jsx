import { useContext, useEffect, useId, useRef, useState } from 'react'
import { ContextoCatalogo, useAcoes, useAtendimento } from '../../../aplicacao/contextos'
import { anunciarAberturaDeConversa } from '../../../aplicacao/aberturaDeConversa'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { useNovoPedidoDoCliente } from '../../../aplicacao/novoPedidoDoCliente'
import { useConviteDeRecompra } from '../../../aplicacao/conviteDeRecompra'
import { useGravacaoDoAjuste, useRascunhoDoAjuste } from '../../../aplicacao/rascunhosDosAjustes'
import { contextoCadastro, obterCadastro, salvarCadastro, useContextoVisualCadastro, useListaCadastros } from '../../../aplicacao/cadastros'
import { useFotoCliente } from '../../../aplicacao/useFotoCliente'
import { useChegouNaTela } from '../../../hooks/useChegouNaTela'
import { Avatar } from '../../../componentes/Avatar'
import { BarraDeAbas } from '../../../componentes/BarraDeAbas'
import { DURACAO_COM_ACAO_MS, useAviso } from '../../../componentes/Aviso'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { BuscaComSugestoes } from '../../../componentes/BuscaComSugestoes'
import { Combobox } from '../../../componentes/Combobox'
import { Icone } from '../../../componentes/Icone'
import { Pilula } from '../../../componentes/Pilula'
import { EsqueletoDeLista } from '../../../componentes/Esqueleto'
import { Vazio } from '../../../componentes/Vazio'
import { linkDoConvite, resumoDaRecompra } from '../../../dominio/recompra'
import { hashDoModulo } from '../../../dominio/modulos'
import { clienteDaHash } from '../../../dominio/fichaCliente'
import { conversasDoCliente } from '../../../dominio/conversa'
import { moeda } from '../../../dominio/formato'
import { nomeExibido } from '../../../dominio/nomeDoContato'
import { abrirEdicaoCadastro, chaveContextoCadastro, emailCadastro, emAtividadeCadastro, filtrarSemCadastro, leituraDaLista, ordemDoCabecalhoNome, ORDENS_CADASTRO, situacaoDaLinha, sortDoNome, telefoneCadastro, telefoneNaTela, temProximaPagina, TEXTOS_CADASTRO, TITULOS_CADASTROS } from '../../../dominio/cadastros'
import { EditorCadastro } from './EditorCadastro'
import { PedirCelularDoPedido } from './CelularDoPedido'
import { useRolagemDaLista } from './rolagemDaLista'
import { CategoriasCadastro } from './CategoriasCadastro'
import { ModelosCadastro } from './ModelosCadastro'
import { MetodosCadastro } from './MetodosCadastro'
import css from './cadastros.module.css'

const FILTROS = [{ valor: '', rotulo: 'Todas' }, { valor: 'ativos', rotulo: 'Ativos' }, { valor: 'inativos', rotulo: 'Inativos' }]
const ABAS_DE_PESSOAS = [['todos', 'Todos'], ['lead', 'Sem pedido'], ['cliente', 'Clientes'], ['recompra', 'Recompra'], ['sem-cadastro', 'Sem cadastro']].map(([id, rotulo]) => ({ id, rotulo, nome: rotulo }))
// #3241: foto do cliente (ou iniciais) na linha. A foto vem de download autenticado, então só baixa quando a linha chega perto da área visível.
function FotoDaLinha({ nome, fotoUrl }) {
  const ref = useRef(null)
  const foto = useFotoCliente(useChegouNaTela(ref) ? fotoUrl : null)
  return <span ref={ref} className={css.fotoDaLinha}><Avatar nome={nome} foto={foto.url} tamanho="medio" /></span>
}

const ICONE = { clientes: 'usuarios', produtos: 'package', fornecedores: 'house', bancos: 'dollar-sign' }
const CHAVE_LEITURA = { produtos: 'verCadastrosProdutos', categorias: 'verCadastrosProdutos', fornecedores: 'verCadastrosFornecedores', bancos: 'verCadastrosFinanceiros', pagamentos: 'verCadastrosFinanceiros' }
const CHAVE_EDICAO = { produtos: 'editarCadastrosProdutos', fornecedores: 'editarCadastrosFornecedores', bancos: 'gerenciarCadastrosFinanceiros' }
const nome = (c) => nomeExibido({ nome: c.nome || c.contatoInformado?.nome, telefone: telefoneCadastro(c) })
// #2457 (U2): telefone na máscara da tela, não no E.164 que a API grava.
const telefone = (item) => telefoneNaTela(telefoneCadastro(item))
const SEM_RESTRICOES = []
const ID_MOTIVO_NOVO_PEDIDO = 'cadastros-motivo-novo-pedido'

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
  if (tela === 'categorias') return <CategoriasCadastro contexto={contexto} />
  const contextoAtivo = contextoFixado === undefined ? contextoDaConsulta : contextoFixado
  const conversa = contextoAtivo === undefined || contextoAtivo?.id === selecionada?.id ? selecionada : contextoAtivo
  return <ListaCadastro key={`${tela}:${contexto.identidade}:${conversa?.id ?? 'geral'}`} tela={tela} contexto={contexto} conversa={conversa} conversas={conversas} acoes={acoes}
    aoFixarContexto={fixar => fixarContexto(fixar ? conversa ?? null : undefined)} aoFixarConsulta={() => fixarConsulta(conversa ?? null)} aoAbrirConversa={aoAbrirConversa} />
}

function ListaCadastro({ tela, contexto, conversa, conversas, acoes, aoFixarContexto, aoFixarConsulta, aoAbrirConversa }) {
  const { selecionar, relerClienteDaConversa, abrirConversaDoCliente } = useAcoes()
  const { conta } = useAcessoModulos()
  // #2734: as restrições do catálogo destacam a tag de restrição no editor, como na Ficha. A lista de cadastros
  // funciona sem o catálogo; só o destaque depende dele, por isso a leitura não exige o provider.
  const restricoes = useContext(ContextoCatalogo)?.restricoes ?? SEM_RESTRICOES
  const idTitulo = useId()
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
  // #2946: a lista de recompra é do modo API (a tag mora na API); a ordem é fixa, quem espera há mais tempo vem primeiro.
  const emRecompra = tela === 'clientes' && tipo === 'recompra' && Boolean(abrirConversaDoCliente)
  const abas = abrirConversaDoCliente ? ABAS_DE_PESSOAS : ABAS_DE_PESSOAS.filter(a => a.id !== 'recompra')
  // #2871: aberto o pedido no Atendimento, a ficha fecha; voltar a Clientes não a reabre como rascunho.
  const novoPedido = useNovoPedidoDoCliente({ aoAbrir: () => { pedirCelular(null) } })
  const podeNovoPedido = tela === 'clientes' && !soLeads && novoPedido.disponivel
  // #2930: cliente sem telefone pede o celular antes de abrir o pedido, em vez de parar no erro do topo.
  const [celularDe, pedirCelular] = useState(null)
  const consulta = useListaCadastros(tela, { busca, pagina, filtro: emRecompra ? '' : filtro, ordem: emRecompra ? 'sem-comprar' : ordem, relacionamento: emRecompra ? 'recompra' : tela === 'clientes' && ['lead', 'cliente'].includes(tipo) ? tipo : '' }, contexto, !soLeads)
  // #3076: o sucesso vai ao Aviso, que some sozinho; só o que pede ação (endereço não gravado) fica na tela.
  const avisoDaTela = useAviso()
  // Orientação ("Volte à conversa…") pede leitura: fica o tempo do aviso com ação.
  const avisar = (texto, { orienta = false } = {}) => avisoDaTela.mostrar({ texto, tom: 'sucesso', ...(orienta ? { duracao: DURACAO_COM_ACAO_MS } : {}) })
  const [pendencia, anotarPendencia] = useState(null)
  const recompra = useConviteDeRecompra({ aoMudar: () => consulta.recarregar() })
  const [reversao, prepararReversao] = useState(null)
  const [desfazendo, iniciarDesfazer] = useGravacaoDoAjuste(`cadastros:bancos:${reversao?.id}`)
  const [abrindo, abrir] = useState(null)
  const [erro, falhar] = useState(null)
  const pedido = useRef(null)
  const tabela = useRef(null)
  const podeEditar = !CHAVE_EDICAO[tela] || acoes[CHAVE_EDICAO[tela]] === true
  const leads = filtrarSemCadastro(conversas ?? [], busca)
  const lista = soLeads ? leads : consulta.itens
  const leitura = leituraDaLista(consulta, soLeads)
  // #2495 (S1, I6): a busca de produto usa outra rota, sem ordem; ali a escolha fica parada.
  const ordenavel = !soLeads && !emRecompra && Boolean(ORDENS_CADASTRO[tela]) && !(tela === 'produtos' && busca.trim())
  const comFiltro = Boolean(filtro) || tipo !== 'todos'
  const ordenar = (valor) => definir({ ordem: valor, pagina: 1, posicao: 0 })
  useEffect(() => () => pedido.current?.abort(), [])
  // #2792: uma rolagem só, a da página; a posição da lista é a dela.
  useRolagemDaLista(tabela, { posicao: memoria.posicao, aoRolar: posicao => definir({ posicao }), pronto: !consulta.carregando, comLista: lista.length > 0 })

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
  // #2734 (C-11): a Ficha do Atendimento não relê a pessoa sozinha. Toda conversa carregada desse cliente relê o
  // cadastro, não só a que está selecionada; a do contexto entra também quando o cadastro nasceu dela (sem vínculo).
  // A leitura pedida é nova: a que já estava em voo pode ter saído do servidor antes da gravação.
  const relerConversasDoCliente = (clienteId) => {
    if (tela !== 'clientes') return
    const alvos = new Set(conversasDoCliente(conversas, clienteId))
    if (conversa && conversa.clienteId === clienteId) alvos.add(conversa.id)
    alvos.forEach(id => relerClienteDaConversa?.(id))
  }
  const salvo = ({ anterior, atual, aviso } = {}) => {
    prepararReversao(tela === 'bancos' && anterior?.id && atual?.id === anterior.id && Number.isInteger(atual.revisao)
      ? { ...anterior, revisao: atual.revisao, requestId: crypto.randomUUID() } : null)
    definir({ editor: null }); aoFixarContexto(false); const volta = editor?.origemConversa ? ' Volte à conversa e confira o vínculo com a pessoa.' : ''
    anotarPendencia(aviso ? aviso + volta : null); if (!aviso) avisar(`Cadastro salvo.${volta}`, { orienta: Boolean(volta) }); consulta.recarregar()
    relerConversasDoCliente(editor?.dados.id)
  }
  // #2495 (S2): inativar ou reativar fecha o editor, avisa e relê a lista (e as conversas desse cliente).
  const atividadeAlternada = (mensagem) => {
    definir({ editor: null }); aoFixarContexto(false); avisar(mensagem); consulta.recarregar()
    relerConversasDoCliente(editor?.dados.id)
  }
  // #2734: a Ficha do Atendimento abre o cadastro completo por `#/m/cadastros/clientes/<id>`. O id sai do endereço
  // depois de lido. Outro cadastro aberto com mudança por salvar, ou com nota ou tag sendo escrita, não é trocado:
  // o pedido espera a pessoa salvar ou descartar.
  const [daRota, pedirDaRota] = useState(() => (tela === 'clientes' ? clienteDaHash(window.location.hash) : null))
  const [rascunhoNoRelacionamento, marcarRascunho] = useState(false)
  const editorAlterado = Boolean(editor) && (rascunhoNoRelacionamento || JSON.stringify(editor.dados) !== JSON.stringify(editor.inicial))
  useEffect(() => {
    if (tela !== 'clientes') return undefined
    const ler = () => pedirDaRota(clienteDaHash(window.location.hash))
    window.addEventListener('hashchange', ler)
    return () => window.removeEventListener('hashchange', ler)
  }, [tela])
  useEffect(() => {
    if (!daRota || abrindo || desfazendo) return
    const aberto = editor?.dados.id
    if (editor && aberto !== daRota && editorAlterado) return
    pedirDaRota(null)
    window.history.replaceState(null, '', hashDoModulo('cadastros', 'clientes'))
    if (aberto !== daRota) editar({ id: daRota })
  }, [daRota, editor, abrindo, desfazendo, rascunhoNoRelacionamento])
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
  return <section className={css.painel} aria-labelledby={idTitulo}>
    <header className={css.cabecalho}>
      <div><h2 id={idTitulo}>{TITULOS_CADASTROS[tela]}</h2><p>{({ clientes: 'Pessoas, contatos e histórico de relacionamento.', produtos: 'Dados dos produtos usados no cardápio e na operação.', fornecedores: 'Contatos e condições de compra.', bancos: 'Contas usadas pela loja.' })[tela]}</p></div>
      {podeEditar && !soLeads && <Botao variante="primario" icone="mais" onClick={novo}>{tela === 'clientes' && conversa && !conversa.clienteId ? 'Cadastrar pessoa da conversa' : TEXTOS_CADASTRO[tela].novo}</Botao>}
    </header>
    {conversa && <div className={css.contexto}><Icone nome="conversa" tamanho={20} /><span>Atendimento de <strong>{nome(conversa)}</strong></span>
      <Botao variante="texto" onClick={() => abrirConversa(conversa.id)}>Voltar à conversa</Botao>
      {tela === 'clientes' && conversa.clienteId && <Botao disabled={Boolean(abrindo)} onClick={() => editar({ id: conversa.clienteId })}>Conferir cadastro</Botao>}
      {tela === 'clientes' && !conversa.clienteId && podeEditar && <Botao onClick={novo}>Preencher novo cadastro</Botao>}
    </div>}
    {pendencia && <div className={css.erro} role="alert"><p>{pendencia}</p><Botao onClick={() => anotarPendencia(null)}>Entendi</Botao></div>}
    {emRecompra && recompra.convite && <div className={css.contexto} role="status"><span>Convite aberto para <strong>{recompra.convite.nome}</strong>. Sai da lista por 30 dias.</span><Botao variante="texto" onClick={recompra.desfazer} aria-label={`Desfazer o convite de ${recompra.convite.nome}`}>Desfazer</Botao></div>}
    {emRecompra && recompra.erro && <div className={css.erro} role="alert"><p>{recompra.erro}</p></div>}
    {reversao && podeEditar && <div className={css.contexto}><span>Desfazer restaura somente a edição da conta <strong>{reversao.nome}</strong> e registra uma nova alteração.</span><Botao disabled={desfazendo} onClick={desfazer}>{desfazendo ? 'Desfazendo…' : 'Desfazer edição desta conta'}</Botao></div>}
    {novoPedido.erro && !editor && !celularDe && <div ref={novoPedido.refDoErro} tabIndex={-1} className={css.erro} role="alert"><p>{novoPedido.erro}</p><Botao onClick={novoPedido.limparErro}>Entendi</Botao></div>}
    {(erro || consulta.erro) && <div className={css.erro} role="alert"><p>{erro || consulta.erro}</p><Botao onClick={() => { falhar(null); consulta.recarregar() }}>Tentar de novo</Botao></div>}
    <div className={css.superficie}>
      {/* #2792: abas de verdade (tablist, aria-selected e setas), como em Modelos. */}
      {tela === 'clientes' && <BarraDeAbas rotulo="Tipos de cadastro" abas={abas} ativa={tipo} aoTrocar={valor => definir({ tipo: valor, pagina: 1, filtro: '', posicao: 0 })} idPainel="cadastros-painel" prefixoId="cadastros-aba" />}
      <div id="cadastros-painel" role={tela === 'clientes' ? 'tabpanel' : undefined} aria-labelledby={tela === 'clientes' ? `cadastros-aba-${tipo}` : undefined}>
      {/* #3193: loja desligada, o motivo aparece uma vez aqui e cada "Novo pedido" da lista aponta para ele. */}
      {podeNovoPedido && novoPedido.motivo && lista.length > 0 && <p id={ID_MOTIVO_NOVO_PEDIDO} className={css.contexto} role="status">{novoPedido.motivo}</p>}
      <div className={!soLeads && !emRecompra && ORDENS_CADASTRO[tela] ? `${css.filtros} ${css.comOrdem}` : tela === 'bancos' ? `${css.filtros} ${css.soBusca}` : css.filtros}>
        {soLeads ? <CampoTexto rotulo="Buscar" placeholder="Nome ou telefone" value={busca} onChange={e => definir({ busca: e.target.value, pagina: 1, posicao: 0 })} /> : <BuscaComSugestoes placeholder={TEXTOS_CADASTRO[tela].busca} valor={busca} aoMudar={valor => definir({ busca: valor, pagina: 1, posicao: 0 })} carregando={consulta.carregando}
          opcoes={consulta.busca === busca ? lista.slice(0, 6).map(item => ({ id: item.id ?? item.produtoId, rotulo: nome(item), descricao: telefone(item) || emailCadastro(item) || item.marca || item.instituicao, item })) : []} aoEscolher={editar} />}
        {!soLeads && !emRecompra && ['clientes', 'produtos', 'fornecedores'].includes(tela) && <Combobox rotulo="Situação" valor={filtro} opcoes={FILTROS} aoMudar={valor => definir({ filtro: valor, pagina: 1, posicao: 0 })} />}
        {!soLeads && !emRecompra && ORDENS_CADASTRO[tela] && <Combobox rotulo="Ordem" valor={ordem} opcoes={ORDENS_CADASTRO[tela]} disabled={!ordenavel} aoMudar={ordenar} />}
        <Botao icone="refresh-cw" disabled={consulta.carregando || soLeads} motivo={consulta.carregando ? 'Atualizando…' : 'Esta lista não recarrega'} onClick={consulta.recarregar}>Atualizar</Botao>
      </div>
      {leitura === 'carregando' && lista.length === 0 ? <EsqueletoDeLista linhas={6}>Carregando cadastros…</EsqueletoDeLista> : leitura === 'falhou' ? null : lista.length === 0 ? <Vazio titulo={busca ? 'Nenhum resultado para esta busca' : emRecompra ? 'Ninguém para chamar agora' : soLeads ? 'Nenhum contato sem cadastro nesta lista de conversas' : comFiltro ? 'Nenhum cadastro com estes filtros' : TEXTOS_CADASTRO[tela].semItens}
        acao={busca ? <Botao onClick={() => definir({ busca: '', pagina: 1, posicao: 0 })}>Limpar busca</Botao> : comFiltro && !soLeads && !emRecompra ? <Botao onClick={() => definir({ busca: '', filtro: '', tipo: 'todos', pagina: 1, posicao: 0 })}>Limpar filtros</Botao> : undefined}>
        {busca ? 'Tente outro nome ou contato.' : emRecompra ? 'Aparece aqui quem já comprou 2 vezes ou mais e está há 30 dias sem pedir.' : soLeads ? 'As pessoas que ainda não têm cadastro aparecem aqui ao chegar no Atendimento.' : comFiltro ? (tela === 'clientes' ? 'Tente outra aba ou situação.' : 'Tente outra situação.') : TEXTOS_CADASTRO[tela].vazio}</Vazio> : <div ref={tabela} className={css.lista} aria-busy={leitura === 'carregando'}>
        <table><caption className="sr">{TITULOS_CADASTROS[tela]}</caption><thead><tr>{ordenavel ? <th scope="col" aria-sort={sortDoNome(tela, ordem)}><button type="button" className={css.ordenar} onClick={() => ordenar(ordemDoCabecalhoNome(tela, ordem))}>{tela === 'produtos' ? 'Produto' : 'Nome'}<Icone nome={({ ascending: 'arrow-up', descending: 'arrow-down' })[sortDoNome(tela, ordem)] ?? 'arrow-up-down'} tamanho={16} /></button></th>
          : <th scope="col">{tela === 'produtos' ? 'Produto' : tela === 'bancos' ? 'Conta' : 'Nome'}</th>}<th scope="col">{tela === 'produtos' ? 'Preço' : tela === 'bancos' ? 'Instituição' : 'Contato'}</th><th scope="col">Situação</th><th scope="col"><span className="sr">Ações</span></th></tr></thead><tbody>{lista.map(item => {
          const id = item.id ?? item.produtoId
          const c = soLeads ? item : (conversas ?? []).find(c => c.clienteId === id)
          const situacao = situacaoDaLinha(tela, item, soLeads)
          const link = emRecompra ? linkDoConvite(telefoneCadastro(item), nome(item) === 'Contato sem nome' ? '' : nome(item), conta?.casa) : null
          // #2457 (U9): a linha inteira abre o cadastro; o botão Editar continua sendo o alvo do teclado.
          return <tr key={id} className={soLeads ? undefined : css.clicavel} onClick={soLeads ? undefined : e => { if (!e.target.closest('button, a')) editar(item) }}>
            <td><div className={css.identidade}>{tela === 'clientes' ? <FotoDaLinha nome={nome(item)} fotoUrl={item.fotoUrl} /> : <span className={css.icone}><Icone nome={ICONE[tela]} tamanho={20} /></span>}<div><strong>{nome(item)}</strong><small>{soLeads ? item.canal : emRecompra ? resumoDaRecompra(item.orderCount, item.lastOrderAt) : item.nomeFantasia || item.marca || item.contato || item.titular || (item.orderCount ? `${item.orderCount} pedido${item.orderCount === 1 ? '' : 's'}` : '')}</small><small className={css.contatoMovel}>{tela === 'produtos' ? moeda(item.precoReferencia ?? 0) : telefone(item) || emailCadastro(item) || item.instituicao}</small></div></div></td>
            <td>{tela === 'produtos' ? moeda(item.precoReferencia ?? 0) : tela === 'bancos' ? item.instituicao || 'Não informada' : <><span>{telefone(item) || emailCadastro(item) || 'Contato não informado'}</span>{telefone(item) && <small>{emailCadastro(item)}</small>}</>}</td>
            <td>{situacao.tom ? <Pilula tom={situacao.tom} titulo={situacao.titulo}>{situacao.rotulo ?? situacao.texto}</Pilula> : <span className={css.situacao}>{situacao.texto}</span>}</td>
            <td><div className={css.acoes}>{emRecompra && (link ? <a className={css.chamar} href={link} target="_blank" rel="noopener noreferrer" aria-label={`Chamar ${nome(item)} no WhatsApp`} onClick={() => recompra.convidar(id, nome(item))} onAuxClick={e => e.button === 1 && recompra.convidar(id, nome(item))}>Chamar no WhatsApp</a> : <span className={css.situacao}>Sem telefone</span>)}{!soLeads && <Botao disabled={Boolean(abrindo)} onClick={() => editar(item)} aria-label={`${tela === 'clientes' ? 'Abrir ficha de' : podeEditar ? 'Editar' : 'Ver'} ${nome(item)}`}>{abrindo === id ? 'Abrindo…' : tela === 'clientes' ? 'Abrir ficha' : podeEditar ? 'Editar' : 'Ver'}</Botao>}{podeNovoPedido && emAtividadeCadastro(item) && <Botao disabled={Boolean(novoPedido.abrindo || novoPedido.motivo)} title={novoPedido.motivo ?? undefined} aria-describedby={novoPedido.motivo ? ID_MOTIVO_NOVO_PEDIDO : undefined} onClick={() => (telefoneCadastro(item) ? novoPedido.iniciar(id) : pedirCelular({ id, nome: nome(item) }))} aria-label={`Novo pedido de ${nome(item)}`}>{novoPedido.abrindo === id ? 'Abrindo…' : novoPedido.rotulo(id)}</Botao>}{c && <Botao variante="texto" onClick={() => abrirConversa(c.id)}>Conversa</Botao>}</div></td>
          </tr>
        })}</tbody></table>
      </div>}
      <footer className={css.rodape}><span>{leitura === 'falhou' ? 'Lista não carregada' : soLeads ? `${leads.length} contatos sem cadastro nas conversas carregadas` : consulta.pesquisaLimitada ? `${lista.length} resultados, até 100 por busca` : `${consulta.total} ${consulta.total === 1 ? TEXTOS_CADASTRO[tela].itens.replace(/s$/, '') : TEXTOS_CADASTRO[tela].itens}`}</span>{leitura !== 'falhou' && !soLeads && !consulta.pesquisaLimitada && tela !== 'bancos' && (pagina > 1 || temProximaPagina(pagina, consulta.total)) && <div className={css.acoes}><Botao disabled={pagina <= 1 || consulta.carregando} onClick={() => definir({ pagina: pagina - 1, posicao: 0 })}>Anterior</Botao><span>Página {pagina}</span><Botao disabled={!temProximaPagina(pagina, consulta.total) || consulta.carregando} onClick={() => definir({ pagina: pagina + 1, posicao: 0 })}>Próxima</Botao></div>}</footer>
      </div>
    </div>
    {celularDe && <PedirCelularDoPedido cliente={celularDe} novoPedido={novoPedido} aoFechar={() => { pedirCelular(null); novoPedido.limparErro() }} />}
    {tela === 'produtos' && <div className={css.links}><a href={hashDoModulo('cardapio', 'itens')}>Abrir itens do cardápio</a><a href={hashDoModulo('producao', 'estoque')}>Conferir estoque do dia</a></div>}
    {editor && <EditorCadastro tela={tela} editor={editor} aoMudar={valor => definir({ editor: valor })} contexto={contexto} conversa={tela === 'clientes' && ((editor.dados.id && conversa?.clienteId === editor.dados.id) || (!editor.dados.id && !conversa?.clienteId && editor.origemConversa?.id === conversa?.id && editor.origemConversa?.vinculoRevisao === (conversa?.vinculoRevisao ?? 0))) ? conversa : null} podeEditar={podeEditar} aoFechar={() => { definir({ editor: null }); aoFixarContexto(false) }} aoSalvo={salvo} aoAlternarAtividade={atividadeAlternada} aoAbrirCadastro={item => { definir({ editor: null }); editar(item) }}
      conversasNoAtendimento={(conversas ?? []).map(c => c.id)} aoAbrirConversa={id => { definir({ editor: null }); aoFixarContexto(false); abrirConversa(id) }} aoGravarRelacionamento={() => relerConversasDoCliente(editor.dados.id)} aoRascunharRelacionamento={marcarRascunho} restricoes={restricoes} novoPedido={podeNovoPedido ? novoPedido : null} />}
  </section>
}
