import { useEffect, useId, useRef, useState } from 'react'
import { flushSync } from 'react-dom'
import { alternarAtividadeCadastro, buscarClientesParecidos, listarCategoriasCadastro, obterCadastro, salvarCadastro, useRelacionamentoCliente } from '../../../aplicacao/cadastros'
import { useSugestaoCadastro } from '../../../aplicacao/useSugestaoCadastro'
import { useGravacaoDoAjuste } from '../../../aplicacao/rascunhosDosAjustes'
import { CampoArea, CampoTexto } from '../../../componentes/Campo'
import { CampoReais } from '../../../componentes/CampoReais'
import { Combobox } from '../../../componentes/Combobox'
import { Modal } from '../../../componentes/Modal'
import { Botao } from '../../../componentes/Botao'
import { FichaCliente } from './FichaCliente'
import { ResumoDoCliente } from './ResumoDoCliente'
import { RelacionamentoCliente } from './RelacionamentoCliente'
import { lerMoeda } from '../../../dominio/formato'
import { avisoDeDuplicado, duplicadosDoCliente, termoDeDuplicado } from '../../../dominio/clienteDuplicado'
import { bloqueiaRepeticaoDoCadastro, CAMPOS_CLIENTE, categoriasRaiz, subcategoriasDe, confirmacaoDeAtividade, emAtividadeCadastro, mensagemDeAtividade, temAtividadeNoEditor, errosDoFormularioCadastro, MENSAGEM_CADASTRO_REPETIDO, mudarDadosCadastro, OBRIGATORIOS_CADASTRO, reconferirCadastroCliente, documentoNaTela, telefoneNaTela } from '../../../dominio/cadastros'
import css from './cadastros.module.css'

const pessoa = [{ valor: 'fisica', rotulo: 'Pessoa física' }, { valor: 'juridica', rotulo: 'Pessoa jurídica' }]
const tiposProduto = [{ valor: 'Alimento', rotulo: 'Alimento' }, { valor: 'Fisico', rotulo: 'Produto físico' }, { valor: 'Servico', rotulo: 'Serviço' }]
const tiposConta = [{ valor: 'corrente', rotulo: 'Conta corrente' }, { valor: 'poupanca', rotulo: 'Poupança' }, { valor: 'pagamento', rotulo: 'Conta de pagamento' }]
const opcoesStatus = [{ valor: 'Inativo', rotulo: 'Inativo' }, { valor: 'Ativo', rotulo: 'Ativo' }]

export function EditorCadastro({ tela, editor, aoMudar, contexto, conversa, podeEditar, aoFechar, aoSalvo, aoAlternarAtividade, aoAbrirCadastro, conversasNoAtendimento, aoAbrirConversa, aoGravarRelacionamento, aoRascunharRelacionamento, restricoes, editarAoAbrir = false, complemento }) {
  const idForm = useId()
  const formRef = useRef(null)
  const { dados, inicial } = editor
  const id = dados.id ?? dados.produtoId
  // #2734: cliente já cadastrado mostra o mesmo relacionamento da Ficha do Atendimento (notas, tags, bloqueio).
  const comRelacionamento = tela === 'clientes' && Boolean(id && dados.detalhe)
  const relacionamento = useRelacionamentoCliente(comRelacionamento ? id : null, contexto)
  // #2734 (A21): o primeiro campo recusado recebe o foco; o erro embaixo dele não fica fora da tela.
  const focarPrimeiroErro = () => requestAnimationFrame(() => formRef.current?.querySelector('[aria-invalid="true"]')?.focus())
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
  // #2849: cliente já cadastrado abre como ficha, em leitura; o formulário só aparece em "Editar dados".
  // Rascunho restaurado ou conflito pendente abre direto no formulário: nada digitado fica escondido.
  const ehFicha = tela === 'clientes' && Boolean(id)
  const [editando, editar] = useState(editarAoAbrir)
  const formularioAberto = !ehFicha || editando || alterado || Boolean(editor.conflito || editor.semConfirmacao)
  // O foco vai ao primeiro campo no mesmo clique: quem digita em seguida não perde tecla para um foco atrasado.
  const abrirFormulario = () => { flushSync(() => editar(true)); formRef.current?.querySelector('[data-foco-inicial]')?.focus() }
  // #2495 (S2, I1): confirmação de inativar ou reativar; o erro fica no Modal e nada fecha.
  const [atividade, confirmarAtividade] = useState(null)
  const [alternando, alternar] = useState(false)
  const [erroAtividade, falharAtividade] = useState(null)
  const atividadeEmVoo = useRef(false)
  // #2495 (S5, B6): ao sair de Telefone ou Documento, avisa quem já tem os mesmos dígitos. Salvar segue liberado.
  const [duplicado, definirDuplicado] = useState(null)
  const [abrirOutro, confirmarAbrirOutro] = useState(null)
  const buscaDuplicado = useRef(null)
  useEffect(() => { montado.current = true; return () => { montado.current = false; leituraAtual.current?.abort(); buscaDuplicado.current?.abort() } }, [])
  const mudar = (campo, valor, manual = true) => {
    aoMudar({ ...editor, dados: mudarDadosCadastro(tela, dados, campo, valor) })
    definirErros(anteriores => ({ ...anteriores, [campo]: null }))
    // Mudar o campo tira o aviso e cancela a busca em voo: resposta velha não avisa.
    definirDuplicado(atual => atual?.campo === campo ? null : atual)
    if (campo === 'telefone' || campo === 'documento') buscaDuplicado.current?.abort()
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
  // #2457: o limite aparece enquanto a pessoa digita (U3); o obrigatório leva * no rótulo (U4).
  const limites = errosDoFormularioCadastro(tela, { ...dados, nome: dados.nome || '-', categoriaId: dados.categoriaId || '-' })
  const obrigatorio = (chave) => (OBRIGATORIOS_CADASTRO[tela] ?? []).includes(chave)
  const erroDo = (chave) => erros[chave] ?? limites[chave]
  const campo = (chave, rotulo, extras = {}) => <CampoTexto key={chave} rotulo={rotulo} obrigatorio={obrigatorio(chave)} value={dados[chave] ?? ''} onChange={e => mudar(chave, e.target.value)} erro={erroDo(chave)} {...extras} />
  // #2457 (U2): o conflito mostra o telefone como a pessoa digitou, não o E.164 gravado.
  // Voltar ao mesmo número não conta como alteração nem entra no conflito (revisão da #2460).
  const mesmoTelefone = (valor) => telefoneNaTela(valor) === telefoneNaTela(inicial.telefone)
  // #2476 (U2): CPF e CNPJ na máscara; digitar o mesmo número de volta não conta como mudança.
  const mesmoDocumento = (valor) => documentoNaTela(valor) === documentoNaTela(inicial.documento)
  const naTela = (campo, valor) => (campo === 'telefone' ? telefoneNaTela(valor) : campo === 'documento' ? documentoNaTela(valor) : String(valor ?? '')) || 'Vazio'
  const telefone = () => <CampoTexto key="telefone" rotulo="Telefone" tipo="tel" inputMode="tel" autoComplete="tel" value={telefoneNaTela(dados.telefone)} onChange={e => mudar('telefone', mesmoTelefone(e.target.value) ? inicial.telefone : e.target.value)} onBlur={() => conferirDuplicado('telefone')} erro={erroDo('telefone')} />
  const documento = (rotulo) => <CampoTexto key="documento" rotulo={rotulo} obrigatorio={obrigatorio('documento')} value={documentoNaTela(dados.documento)} onChange={e => mudar('documento', mesmoDocumento(e.target.value) ? inicial.documento : e.target.value)} onBlur={() => conferirDuplicado('documento')} erro={erroDo('documento')} />
  const area = (chave, rotulo) => <div className={css.larguraTotal}><CampoArea rotulo={rotulo} value={dados[chave] ?? ''} onChange={e => mudar(chave, e.target.value)} erro={erroDo(chave)} /></div>
  const selecao = (chave, rotulo, opcoes) => <Combobox rotulo={rotulo} obrigatorio={obrigatorio(chave)} valor={dados[chave]} aoMudar={valor => mudar(chave, valor)} opcoes={opcoes} erro={erros[chave]} disabled={salvando || !podeEditar} />
  const subcategorias = tela === 'produtos' ? subcategoriasDe(categorias, dados.categoriaId) : []
  const dinheiro = (chave, rotulo) => <CampoReais rotulo={rotulo} centavos={dados[chave] == null ? null : Math.round(dados[chave] * 100)} aoMudar={valor => mudar(chave, valor == null ? null : valor / 100)} ler={lerMoeda} erro={erros[chave]} />

  async function conferirDuplicado(chave) {
    if (tela !== 'clientes' || !podeEditar) return
    buscaDuplicado.current?.abort()
    const valor = dados[chave]
    const termo = termoDeDuplicado(chave, valor)
    if (!termo) return
    const controlador = new AbortController(); buscaDuplicado.current = controlador
    try {
      const itens = await buscarClientesParecidos(termo, contexto, controlador.signal)
      if (controlador.signal.aborted || !montado.current) return
      const [cliente] = duplicadosDoCliente(chave, valor, itens, id)
      definirDuplicado(cliente ? { campo: chave, cliente } : null)
    } catch { /* O aviso é ajuda: se a busca falhar, o cadastro segue normal. */ }
  }
  const pedirAbrirOutro = (cliente) => alterado ? confirmarAbrirOutro(cliente) : aoAbrirCadastro(cliente)

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
    const faltas = errosDoFormularioCadastro(tela, dados)
    if (Object.keys(faltas).length) { definirErros(faltas); focarPrimeiroErro(); return }
    const concluir = iniciar(); if (!concluir) return
    falhar(null)
    try {
      const atual = await salvarCadastro(tela, dados, contexto)
      if (montado.current) aoSalvo({ anterior: inicial, atual })
    } catch (e) {
      if (!montado.current) return
      const semConfirmacao = bloqueiaRepeticaoDoCadastro(tela, dados, e)
      const conflitoCliente = e.status === 409 && tela === 'clientes' && id
      // #2457 (B5): erro de validação da API vai para baixo do campo; o aviso geral só diz para conferir.
      const camposComErro = e.campos ?? {}
      if (Object.keys(camposComErro).length) { definirErros(camposComErro); focarPrimeiroErro() }
      falhar(e.codigo === 'IDEMPOTENCY_PAYLOAD_MISMATCH' ? MENSAGEM_CADASTRO_REPETIDO : e.status === 409 ? conflitoCliente ? 'Este cadastro mudou. O que você digitou continua aqui. Confira a versão atual antes de salvar novamente.' : 'Este cadastro mudou. Feche e abra novamente para conferir a versão atual. O que você digitou continua aqui.' : Object.keys(camposComErro).length ? `Confira os campos destacados: ${Object.values(camposComErro).join(' ')}` : e.message)
      if (conflitoCliente) { aoMudar({ ...editor, conflito: true, reconferido: false }); definirVersaoAtual(null) }
      if (semConfirmacao) aoMudar({ ...editor, semConfirmacao: true })
    } finally { concluir() }
  }
  async function concluirAtividade() {
    if (atividadeEmVoo.current || !atividade) return
    atividadeEmVoo.current = true; alternar(true); falharAtividade(null)
    try {
      await alternarAtividadeCadastro(tela, id, atividade.ativar, contexto)
      if (montado.current) aoAlternarAtividade?.(atividade.sucesso)
    } catch (e) { if (montado.current) falharAtividade(mensagemDeAtividade(tela, atividade.ativar, e)) }
    finally { atividadeEmVoo.current = false; if (montado.current) alternar(false) }
  }
  const mostraAtividade = podeEditar && temAtividadeNoEditor(tela, dados)
  const titulo = ehFicha ? 'Ficha do cliente' : `${podeEditar ? id ? 'Editar' : 'Novo' : 'Consultar'} ${({ clientes: 'cliente', produtos: 'produto', fornecedores: 'fornecedor', bancos: 'cadastro bancário' })[tela]}`
  return <Modal titulo={titulo} largura="md" focarCampo={!ehFicha || editarAoAbrir} aoFechar={salvando ? undefined : aoFechar} segurarFechamento={salvando} confirmarDescarte={alterado && !salvando} rodape={<>
    {/* O vermelho fica só na confirmação: aqui o botão é discreto e afastado de Salvar. Com `motivo` sempre
        presente o invólucro não troca e o botão não remonta (#2258). */}
    {mostraAtividade && <span className={css.acaoAtividade}><Botao disabled={salvando || alterado || Boolean(editor.conflito || editor.semConfirmacao)} motivo={`Salve ou descarte as alterações antes de ${emAtividadeCadastro(dados) ? 'inativar' : 'reativar'}.`} onClick={() => { falharAtividade(null); confirmarAtividade(confirmacaoDeAtividade(tela, dados)) }}>{emAtividadeCadastro(dados) ? 'Inativar' : 'Reativar'}</Botao></span>}
    <Botao onClick={e => e.currentTarget.closest('dialog')?.dispatchEvent(new Event('cancel', { cancelable: true }))} disabled={salvando}>{podeEditar && formularioAberto ? 'Cancelar' : 'Fechar'}</Botao>
    {podeEditar && formularioAberto && <Botao tipo="submit" form={idForm} variante="primario" disabled={salvando || editor.semConfirmacao || editor.conflito || !alterado} motivo={editor.conflito ? 'Confira a versão atual antes de salvar novamente.' : editor.semConfirmacao ? 'Confira a lista antes de repetir um cadastro sem confirmação.' : !alterado ? id ? 'Nada mudou para salvar.' : 'Preencha o cadastro para salvar.' : undefined}>{salvando ? 'Salvando…' : 'Salvar cadastro'}</Botao>}
  </>}>
    {erro && <p className={css.erro} role="alert">{erro}</p>}
    {tela === 'clientes' && editor.conflito && <section className={css.assistente} aria-label="Conferir versão do cadastro">
      <p>Outra edição foi salva para esta pessoa. Conferir a versão atual não grava nem descarta o que você digitou.</p>
      <Botao disabled={conferindo} onClick={carregarVersaoAtual}>{conferindo ? 'Carregando versão…' : 'Conferir versão atual'}</Botao>
      {versaoAtual && <>
        <p><strong>Versão atual de {versaoAtual.nome}</strong></p>
        <dl>{Object.entries(CAMPOS_CLIENTE).filter(([campo]) => (versaoAtual[campo] ?? '') !== (inicial[campo] ?? '') || (dados[campo] ?? '') !== (inicial[campo] ?? '')).map(([campo, rotulo]) => <div className={css.sugestao} key={campo}><dt>{rotulo}</dt><dd>Atual: {naTela(campo, versaoAtual[campo])}<br />Sua edição: {naTela(campo, dados[campo])}</dd></div>)}</dl>
        <p>Serão mantidos os campos que você alterou. Os demais acompanharão a versão atual. Revise o formulário antes de salvar.</p>
        <Botao disabled={conferindo} onClick={reconferir}>Manter minhas alterações sobre esta versão</Botao>
      </>}
    </section>}
    {editor.reconferido && !editor.conflito && <p role="status">Versão atual conferida. Revise os campos e use Salvar cadastro para confirmar sua edição.</p>}
    {editor.semConfirmacao && <div className={css.erro} role="alert"><p>A resposta não chegou. O cadastro pode ter sido salvo. Feche e confira a lista antes de criar outro.</p></div>}
    {ehFicha && <ResumoDoCliente key={`resumo:${id}`} dados={dados} relacionamento={relacionamento.dados} carregando={relacionamento.carregando && !relacionamento.dados}
      editando={formularioAberto} aoEditar={podeEditar ? abrirFormulario : undefined}
      aoMudarFoto={podeEditar ? () => { relacionamento.recarregar(); aoGravarRelacionamento?.() } : undefined} />}
    {/* #2849: o que só a tela que abriu sabe (no Atendimento: apontamentos, avisos, endereço da conversa). */}
    {ehFicha && complemento}
    {/* `key`: o atalho por endereço troca a pessoa sem desmontar o editor; modal, aviso e notas abertas não passam adiante. */}
    {comRelacionamento && <RelacionamentoCliente key={id} clienteId={id} relacionamento={relacionamento} contexto={contexto} podeEditar={podeEditar} aoGravar={aoGravarRelacionamento} aoRascunhar={aoRascunharRelacionamento} restricoes={restricoes} />}
    {conversa && podeEditar && formularioAberto && <section className={css.assistente} aria-label="Assistência ao cadastro">
      <div className={css.barra}><strong>Conferir dados da conversa</strong><Botao icone="sparkles" disabled={salvando || sugestao.carregando} onClick={sugestao.consultar}>{sugestao.carregando ? 'Consultando…' : 'Consultar com IA'}</Botao></div>
      <p>Usa até 20 mensagens recentes recebidas com texto nesta conversa para sugerir nome, telefone e e-mail.</p>
      {sugestao.erro && <p className={css.erro} role="alert">{sugestao.erro}</p>}
      {sugestao.campos.map(c => <div className={css.sugestao} key={c.campo}><span><strong>{({ nome: 'Nome', telefone: 'Telefone', email: 'E-mail' })[c.campo]}: {c.valor}</strong><q>{c.evidencia}</q></span><Botao disabled={salvando} onClick={() => sugestao.aplicar(c.campo)}>Usar este dado</Botao></div>)}
    </section>}
    <form id={idForm} ref={formRef} onSubmit={salvar} hidden={!formularioAberto}>
      {podeEditar && <p className={css.legenda}>Campos com * são obrigatórios.</p>}
      <fieldset disabled={salvando || !podeEditar} className={css.formulario}>
        {campo('nome', tela === 'bancos' ? 'Nome da conta' : 'Nome', { required: true, 'data-foco-inicial': true })}
        {tela === 'clientes' && <>
          {selecao('tipoPessoa', 'Tipo de pessoa', pessoa)}
          {telefone()}{campo('email', 'E-mail', { tipo: 'email' })}
          {documento(dados.tipoPessoa === 'juridica' ? 'CNPJ' : 'CPF ou documento')}
          {duplicado && <div className={`${css.larguraTotal} ${css.avisoDuplicado}`} role="status">
            <p>{avisoDeDuplicado(duplicado.campo, duplicado.cliente)}</p>
            {aoAbrirCadastro && <Botao onClick={() => pedirAbrirOutro(duplicado.cliente)}>Abrir cadastro de {duplicado.cliente.nome?.trim() || 'cliente sem nome'}</Botao>}
          </div>}
          {dados.tipoPessoa === 'juridica' && <>{campo('nomeFantasia', 'Nome fantasia')}{campo('inscricaoEstadual', 'Inscrição estadual')}</>}
          {campo('endereco', 'Endereço')}{campo('apt', 'Complemento')}
          {area('observacoes', 'Observações')}
        </>}
        {tela === 'fornecedores' && <>
          {campo('contato', 'Pessoa de contato')}{telefone()}{campo('email', 'E-mail', { tipo: 'email' })}
          {documento('CNPJ ou CPF')}{campo('categoria', 'Categoria')}
          {campo('tipo', 'Tipo de fornecimento')}{campo('siteUrl', 'Site', { tipo: 'url' })}
          <CampoTexto rotulo="Prazo de entrega (dias)" tipo="number" min="0" value={dados.leadTimeEstimadoDias ?? ''} onChange={e => mudar('leadTimeEstimadoDias', e.target.value === '' ? null : Number(e.target.value))} />
          {campo('pedidoMinimo', 'Pedido mínimo')}{campo('fretePadrao', 'Condição de frete')}
          {area('observacoes', 'Observações')}
        </>}
        {tela === 'produtos' && <>
          {selecao('categoriaId', 'Categoria', categoriasRaiz(categorias))}
          {subcategorias.length > 0 && <Combobox rotulo="Subcategoria" valor={dados.subcategoriaId ?? ''} aoMudar={valor => mudar('subcategoriaId', valor)} opcoes={subcategorias} disabled={salvando || !podeEditar} />}
          {erroCategorias && <div className={css.erro}><p>{erroCategorias}</p><Botao onClick={() => recarregarCategorias(n => n + 1)}>Carregar categorias</Botao></div>}
          {selecao('tipo', 'Tipo de produto', tiposProduto)}{campo('marca', 'Marca')}
          {campo('skuBase', 'Código interno (SKU)')}{campo('codigoBarras', 'Código de barras')}
          {dinheiro('precoReferencia', 'Preço de venda (R$)')}{dinheiro('custoReferencia', 'Custo de referência (R$)')}
          {id ? selecao('status', 'Situação', opcoesStatus) : <p className={css.legenda}>O produto entra ativo quando tem preço de venda. Sem preço, entra inativo.</p>}
          <label className={css.check}><input type="checkbox" checked={dados.controlaValidade === true} onChange={e => mudar('controlaValidade', e.target.checked)} />Controlar validade</label>
          {area('descricaoBase', 'Descrição')}
          {area('observacaoInterna', 'Observação interna')}
        </>}
        {tela === 'bancos' && <>
          {selecao('tipo', 'Tipo de conta', tiposConta)}{campo('instituicao', 'Instituição financeira')}{campo('agencia', 'Agência')}{campo('conta', 'Número da conta')}{campo('titular', 'Titular')}
          <label className={css.check}><input type="checkbox" checked={dados.ativo === true} onChange={e => mudar('ativo', e.target.checked)} />Conta ativa</label>
        </>}
      </fieldset>
    </form>
    {comRelacionamento && <FichaCliente key={`${id}:${dados.revisao ?? ''}`} cliente={dados} detalhe={dados.detalhe} contexto={contexto} podeEditar={podeEditar} aoGravar={aoGravarRelacionamento}
      relacionamento={relacionamento.dados} carregandoRelacionamento={relacionamento.carregando && !relacionamento.dados} conversasNoAtendimento={conversasNoAtendimento} aoAbrirConversa={alterado || salvando ? undefined : aoAbrirConversa} />}
    {atividade && <Modal titulo={atividade.titulo} aoFechar={alternando ? undefined : () => confirmarAtividade(null)} segurarFechamento={alternando}
      rodape={<><Botao disabled={alternando} onClick={() => confirmarAtividade(null)}>Cancelar</Botao><Botao variante={atividade.ativar ? 'primario' : 'perigo'} disabled={alternando} onClick={concluirAtividade}>{alternando ? atividade.ativar ? 'Reativando…' : 'Inativando…' : atividade.botao}</Botao></>}>
      <p>{atividade.texto}</p>
      {erroAtividade && <p className={css.erro} role="alert">{erroAtividade}</p>}
    </Modal>}
    {abrirOutro && <Modal titulo={`Abrir o cadastro de ${abrirOutro.nome?.trim() || 'cliente sem nome'}?`} aoFechar={() => confirmarAbrirOutro(null)}
      rodape={<><Botao onClick={() => confirmarAbrirOutro(null)}>Continuar aqui</Botao><Botao variante="primario" onClick={() => aoAbrirCadastro(abrirOutro)}>Abrir cadastro</Botao></>}>
      <p>O que você digitou aqui não será salvo.</p>
    </Modal>}
    {tela === 'clientes' && dados.detalhe?.alteracoes?.length > 0 && <details className={css.historico}><summary>Histórico do cadastro</summary><ul>{dados.detalhe.alteracoes.slice(0, 10).map(a => <li key={a.id}><strong>{a.campo}</strong> · {a.alteradoPorNome || 'Usuário da loja'}<small>{a.valorAntigo || 'Vazio'} → {a.valorNovo || 'Vazio'}</small></li>)}</ul></details>}
  </Modal>
}
