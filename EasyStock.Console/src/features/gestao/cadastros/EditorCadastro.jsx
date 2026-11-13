import { useEffect, useId, useRef, useState } from 'react'
import { listarCategoriasCadastro, obterCadastro, salvarCadastro } from '../../../aplicacao/cadastros'
import { useSugestaoCadastro } from '../../../aplicacao/useSugestaoCadastro'
import { useGravacaoDoAjuste } from '../../../aplicacao/rascunhosDosAjustes'
import { CampoArea, CampoTexto } from '../../../componentes/Campo'
import { CampoReais } from '../../../componentes/CampoReais'
import { Combobox } from '../../../componentes/Combobox'
import { Modal } from '../../../componentes/Modal'
import { Botao } from '../../../componentes/Botao'
import { lerMoeda } from '../../../dominio/formato'
import { CAMPOS_CLIENTE, mudarDadosCadastro, reconferirCadastroCliente } from '../../../dominio/cadastros'
import css from './cadastros.module.css'

const pessoa = [{ valor: 'fisica', rotulo: 'Pessoa física' }, { valor: 'juridica', rotulo: 'Pessoa jurídica' }]
const tiposProduto = [{ valor: 'Alimento', rotulo: 'Alimento' }, { valor: 'Fisico', rotulo: 'Produto físico' }, { valor: 'Servico', rotulo: 'Serviço' }]
const tiposConta = [{ valor: 'corrente', rotulo: 'Conta corrente' }, { valor: 'poupanca', rotulo: 'Poupança' }, { valor: 'pagamento', rotulo: 'Conta de pagamento' }]
const opcoesStatus = [{ valor: 'Inativo', rotulo: 'Inativo' }, { valor: 'Ativo', rotulo: 'Ativo' }]

export function EditorCadastro({ tela, editor, aoMudar, contexto, conversa, podeEditar, aoFechar, aoSalvo }) {
  const idForm = useId()
  const { dados, inicial } = editor
  const id = dados.id ?? dados.produtoId
  const [erro, falhar] = useState(null)
  const [erros, definirErros] = useState({})
  const [categorias, definirCategorias] = useState([])
  const [erroCategorias, falharCategorias] = useState(null)
  const [relerCategorias, recarregarCategorias] = useState(0)
  const [revisaoManual, revisarFormulario] = useState(0)
  const [versaoAtual, definirVersaoAtual] = useState(null)
  const [conferindo, conferirVersao] = useState(false)
  const leituraAtual = useRef(null)
  const [salvando, iniciar] = useGravacaoDoAjuste(`cadastros:${tela}:${id ?? dados.requestId}`)
  const alterado = JSON.stringify(dados) !== JSON.stringify(inicial)
  const montado = useRef(true)
  useEffect(() => { montado.current = true; return () => { montado.current = false; leituraAtual.current?.abort() } }, [])
  const mudar = (campo, valor, manual = true) => {
    aoMudar({ ...editor, dados: mudarDadosCadastro(tela, dados, campo, valor) })
    definirErros(anteriores => ({ ...anteriores, [campo]: null }))
    if (manual) revisarFormulario(r => r + 1)
  }
  const sugestao = useSugestaoCadastro({ conversa, revisaoFormulario: `${id ?? 'novo'}:${revisaoManual}`, conversaId: conversa?.id, clienteId: conversa?.clienteId, vinculoRevisao: conversa?.vinculoRevisao, aoAplicar: (campo, valor) => mudar(campo, valor, false) })
  useEffect(() => {
    if (tela !== 'produtos') return undefined
    const controlador = new AbortController()
    listarCategoriasCadastro(contexto, controlador.signal).then(
      itens => { if (!controlador.signal.aborted) { definirCategorias(itens ?? []); falharCategorias(null) } },
      e => { if (!controlador.signal.aborted) falharCategorias(e.message) },
    )
    return () => controlador.abort()
  }, [tela, contexto.identidade, relerCategorias])
  const campo = (chave, rotulo, extras = {}) => <CampoTexto key={chave} rotulo={rotulo} value={dados[chave] ?? ''} onChange={e => mudar(chave, e.target.value)} erro={erros[chave]} {...extras} />
  const selecao = (chave, rotulo, opcoes) => <Combobox rotulo={rotulo} valor={dados[chave]} aoMudar={valor => mudar(chave, valor)} opcoes={opcoes} erro={erros[chave]} disabled={salvando || !podeEditar} />
  const dinheiro = (chave, rotulo) => <CampoReais rotulo={rotulo} centavos={dados[chave] == null ? null : Math.round(dados[chave] * 100)} aoMudar={valor => mudar(chave, valor == null ? null : valor / 100)} ler={lerMoeda} erro={erros[chave]} />

  async function carregarVersaoAtual() {
    leituraAtual.current?.abort()
    const controlador = new AbortController(); leituraAtual.current = controlador
    conferirVersao(true); falhar(null)
    try {
      const atual = await obterCadastro('clientes', id, contexto, controlador.signal)
      if (!controlador.signal.aborted) definirVersaoAtual(atual)
    } catch (e) { if (!controlador.signal.aborted) falhar(e.message) }
    finally { if (!controlador.signal.aborted) conferirVersao(false) }
  }
  function reconferir() {
    try {
      aoMudar(reconferirCadastroCliente(editor, versaoAtual)); definirVersaoAtual(null); falhar(null); revisarFormulario(r => r + 1)
    } catch (e) { falhar(e.message) }
  }

  async function salvar(evento) {
    evento.preventDefault()
    if (salvando || !podeEditar || editor.semConfirmacao || editor.conflito) return
    const faltas = {}
    if (!dados.nome?.trim()) faltas.nome = 'Informe o nome.'
    if (tela === 'produtos' && !dados.categoriaId) faltas.categoriaId = 'Escolha uma categoria.'
    if (tela === 'produtos' && dados.status === 'Ativo' && !(dados.precoReferencia > 0)) faltas.precoReferencia = 'Informe o preço para ativar o produto.'
    if (Object.keys(faltas).length) { definirErros(faltas); return }
    const concluir = iniciar(); if (!concluir) return
    falhar(null)
    try {
      const atual = await salvarCadastro(tela, dados, contexto)
      if (montado.current) aoSalvo({ anterior: inicial, atual })
    } catch (e) {
      if (!montado.current) return
      const semConfirmacao = !id && tela !== 'bancos' && (e.status === 0 || e.status >= 500)
      const conflitoCliente = e.status === 409 && tela === 'clientes' && id
      falhar(e.status === 409 ? conflitoCliente ? 'Este cadastro mudou. O que você digitou continua aqui. Confira a versão atual antes de salvar novamente.' : 'Este cadastro mudou. Feche e abra novamente para conferir a versão atual. O que você digitou continua aqui.' : e.message)
      if (conflitoCliente) { aoMudar({ ...editor, conflito: true, reconferido: false }); definirVersaoAtual(null) }
      if (semConfirmacao) aoMudar({ ...editor, semConfirmacao: true })
    } finally { concluir() }
  }
  const titulo = `${podeEditar ? id ? 'Editar' : 'Novo' : 'Consultar'} ${({ clientes: 'cliente', produtos: 'produto', fornecedores: 'fornecedor', bancos: 'cadastro bancário' })[tela]}`
  return <Modal titulo={titulo} largura="md" focarCampo aoFechar={salvando ? undefined : aoFechar} segurarFechamento={salvando} confirmarDescarte={alterado && !salvando} rodape={<>
    <Botao onClick={e => e.currentTarget.closest('dialog')?.dispatchEvent(new Event('cancel', { cancelable: true }))} disabled={salvando}>{podeEditar ? 'Cancelar' : 'Fechar'}</Botao>
    {podeEditar && <Botao tipo="submit" form={idForm} variante="primario" disabled={salvando || editor.semConfirmacao || editor.conflito} motivo={editor.conflito ? 'Confira a versão atual antes de salvar novamente.' : editor.semConfirmacao ? 'Confira a lista antes de repetir um cadastro sem confirmação.' : undefined}>{salvando ? 'Salvando…' : 'Salvar cadastro'}</Botao>}
  </>}>
    {erro && <p className={css.erro} role="alert">{erro}</p>}
    {tela === 'clientes' && editor.conflito && <section className={css.assistente} aria-label="Conferir versão do cadastro">
      <p>Outra edição foi salva para esta pessoa. Conferir a versão atual não grava nem descarta o que você digitou.</p>
      <Botao disabled={conferindo} onClick={carregarVersaoAtual}>{conferindo ? 'Carregando versão…' : 'Conferir versão atual'}</Botao>
      {versaoAtual && <>
        <p><strong>Versão atual de {versaoAtual.nome}</strong></p>
        <dl>{Object.entries(CAMPOS_CLIENTE).filter(([campo]) => (versaoAtual[campo] ?? '') !== (inicial[campo] ?? '') || (dados[campo] ?? '') !== (inicial[campo] ?? '')).map(([campo, rotulo]) => <div className={css.sugestao} key={campo}><dt>{rotulo}</dt><dd>Atual: {String(versaoAtual[campo] ?? 'Vazio')}<br />Sua edição: {String(dados[campo] ?? 'Vazio')}</dd></div>)}</dl>
        <p>Serão mantidos os campos que você alterou. Os demais acompanharão a versão atual. Revise o formulário antes de salvar.</p>
        <Botao disabled={conferindo} onClick={reconferir}>Manter minhas alterações sobre esta versão</Botao>
      </>}
    </section>}
    {editor.reconferido && !editor.conflito && <p role="status">Versão atual conferida. Revise os campos e use Salvar cadastro para confirmar sua edição.</p>}
    {editor.semConfirmacao && <div className={css.erro} role="alert"><p>A resposta não chegou. O cadastro pode ter sido salvo. Feche e confira a lista antes de criar outro.</p></div>}
    {conversa && podeEditar && <section className={css.assistente} aria-label="Assistência ao cadastro">
      <div className={css.barra}><strong>Conferir dados da conversa</strong><Botao icone="sparkles" disabled={salvando || sugestao.carregando} onClick={sugestao.consultar}>{sugestao.carregando ? 'Consultando…' : 'Consultar com IA'}</Botao></div>
      <p>Usa até 20 mensagens recentes recebidas com texto nesta conversa para sugerir nome, telefone e e-mail.</p>
      {sugestao.erro && <p className={css.erro} role="alert">{sugestao.erro}</p>}
      {sugestao.campos.map(c => <div className={css.sugestao} key={c.campo}><span><strong>{({ nome: 'Nome', telefone: 'Telefone', email: 'E-mail' })[c.campo]}: {c.valor}</strong><q>{c.evidencia}</q></span><Botao disabled={salvando} onClick={() => sugestao.aplicar(c.campo)}>Usar este dado</Botao></div>)}
    </section>}
    <form id={idForm} onSubmit={salvar}>
      <fieldset disabled={salvando || !podeEditar} className={css.formulario}>
        {campo('nome', tela === 'bancos' ? 'Nome da conta' : 'Nome', { required: true, maxLength: tela === 'produtos' ? 180 : 150, 'data-foco-inicial': true })}
        {tela === 'clientes' && <>
          {selecao('tipoPessoa', 'Tipo de pessoa', pessoa)}
          {campo('telefone', 'Telefone', { tipo: 'tel', maxLength: 32 })}{campo('email', 'E-mail', { tipo: 'email', maxLength: 255 })}
          {campo('documento', dados.tipoPessoa === 'juridica' ? 'CNPJ' : 'CPF ou documento', { maxLength: 30 })}
          {dados.tipoPessoa === 'juridica' && <>{campo('nomeFantasia', 'Nome fantasia', { maxLength: 150 })}{campo('inscricaoEstadual', 'Inscrição estadual', { maxLength: 20 })}</>}
          {campo('endereco', 'Endereço', { maxLength: 255 })}{campo('apt', 'Apartamento ou referência', { maxLength: 32 })}
          <div className={css.larguraTotal}><CampoArea rotulo="Observações" value={dados.observacoes ?? ''} onChange={e => mudar('observacoes', e.target.value)} /></div>
        </>}
        {tela === 'fornecedores' && <>
          {campo('contato', 'Pessoa de contato', { maxLength: 150 })}{campo('telefone', 'Telefone', { tipo: 'tel', maxLength: 20 })}{campo('email', 'E-mail', { tipo: 'email', maxLength: 255 })}
          {campo('documento', 'CNPJ ou CPF', { maxLength: 30 })}{campo('categoria', 'Categoria', { maxLength: 120 })}
          {campo('tipo', 'Tipo de fornecimento', { maxLength: 60 })}{campo('siteUrl', 'Site', { tipo: 'url', maxLength: 255 })}
          <CampoTexto rotulo="Prazo de entrega (dias)" tipo="number" min="0" value={dados.leadTimeEstimadoDias ?? ''} onChange={e => mudar('leadTimeEstimadoDias', e.target.value === '' ? null : Number(e.target.value))} />
          {campo('pedidoMinimo', 'Pedido mínimo', { maxLength: 120 })}{campo('fretePadrao', 'Condição de frete', { maxLength: 120 })}
          <div className={css.larguraTotal}><CampoArea rotulo="Observações" value={dados.observacoes ?? ''} onChange={e => mudar('observacoes', e.target.value)} /></div>
        </>}
        {tela === 'produtos' && <>
          {selecao('categoriaId', 'Categoria', categorias.filter(c => !c.categoriaPaiId).map(c => ({ valor: c.id, rotulo: c.nome })))}
          {erroCategorias && <div className={css.erro}><p>{erroCategorias}</p><Botao onClick={() => recarregarCategorias(n => n + 1)}>Carregar categorias</Botao></div>}
          {selecao('tipo', 'Tipo de produto', tiposProduto)}{campo('marca', 'Marca')}
          {campo('skuBase', 'Código interno (SKU)')}{campo('codigoBarras', 'Código de barras')}
          {dinheiro('precoReferencia', 'Preço de venda (R$)')}{dinheiro('custoReferencia', 'Custo de referência (R$)')}
          {id && selecao('status', 'Situação', opcoesStatus)}
          <label className={css.check}><input type="checkbox" checked={dados.controlaValidade === true} onChange={e => mudar('controlaValidade', e.target.checked)} />Controlar validade</label>
          <div className={css.larguraTotal}><CampoArea rotulo="Descrição" value={dados.descricaoBase ?? ''} onChange={e => mudar('descricaoBase', e.target.value)} /></div>
          <div className={css.larguraTotal}><CampoArea rotulo="Observação interna" value={dados.observacaoInterna ?? ''} onChange={e => mudar('observacaoInterna', e.target.value)} /></div>
        </>}
        {tela === 'bancos' && <>
          {selecao('tipo', 'Tipo de conta', tiposConta)}{campo('instituicao', 'Instituição financeira')}{campo('agencia', 'Agência')}{campo('conta', 'Número da conta')}{campo('titular', 'Titular')}
          <label className={css.check}><input type="checkbox" checked={dados.ativo === true} onChange={e => mudar('ativo', e.target.checked)} />Conta ativa</label>
        </>}
      </fieldset>
    </form>
    {tela === 'clientes' && dados.detalhe?.alteracoes?.length > 0 && <details className={css.historico}><summary>Histórico do cadastro</summary><ul>{dados.detalhe.alteracoes.slice(0, 10).map(a => <li key={a.id}><strong>{a.campo}</strong> · {a.alteradoPorNome || 'Usuário da loja'}<small>{a.valorAntigo || 'Vazio'} → {a.valorNovo || 'Vazio'}</small></li>)}</ul></details>}
  </Modal>
}
