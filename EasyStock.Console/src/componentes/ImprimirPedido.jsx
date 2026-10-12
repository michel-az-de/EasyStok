import { useEffect, useRef, useState } from 'react'
import { Botao } from './Botao'
import { CampoSelecao } from './Campo'
import { Modal } from './Modal'
import { imprimirHtml } from './htmlNoNavegador'
import { PreviaImpresso } from './PreviaImpresso'
import css from './ImprimirPedido.module.css'

const naoPropagar = (e) => e.stopPropagation()
const CODIGOS = [{ valor: 'qr', rotulo: 'QR Code' }, { valor: 'barras', rotulo: 'Código de barras · Code128' }]
const PERFIS = [{ valor: 'sistema', rotulo: 'Escolher no diálogo do sistema' }, { valor: 'hp-a4', rotulo: 'HP · folha A4' }]
const nomePerfil = (valor) => PERFIS.find((p) => p.valor === valor).rotulo
const data = (valor) => new Date(valor).toLocaleString('pt-BR')
const estadoRegistro = (valor) => ({ preparada: 'Prévia guardada', impressa: 'Saída confirmada', falhou: 'Falha informada' })[valor] ?? valor

export function ImprimirPedido({
  documentos, papeis, papelPadrao, preparar, historico, confirmar, documentoInicial, aoImprimir, abrirImpressao = imprimirHtml,
  rotulo = 'Imprimir', variante = 'secundario', titulo = 'Imprimir pedido', ...resto
}) {
  const [aberto, setAberto] = useState(false)
  const [documento, setDocumento] = useState(documentoInicial ?? documentos[0].valor)
  const [papel, setPapel] = useState(null)
  const [codigo, setCodigo] = useState('qr')
  const [perfil, setPerfil] = useState('sistema')
  const [registro, setRegistro] = useState(null)
  const [situacao, setSituacao] = useState({ estado: 'ocioso' })
  const [lista, setLista] = useState([])
  const [pagina, setPagina] = useState(1)
  const [revisao, setRevisao] = useState(0)
  const [erroHistorico, setErroHistorico] = useState(null)
  const [carregandoHistorico, setCarregandoHistorico] = useState(false)
  const operacao = useRef(null)
  const solicitacao = useRef(null)
  const historicoRef = useRef(historico)
  historicoRef.current = historico

  useEffect(() => () => operacao.current?.abort(), [])
  useEffect(() => {
    if (!aberto) return
    const controle = new AbortController()
    setErroHistorico(null)
    setCarregandoHistorico(true)
    setLista([])
    historicoRef.current(pagina, controle.signal).then((itens) => {
      if (!controle.signal.aborted) setLista(itens)
    }).catch((erro) => { if (!controle.signal.aborted) setErroHistorico(erro.message) })
      .finally(() => { if (!controle.signal.aborted) setCarregandoHistorico(false) })
    return () => controle.abort()
  }, [aberto, pagina, revisao])

  const fechar = () => { operacao.current?.abort(); operacao.current = null; setAberto(false) }
  const limpar = () => { solicitacao.current = null; setRegistro(null); setSituacao({ estado: 'ocioso' }) }
  const abrir = () => { limpar(); setPapel(perfil === 'hp-a4' ? 'a4' : papelPadrao(documento)); setPagina(1); setAberto(true) }

  async function carregar(original = null) {
    if (operacao.current) return
    const controle = new AbortController()
    operacao.current = controle
    setSituacao({ estado: 'preparando' })
    const opcoes = original
      ? { documento: original.documento, modelo: original.modelo, codigo: original.codigo, perfil: original.perfil, originalId: original.id }
      : { documento, modelo: papel, codigo, perfil: nomePerfil(perfil) }
    const chave = JSON.stringify(opcoes)
    if (solicitacao.current?.chave !== chave) solicitacao.current = { chave, id: crypto.randomUUID() }
    try {
      const pronto = await preparar({ ...opcoes, solicitacaoId: solicitacao.current.id }, controle.signal)
      controle.signal.throwIfAborted()
      setRegistro(pronto)
      setSituacao({ estado: 'pronto' })
      setRevisao((r) => r + 1)
    } catch (erro) {
      if (!controle.signal.aborted) setSituacao({ estado: 'erro', mensagem: erro.message })
    } finally { if (operacao.current === controle) operacao.current = null }
  }

  async function imprimir() {
    if (operacao.current || !registro) return
    const controle = new AbortController()
    operacao.current = controle
    setSituacao({ estado: 'imprimindo' })
    try {
      const destino = await abrirImpressao(registro.html, { sinal: controle.signal })
      controle.signal.throwIfAborted()
      setSituacao({ estado: 'aguardando', destino })
    } catch (erro) {
      if (!controle.signal.aborted) setSituacao({ estado: 'erro', mensagem: erro.message })
    } finally { if (operacao.current === controle) operacao.current = null }
  }

  async function confirmarSaida() {
    if (operacao.current) return
    const controle = new AbortController()
    operacao.current = controle
    setSituacao({ estado: 'confirmando' })
    try {
      await confirmar(registro)
      controle.signal.throwIfAborted()
      if (!registro.originalId) await aoImprimir?.(registro.documento)
      controle.signal.throwIfAborted()
      setRegistro({ ...registro, status: 'impressa' })
      setSituacao({ estado: 'confirmada' })
      setRevisao((r) => r + 1)
    } catch (erro) {
      if (!controle.signal.aborted) setSituacao({ estado: 'aguardando', mensagem: erro.message })
    } finally { if (operacao.current === controle) operacao.current = null }
  }

  const ocupado = ['preparando', 'imprimindo', 'confirmando'].includes(situacao.estado)
  const confirmarPapel = ['aguardando', 'confirmando'].includes(situacao.estado)
  const congelado = ocupado || Boolean(registro)
  return (
    <span style={{ display: 'contents' }} onPointerDown={naoPropagar} onMouseDown={naoPropagar} onTouchStart={naoPropagar}>
      <Botao variante={variante} icone="printer" onClick={abrir} {...resto}>{rotulo}</Botao>
      {aberto && <Modal titulo={titulo} aoFechar={fechar} largura="min(1060px, calc(100vw - 24px))" rodape={(
        <>
          <Botao variante="texto" onClick={fechar}>Fechar</Botao>
          {registro && !confirmarPapel && <Botao variante="secundario" onClick={limpar} disabled={ocupado}>Nova prévia</Botao>}
          {confirmarPapel ? <>
            <Botao variante="secundario" disabled={ocupado} onClick={() => setSituacao({ estado: 'pronto' })}>Não saiu / cancelei</Botao>
            <Botao variante="primario" disabled={ocupado} onClick={confirmarSaida}>{ocupado ? 'Confirmando…' : 'Sim, o papel saiu'}</Botao>
          </> : situacao.estado !== 'confirmada' && <Botao variante="primario" icone={registro ? 'printer' : 'eye'} disabled={ocupado} onClick={() => registro ? imprimir() : carregar()}>
            {ocupado ? 'Preparando…' : registro ? 'Abrir impressão' : 'Preparar prévia'}
          </Botao>}
        </>
      )}>
        <div className={css.layout}>
          <div className={css.opcoes}>
            <p className={css.etapa}>1. Documento e destino</p>
            <CampoSelecao rotulo="Documento" opcoes={documentos} value={registro?.documento ?? documento} disabled={congelado} onChange={(e) => { limpar(); setDocumento(e.target.value); setPapel(perfil === 'hp-a4' ? 'a4' : papelPadrao(e.target.value)) }} />
            <CampoSelecao rotulo="Perfil de impressão" opcoes={registro ? [{ valor: registro.perfil, rotulo: registro.perfil }] : PERFIS} value={registro?.perfil ?? perfil} disabled={congelado} onChange={(e) => { limpar(); setPerfil(e.target.value); if (e.target.value === 'hp-a4') setPapel('a4') }} />
            <CampoSelecao rotulo="Papel" opcoes={registro || perfil !== 'hp-a4' ? papeis : papeis.filter((p) => p.valor === 'a4')} value={registro?.modelo ?? papel ?? ''} disabled={congelado} onChange={(e) => { limpar(); setPapel(e.target.value) }} />
            <CampoSelecao rotulo="Código" opcoes={CODIGOS} value={registro?.codigo ?? codigo} disabled={congelado} onChange={(e) => { limpar(); setCodigo(e.target.value) }} />
            <p className={css.dica}>Escolha a impressora física no diálogo do sistema. Para a HP: papel A4, escala 100% e cabeçalhos e rodapés desligados.</p>
            <p className={css.dica}>Uma via por abertura. Defina as cópias no diálogo. Os papéis térmicos aguardam teste nos seus equipamentos.</p>
          </div>
          <section className={css.previa} aria-label="Prévia do documento">
            <p className={css.etapa}>2. Confira o papel</p>
            {registro ? <>
              <p className={css.resumo}>{registro.documento} · {registro.modelo} · {registro.codigo} · {registro.perfil}{registro.originalId ? ' · reimpressão do original' : ''}</p>
              <PreviaImpresso html={registro.html} modelo={registro.modelo} />
              <p className={css.dica}>Versão {registro.versao} · conteúdo, fontes e logo guardados. A paginação final aparece no diálogo de impressão.</p>
            </> : <p className={css.vazia}>Prepare a prévia para conferir o documento antes de imprimir.</p>}
          </section>
        </div>
        <div aria-live="polite">
          {confirmarPapel && <p className={css.aviso}>{situacao.destino === 'aba' ? 'A página abriu em outra aba. Imprima por ela e volte para confirmar.' : 'Confira a impressora.'} O papel saiu corretamente?{situacao.mensagem && ` ${situacao.mensagem}`}</p>}
          {situacao.estado === 'confirmada' && <p className={css.aviso}>Saída confirmada e registrada no histórico.</p>}
          {situacao.estado === 'erro' && <p className={css.erro} role="alert">{situacao.mensagem}</p>}
        </div>
        <section className={css.historico} aria-label="Histórico de impressão deste pedido" aria-busy={carregandoHistorico}>
          <h3>Histórico deste pedido</h3>
          <p className={css.dica}>Reimprimir guarda uma nova tentativa e repete o conteúdo original. Uma prévia guardada não comprova a saída do papel.</p>
          {erroHistorico && <p className={css.erro} role="alert">{erroHistorico}</p>}
          {carregandoHistorico && <p className={css.dica} role="status">Carregando histórico…</p>}
          {!carregandoHistorico && !lista.length && !erroHistorico && <p className={css.dica}>Nenhum documento nesta página do histórico.</p>}
          <ul className={css.registros}>{lista.map((i) => <li key={i.id}>
            <div><strong>{documentos.find((d) => d.valor === i.documento)?.rotulo ?? i.documento} · {i.modelo}</strong><span>{data(i.criadaEm)} · {estadoRegistro(i.status)}{i.originalId ? ' · reimpressão' : ''}</span><span>{i.perfil} · {i.codigo} · {i.versao}</span></div>
            <Botao variante="secundario" disabled={ocupado || confirmarPapel} onClick={() => { limpar(); carregar(i) }}>Reimprimir original</Botao>
          </li>)}</ul>
          <div className={css.paginas}><Botao variante="texto" disabled={pagina === 1 || ocupado} onClick={() => setPagina((p) => p - 1)}>Anterior</Botao><span>Página {pagina}</span><Botao variante="texto" disabled={lista.length < 20 || ocupado} onClick={() => setPagina((p) => p + 1)}>Próxima</Botao></div>
        </section>
      </Modal>}
    </span>
  )
}
