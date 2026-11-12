import { useEffect, useRef, useState } from 'react'
import { useAcoes, useAtendimento } from '../../../aplicacao/contextos'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { useRascunhoDoAjuste } from '../../../aplicacao/rascunhosDosAjustes'
import { contextoCadastro, obterCadastro, useContextoVisualCadastro, useListaCadastros } from '../../../aplicacao/cadastros'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { BuscaComSugestoes } from '../../../componentes/BuscaComSugestoes'
import { Combobox } from '../../../componentes/Combobox'
import { Icone } from '../../../componentes/Icone'
import { Pilula } from '../../../componentes/Pilula'
import { Vazio } from '../../../componentes/Vazio'
import { hashDoModulo } from '../../../dominio/modulos'
import { moeda } from '../../../dominio/formato'
import { chaveContextoCadastro, emailCadastro, filtrarSemCadastro, relacionamentoCadastro, telefoneCadastro, TITULOS_CADASTROS } from '../../../dominio/cadastros'
import { EditorCadastro } from './EditorCadastro'
import { ModelosCadastro } from './ModelosCadastro'
import { MetodosCadastro } from './MetodosCadastro'
import css from './cadastros.module.css'

const FILTROS = [{ valor: '', rotulo: 'Todos os cadastros' }, { valor: 'ativos', rotulo: 'Ativos' }, { valor: 'inativos', rotulo: 'Inativos' }]
const ICONE = { clientes: 'usuarios', produtos: 'package', fornecedores: 'house', bancos: 'dollar-sign' }
const CHAVE_LEITURA = { produtos: 'verCadastrosProdutos', fornecedores: 'verCadastrosFornecedores', bancos: 'verCadastrosFinanceiros', pagamentos: 'verCadastrosFinanceiros' }
const CHAVE_EDICAO = { produtos: 'editarCadastrosProdutos', fornecedores: 'editarCadastrosFornecedores', bancos: 'gerenciarCadastrosFinanceiros' }
const emAtividade = (item) => item.ativo !== false && item.status !== 'Inativo' && item.status !== 1
const nome = (c) => c.nome || c.contatoInformado?.nome || 'Contato sem nome'

export function PainelCadastros({ tela, respostas, impressao }) {
  const { selecionada, conversas } = useAtendimento()
  const { acoes } = useAcessoModulos()
  const contexto = contextoCadastro()
  const permitida = !CHAVE_LEITURA[tela] || acoes[CHAVE_LEITURA[tela]] === true
  if (!permitida) return <Vazio titulo="Esta área precisa de outro acesso">Peça à pessoa responsável pelos cadastros para conferir estes dados.</Vazio>
  if (tela === 'modelos') return <ModelosCadastro contexto={contexto} respostas={respostas} impressao={impressao} />
  if (tela === 'pagamentos') return <MetodosCadastro contexto={contexto} />
  return <ListaCadastro key={`${tela}:${contexto.identidade}:${selecionada?.id ?? 'geral'}`} tela={tela} contexto={contexto} conversa={selecionada} conversas={conversas} acoes={acoes} />
}

function ListaCadastro({ tela, contexto, conversa, conversas, acoes }) {
  const { selecionar, carregarClienteDaConversa } = useAcoes()
  const chave = chaveContextoCadastro(tela, conversa?.id)
  const [editor, guardarEditor] = useRascunhoDoAjuste(chave)
  const [memoria, guardarVisual] = useContextoVisualCadastro(chave, contexto.identidade, { busca: '', pagina: 1, filtro: '', tipo: 'todos', posicao: 0 })
  const { busca, pagina, filtro, tipo } = memoria
  const definir = (valores) => {
    const { editor: novoEditor, ...visuais } = valores
    if (Object.hasOwn(valores, 'editor')) guardarEditor(novoEditor)
    if (Object.keys(visuais).length) guardarVisual({ ...memoria, ...visuais })
  }
  const soLeads = tela === 'clientes' && tipo === 'sem-cadastro'
  const consulta = useListaCadastros(tela, { busca, pagina, filtro, relacionamento: tela === 'clientes' && ['lead', 'cliente'].includes(tipo) ? tipo : '' }, contexto, !soLeads)
  const [aviso, avisar] = useState(null)
  const [abrindo, abrir] = useState(null)
  const [erro, falhar] = useState(null)
  const pedido = useRef(null)
  const tabela = useRef(null)
  const podeEditar = !CHAVE_EDICAO[tela] || acoes[CHAVE_EDICAO[tela]] === true
  const leads = filtrarSemCadastro(conversas ?? [], busca)
  const lista = soLeads ? leads : consulta.itens
  useEffect(() => () => pedido.current?.abort(), [])
  useEffect(() => { if (tabela.current && !consulta.carregando) tabela.current.scrollTop = memoria.posicao ?? 0 }, [consulta.carregando])

  const abrirConversa = (id) => { selecionar(id); window.location.hash = hashDoModulo('atendimento', 'balcao') }
  async function editar(item) {
    if (abrindo) return
    falhar(null); abrir(item.id ?? item.produtoId)
    const abortar = new AbortController(); pedido.current = abortar
    try {
      const dados = tela === 'bancos' ? item : await obterCadastro(tela, item.id ?? item.produtoId, contexto, abortar.signal)
      if (!abortar.signal.aborted) definir({ editor: { dados, inicial: dados } })
    } catch (e) { if (!abortar.signal.aborted) falhar(e.message) }
    finally { if (!abortar.signal.aborted) abrir(null) }
  }
  const novo = () => {
    pedido.current?.abort(); abrir(null)
    const dados = { nome: '', tipoPessoa: 'fisica', tipo: tela === 'bancos' ? 'corrente' : 'Alimento', status: 'Inativo', ativo: true, requestId: crypto.randomUUID() }
    definir({ editor: { dados, inicial: dados } })
  }
  const salvo = () => {
    definir({ editor: null }); avisar('Cadastro salvo.'); consulta.recarregar()
    if (tela === 'clientes' && conversa?.clienteId === editor?.dados.id) carregarClienteDaConversa?.(conversa.id)
  }
  return <section className={css.painel} aria-label={TITULOS_CADASTROS[tela]}>
    <header className={css.cabecalho}>
      <div><p className={css.eyebrow}>CADASTROS</p><h2>{TITULOS_CADASTROS[tela]}</h2><p>{({ clientes: 'Pessoas, contatos e histórico de relacionamento.', produtos: 'Dados dos produtos usados no cardápio e na operação.', fornecedores: 'Contatos e condições de compra.', bancos: 'Contas usadas pela loja.' })[tela]}</p></div>
      {podeEditar && !soLeads && <Botao variante="primario" icone="mais" onClick={novo}>Novo {({ clientes: 'cliente', produtos: 'produto', fornecedores: 'fornecedor', bancos: 'cadastro' })[tela]}</Botao>}
    </header>
    {conversa && <div className={css.contexto}><Icone nome="conversa" tamanho={20} /><span>Atendimento de <strong>{nome(conversa)}</strong></span>
      <Botao variante="texto" onClick={() => abrirConversa(conversa.id)}>Voltar à conversa</Botao>
      {tela === 'clientes' && conversa.clienteId && <Botao disabled={Boolean(abrindo)} onClick={() => editar({ id: conversa.clienteId })}>Conferir cadastro</Botao>}
    </div>}
    {aviso && <p className={css.sucesso} role="status">{aviso}</p>}
    {(erro || consulta.erro) && <div className={css.erro} role="alert"><p>{erro || consulta.erro}</p><Botao onClick={() => { falhar(null); consulta.recarregar() }}>Tentar de novo</Botao></div>}
    <div className={css.superficie}>
      {tela === 'clientes' && <div className={css.abas} aria-label="Pessoas">{[['todos', 'Todos'], ['lead', 'Leads'], ['cliente', 'Clientes'], ['sem-cadastro', 'Sem cadastro']].map(([valor, rotulo]) => <Botao key={valor} aria-pressed={tipo === valor} onClick={() => definir({ tipo: valor, pagina: 1, filtro: '', posicao: 0 })}>{rotulo}</Botao>)}</div>}
      <div className={css.filtros}>
        {soLeads ? <CampoTexto rotulo="Buscar" placeholder="Nome ou telefone" value={busca} onChange={e => definir({ busca: e.target.value, pagina: 1, posicao: 0 })} /> : <BuscaComSugestoes placeholder={tela === 'produtos' ? 'Nome, marca, código ou SKU' : 'Nome, telefone ou documento'} valor={busca} aoMudar={valor => definir({ busca: valor, pagina: 1, posicao: 0 })} carregando={consulta.carregando}
          opcoes={consulta.busca === busca ? lista.slice(0, 6).map(item => ({ id: item.id ?? item.produtoId, rotulo: nome(item), descricao: telefoneCadastro(item) || emailCadastro(item) || item.marca || item.instituicao, item })) : []} aoEscolher={editar} />}
        {!soLeads && ['clientes', 'produtos', 'fornecedores'].includes(tela) && <Combobox rotulo="Situação" valor={filtro} opcoes={FILTROS} aoMudar={valor => definir({ filtro: valor, pagina: 1, posicao: 0 })} />}
        <Botao icone="refresh-cw" disabled={consulta.carregando || soLeads} onClick={consulta.recarregar}>Atualizar</Botao>
      </div>
      {consulta.carregando && !soLeads ? <Vazio titulo="Carregando cadastros">Aguarde um instante.</Vazio> : lista.length === 0 ? <Vazio titulo={busca ? 'Nenhum resultado para esta busca' : soLeads ? 'Nenhum contato sem cadastro nesta lista de conversas' : 'Nenhum cadastro encontrado'}>{busca ? 'Tente outro nome ou contato.' : soLeads ? 'As pessoas que ainda não têm cadastro aparecem aqui ao chegar no Atendimento.' : 'Os novos cadastros aparecerão aqui.'}</Vazio> : <div ref={tabela} className={css.lista} onScroll={e => definir({ posicao: e.currentTarget.scrollTop })}>
        <table><thead><tr><th>{tela === 'produtos' ? 'Produto' : tela === 'bancos' ? 'Conta' : 'Nome'}</th><th>{tela === 'produtos' ? 'Preço' : tela === 'bancos' ? 'Instituição' : 'Contato'}</th><th>Situação</th><th><span className="sr">Ações</span></th></tr></thead><tbody>{lista.map(item => {
          const id = item.id ?? item.produtoId
          const c = soLeads ? item : (conversas ?? []).find(c => c.clienteId === id)
          return <tr key={id}>
            <td><div className={css.identidade}><span className={css.icone}><Icone nome={ICONE[tela]} tamanho={20} /></span><div><strong>{nome(item)}</strong><small>{soLeads ? item.canal : item.nomeFantasia || item.marca || item.contato || item.titular || (item.orderCount ? `${item.orderCount} pedidos` : '')}</small><small className={css.contatoMovel}>{tela === 'produtos' ? moeda(item.precoReferencia ?? 0) : telefoneCadastro(item) || emailCadastro(item) || item.instituicao}</small></div></div></td>
            <td>{tela === 'produtos' ? moeda(item.precoReferencia ?? 0) : tela === 'bancos' ? item.instituicao || 'Não informada' : <><span>{telefoneCadastro(item) || emailCadastro(item) || 'Contato não informado'}</span>{telefoneCadastro(item) && <small>{emailCadastro(item)}</small>}</>}</td>
            <td><Pilula tom={soLeads ? 'alerta' : emAtividade(item) ? 'sucesso' : 'neutro'}>{soLeads ? 'Sem cadastro' : tela === 'clientes' && emAtividade(item) ? relacionamentoCadastro(item) : emAtividade(item) ? 'Ativo' : 'Inativo'}</Pilula></td>
            <td><div className={css.acoes}>{!soLeads && <Botao disabled={Boolean(abrindo)} onClick={() => editar(item)} aria-label={`${podeEditar ? 'Editar' : 'Ver'} ${nome(item)}`}>{abrindo === id ? 'Abrindo…' : podeEditar ? 'Editar' : 'Ver'}</Botao>}{c && <Botao variante="texto" onClick={() => abrirConversa(c.id)}>Conversa</Botao>}</div></td>
          </tr>
        })}</tbody></table>
      </div>}
      <footer className={css.rodape}><span>{soLeads ? `${leads.length} contatos sem cadastro nas conversas carregadas` : consulta.pesquisaLimitada ? `${lista.length} resultados, até 100 por busca` : `${consulta.total} cadastros`}</span>{!soLeads && !consulta.pesquisaLimitada && tela !== 'bancos' && <div className={css.acoes}><Botao disabled={pagina <= 1 || consulta.carregando} onClick={() => definir({ pagina: pagina - 1, posicao: 0 })}>Anterior</Botao><span>Página {pagina}</span><Botao disabled={pagina * 20 >= consulta.total || consulta.carregando} onClick={() => definir({ pagina: pagina + 1, posicao: 0 })}>Próxima</Botao></div>}</footer>
    </div>
    {tela === 'produtos' && <div className={css.links}><a href={hashDoModulo('cardapio', 'itens')}>Abrir itens do cardápio</a><a href={hashDoModulo('producao', 'estoque')}>Conferir estoque do dia</a></div>}
    {editor && <EditorCadastro tela={tela} editor={editor} aoMudar={valor => definir({ editor: valor })} contexto={contexto} conversa={tela === 'clientes' && conversa?.clienteId === editor.dados.id ? conversa : null} podeEditar={podeEditar} aoFechar={() => definir({ editor: null })} aoSalvo={salvo} />}
  </section>
}
