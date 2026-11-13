import { useId, useRef, useState } from 'react'
import { aprovarModeloCadastro, definirEtiquetaPadrao, duplicarEtiquetaCadastro, editarEtiquetaCadastro, obterCadastro, previaModeloCadastro, salvarCadastro, useContextoVisualCadastro, useListaCadastros } from '../../../aplicacao/cadastros'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { useRascunhoDoAjuste } from '../../../aplicacao/rascunhosDosAjustes'
import { Botao } from '../../../componentes/Botao'
import { CampoArea, CampoTexto } from '../../../componentes/Campo'
import { Combobox } from '../../../componentes/Combobox'
import { Modal } from '../../../componentes/Modal'
import { Pilula } from '../../../componentes/Pilula'
import { Vazio } from '../../../componentes/Vazio'
import { EVENTOS_MODELO, MENSAGEM_CADASTRO_REPETIDO, NOMES_CANAIS_MODELO, novoModeloCadastro } from '../../../dominio/cadastros'
import css from './cadastros.module.css'

const CANAIS = [{ valor: '', rotulo: 'Todos os canais' }, { valor: 'Email', rotulo: 'E-mail' }, { valor: 'Sms', rotulo: 'SMS' }, { valor: 'WhatsApp', rotulo: 'WhatsApp' }]
export function ModelosCadastro({ contexto, respostas, impressao }) {
  const { acoes } = useAcessoModulos()
  const [memoria, guardar] = useContextoVisualCadastro('cadastros:modelos', contexto.identidade, { aba: 'respostas' })
  const aba = memoria?.aba ?? 'respostas'
  const abas = [{ id: 'respostas', nome: 'Mensagens' }, ...(acoes.verModelosNotificacao ? [{ id: 'notificacoes', nome: 'E-mail e SMS' }] : []), ...(acoes.verCadastrosEtiquetas ? [{ id: 'etiquetas', nome: 'Etiquetas' }] : []), { id: 'impressao', nome: 'Impressão' }]
  const atual = abas.find(a => a.id === aba)?.id ?? abas[0].id
  return <section className={css.painel} aria-label="Modelos">
    <header className={css.cabecalho}><div><h2>Modelos</h2><p>Textos, etiquetas e impressos da loja.</p></div></header>
    <nav className={css.abas} aria-label="Tipos de modelo">{abas.map(a => <Botao key={a.id} aria-pressed={atual === a.id} onClick={() => guardar({ ...memoria, aba: a.id })}>{a.nome}</Botao>)}</nav>
    {atual === 'respostas' && respostas}
    {atual === 'impressao' && impressao}
    {atual === 'notificacoes' && <ListaModelos contexto={contexto} />}
    {atual === 'etiquetas' && <EtiquetasCadastro contexto={contexto} />}
  </section>
}

function ListaModelos({ contexto }) {
  const [editor, guardarEditor] = useRascunhoDoAjuste('cadastros:modelos:notificacoes')
  const [visuais, guardar] = useContextoVisualCadastro('cadastros:modelos:notificacoes', contexto.identidade, { filtro: '', pagina: 1 })
  const memoria = { ...visuais, editor }
  const consulta = useListaCadastros('modelos', memoria, contexto)
  const [erro, falhar] = useState(null)
  const [ocupado, ocupar] = useState(false)
  const [sucesso, avisar] = useState(null)
  const emVoo = useRef(false)
  const mudar = (campos) => {
    const { editor: proximoEditor, ...valores } = campos
    if (Object.hasOwn(campos, 'editor')) guardarEditor(proximoEditor)
    if (Object.keys(valores).length) guardar({ ...visuais, ...valores })
  }
  async function abrir(item) {
    if (emVoo.current) return
    emVoo.current = true; ocupar(true); falhar(null)
    try { const dados = await obterCadastro('modelos', item.id, contexto); mudar({ editor: { dados, inicial: dados } }) }
    catch (e) { falhar(e.message) } finally { emVoo.current = false; ocupar(false) }
  }
  async function aprovar(item) {
    if (emVoo.current) return
    emVoo.current = true; ocupar(true); falhar(null)
    try { await aprovarModeloCadastro(item.id, contexto); avisar('Modelo aprovado na loja.'); consulta.recarregar() }
    catch (e) { falhar(e.message) } finally { emVoo.current = false; ocupar(false) }
  }
  const novo = () => { const dados = novoModeloCadastro(); mudar({ editor: { dados, inicial: dados } }) }
  return <div className={css.painel}>
    <div className={css.barra}><Combobox rotulo="Canal" valor={memoria.filtro} opcoes={CANAIS} aoMudar={filtro => mudar({ filtro, pagina: 1 })} /><Botao variante="primario" icone="mais" onClick={novo}>Novo modelo</Botao></div>
    {sucesso && <p role="status" className={css.sucesso}>{sucesso}</p>}
    {(erro || consulta.erro) && <div role="alert" className={css.erro}>{erro || consulta.erro}<Botao onClick={() => { falhar(null); consulta.recarregar() }}>Tentar de novo</Botao></div>}
    {consulta.carregando ? <Vazio titulo="Carregando modelos" /> : consulta.itens.length === 0 ? <Vazio titulo="Nenhum modelo neste canal">Crie o primeiro modelo da loja.</Vazio> : <ul className={css.cartoes}>{consulta.itens.map(m => <li key={m.id}>
      <div><small>{NOMES_CANAIS_MODELO[m.canal] ?? m.canal} · versão {m.versao}</small><h3>{m.nome}</h3><p className={css.amostra}>{m.assuntoTemplate || m.corpoTemplate}</p><Pilula tom={m.aprovado ? 'ok' : 'aviso'}>{m.aprovado ? 'Aprovado na loja' : 'Aguardando revisão'}</Pilula></div>
      <div className={css.acoes}><Botao disabled={ocupado} onClick={() => abrir(m)}>Editar</Botao>{!m.aprovado && <Botao disabled={ocupado} onClick={() => aprovar(m)}>Aprovar na loja</Botao>}</div>
    </li>)}</ul>}
    <div className={css.rodape}><span>{consulta.total} {consulta.total === 1 ? 'modelo' : 'modelos'}</span><div className={css.acoes}><Botao disabled={memoria.pagina <= 1 || consulta.carregando} onClick={() => mudar({ pagina: memoria.pagina - 1 })}>Anterior</Botao><span>Página {memoria.pagina}</span><Botao disabled={memoria.pagina * 20 >= consulta.total || consulta.carregando} onClick={() => mudar({ pagina: memoria.pagina + 1 })}>Próxima</Botao></div></div>
    {memoria.editor && <EditorModelo contexto={contexto} editor={memoria.editor} aoMudar={editor => mudar({ editor })} aoFechar={() => mudar({ editor: null })} aoSalvo={() => { mudar({ editor: null }); avisar('Modelo salvo para revisão.'); consulta.recarregar() }} />}
  </div>
}

function EditorModelo({ contexto, editor, aoMudar, aoFechar, aoSalvo }) {
  const id = useId(), { dados, inicial } = editor
  const [ocupado, ocupar] = useState(false)
  const [erro, falhar] = useState(null)
  const [previa, mostrarPrevia] = useState(null)
  const emVoo = useRef(false)
  const mudar = (campo, valor) => { aoMudar({ ...editor, dados: { ...dados, [campo]: valor } }); mostrarPrevia(null) }
  async function executar(salvar) {
    if (emVoo.current) return
    emVoo.current = true; ocupar(true); falhar(null)
    try {
      if (salvar) { await salvarCadastro('modelos', dados, contexto); aoSalvo() }
      else mostrarPrevia(await previaModeloCadastro(dados, contexto))
    } catch (e) { falhar(e.codigo === 'IDEMPOTENCY_PAYLOAD_MISMATCH' ? MENSAGEM_CADASTRO_REPETIDO : e.message) } finally { emVoo.current = false; ocupar(false) }
  }
  return <Modal titulo={dados.id ? 'Editar modelo' : 'Novo modelo'} largura="lg" focarCampo aoFechar={ocupado ? undefined : aoFechar} segurarFechamento={ocupado} confirmarDescarte={JSON.stringify(dados) !== JSON.stringify(inicial)}
    rodape={<><Botao disabled={ocupado} onClick={e => e.currentTarget.closest('dialog')?.dispatchEvent(new Event('cancel', { cancelable: true }))}>Cancelar</Botao><Botao disabled={ocupado || !dados.corpoTemplate?.trim()} onClick={() => executar(false)}>Ver prévia</Botao><Botao variante="primario" tipo="submit" form={id} disabled={ocupado}>{ocupado ? 'Aguarde…' : 'Salvar modelo'}</Botao></>}>
    {erro && <p className={css.erro} role="alert">{erro}</p>}
    <form id={id} onSubmit={e => { e.preventDefault(); if (!(dados.nome?.length > 150)) executar(true) }}><fieldset disabled={ocupado} className={css.formulario}>
      <CampoTexto rotulo="Nome do modelo" required erro={dados.nome?.length > 150 ? `Use até 150 caracteres. Agora tem ${dados.nome.length}.` : undefined} value={dados.nome} disabled={Boolean(dados.id)} onChange={e => mudar('nome', e.target.value)} />
      {!dados.id && <><CampoTexto rotulo="Identificação do modelo" dica="Um nome curto, sem espaços, para identificar este modelo." required pattern="[a-zA-Z0-9_-]+" value={dados.codigo} onChange={e => mudar('codigo', e.target.value)} /><Combobox rotulo="Canal" valor={dados.canal} aoMudar={v => mudar('canal', v)} opcoes={CANAIS.slice(1)} /><Combobox rotulo="Quando usar" valor={dados.tipoEvento} aoMudar={v => mudar('tipoEvento', v)} opcoes={EVENTOS_MODELO} /></>}
      <div className={css.larguraTotal}><CampoTexto rotulo="Assunto" value={dados.assuntoTemplate ?? ''} onChange={e => mudar('assuntoTemplate', e.target.value)} /></div>
      <div className={css.larguraTotal}><CampoArea rotulo="Mensagem" required rows={9} value={dados.corpoTemplate ?? ''} onChange={e => mudar('corpoTemplate', e.target.value)} /></div>
    </fieldset></form>
    {previa && <section className={css.previa}><h3>Prévia sem envio</h3><p>{previa.assuntoRenderizado}</p>{dados.canal === 'Email' || dados.canal === 1 ? <iframe title="Prévia do modelo" sandbox="" srcDoc={`<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'"><style>body{font-family:system-ui;overflow-wrap:anywhere;padding:12px}</style>${previa.corpoRenderizado}`} /> : <p className={css.previaTexto}>{previa.corpoRenderizado}</p>}<p className={css.apoio}>Dados do cliente e do pedido serão preenchidos no uso do modelo.</p></section>}
  </Modal>
}

function EtiquetasCadastro({ contexto }) {
  const consulta = useListaCadastros('etiquetas', {}, contexto)
  const [copiar, escolher] = useState(null)
  const [nome, nomear] = useState('')
  const [desenho, desenhar] = useState(null)
  const [edicao, editar] = useState(false)
  const [erro, falhar] = useState(null)
  const [sucesso, avisar] = useState(null)
  const [ocupado, ocupar] = useState(false)
  const emVoo = useRef(false)
  async function executar(item, copia = false) {
    if (emVoo.current) return
    emVoo.current = true; ocupar(true); falhar(null)
    try { if (copia) await (edicao ? editarEtiquetaCadastro : duplicarEtiquetaCadastro)({ ...item, layoutJson: JSON.stringify(desenho) }, nome, contexto); else await definirEtiquetaPadrao(item, contexto); escolher(null); avisar(copia ? 'Modelo salvo.' : 'Modelo padrão atualizado.'); consulta.recarregar() }
    catch (e) { falhar(e.message) } finally { emVoo.current = false; ocupar(false) }
  }
  function abrir(item, existente) {
    try { const layout = JSON.parse(item.layoutJson); if (!layout.size || !Array.isArray(layout.elements)) throw new Error('O desenho desta etiqueta não pôde ser aberto.'); desenhar(layout); escolher(item); editar(existente); nomear(existente ? item.nome : `${item.nome} · loja`); falhar(null) }
    catch { falhar('O desenho desta etiqueta não pôde ser aberto. Atualize a lista e tente novamente.') }
  }
  return <div className={css.painel}>
    {sucesso && <p className={css.sucesso} role="status">{sucesso}</p>}
    {(erro || consulta.erro) && <p className={css.erro} role="alert">{erro || consulta.erro}</p>}
    <div className={css.barra}><p>Modelos de etiqueta usados pela loja.</p><Botao onClick={consulta.recarregar} disabled={consulta.carregando}>Atualizar</Botao></div>
    {consulta.carregando ? <Vazio titulo="Carregando etiquetas" /> : consulta.itens.length === 0 ? <Vazio titulo="Nenhum modelo de etiqueta disponível" /> : <ul className={css.cartoes}>{consulta.itens.map(m => <li key={`${m.origem}:${m.id}`}><div><small>{m.origem === 'Sistema' ? 'Modelo de origem' : 'Modelo da loja'}</small><h3>{m.nome}</h3><p>{m.descricao}</p>{m.isDefault && <Pilula tom="ok">Padrão</Pilula>}</div><div className={css.acoes}>{m.origem === 'Empresa' && <Botao disabled={ocupado} onClick={() => abrir(m, true)}>Editar textos</Botao>}<Botao disabled={ocupado} onClick={() => abrir(m, false)}>Criar a partir deste</Botao><Botao disabled={ocupado || m.isDefault} onClick={() => executar(m)}>Usar como padrão</Botao></div></li>)}</ul>}
    {copiar && <Modal titulo={edicao ? 'Editar modelo de etiqueta' : 'Criar modelo de etiqueta'} largura="md" aoFechar={ocupado ? undefined : () => escolher(null)} segurarFechamento={ocupado} confirmarDescarte={nome !== copiar.nome || JSON.stringify(desenho) !== copiar.layoutJson}
      rodape={<><Botao disabled={ocupado} onClick={e => e.currentTarget.closest('dialog')?.dispatchEvent(new Event('cancel', { cancelable: true }))}>Cancelar</Botao><Botao variante="primario" disabled={ocupado || !nome.trim()} onClick={() => executar(copiar, true)}>{ocupado ? 'Salvando…' : 'Salvar modelo'}</Botao></>}>
      <div className={css.painel}><CampoTexto rotulo="Nome" disabled={ocupado} value={nome} onChange={e => nomear(e.target.value)} /><p>Formato: {desenho.size.w_mm} × {desenho.size.h_mm} mm. Posições e códigos serão preservados.</p>
      {desenho.elements.filter(e => e.type === 'text').map((elemento, indice) => <CampoArea key={elemento.id} rotulo={`Texto ${indice + 1}`} dica="Os campos entre chaves serão preenchidos na impressão." disabled={ocupado} erro={elemento.content?.length > 500 ? `Use até 500 caracteres. Agora tem ${elemento.content.length}.` : undefined} value={elemento.content ?? ''} onChange={e => desenhar({ ...desenho, elements: desenho.elements.map(atual => atual.id === elemento.id ? { ...atual, content: e.target.value } : atual) })} />)}
      {erro && <p className={css.erro} role="alert">{erro}</p>}</div>
    </Modal>}
  </div>
}
