import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Botao } from './Botao'
import { CampoSelecao } from './Campo'
import { Modal } from './Modal'
import { imprimirHtml } from './htmlNoNavegador'
import { PreviaImpresso } from './PreviaImpresso'
import css from './ImprimirPedido.module.css'

const naoPropagar = (e) => e.stopPropagation()
const CODIGOS = [{ valor: 'qr', rotulo: 'QR Code' }, { valor: 'barras', rotulo: 'Código de barras · Code128' }]
// Sem catálogo de perfis (#1756), só o diálogo do sistema, que aceita todo papel.
const perfilLivre = (papeis) => [{ valor: 'sistema', rotulo: 'Escolher no diálogo do sistema', papeis: papeis.map((p) => p.valor) }]
const data = (valor) => new Date(valor).toLocaleString('pt-BR')
const estadoRegistro = (valor) => ({ preparada: 'Prévia guardada', impressa: 'Saída confirmada', falhou: 'Falha informada' })[valor] ?? valor
const rotuloDe = (opcoes, valor) => opcoes.find((o) => o.valor === valor)?.rotulo ?? valor

export function ImprimirPedido({
  documentos, papeis, papelPadrao, perfis = perfilLivre(papeis), perfilPadrao = () => perfis[0].valor, orientacao,
  preparar, historico, confirmar, documentoInicial, aoImprimir, abrirImpressao = imprimirHtml,
  rotulo = 'Imprimir', variante = 'secundario', titulo = 'Imprimir pedido', contexto, clienteNome, ...resto
}) {
  const [aberto, setAberto] = useState(false)
  const [documento, setDocumento] = useState(documentoInicial ?? documentos[0].valor)
  const [papel, setPapel] = useState(null)
  const [codigo, setCodigo] = useState('qr')
  const [perfil, setPerfil] = useState('sistema')
  const [registro, setRegistro] = useState(null)
  const [opcoesTentativa, setOpcoesTentativa] = useState(null)
  const [situacao, setSituacao] = useState({ estado: 'ocioso' })
  const [lista, setLista] = useState([])
  const [pagina, setPagina] = useState(1)
  const [revisao, setRevisao] = useState(0)
  const [erroHistorico, setErroHistorico] = useState(null)
  const [carregandoHistorico, setCarregandoHistorico] = useState(false)
  const operacao = useRef(null)
  const solicitacao = useRef(null)
  const tentativa = useRef(null)
  const focoPrevia = useRef(null)
  const focoEstado = useRef(null)
  const historicoRef = useRef(historico)
  useLayoutEffect(() => { historicoRef.current = historico }, [historico])

  useEffect(() => () => operacao.current?.abort(), [])
  useEffect(() => {
    if (!aberto) return
    if (situacao.estado === 'pronto') {
      focoPrevia.current?.focus({ preventScroll: true })
      focoPrevia.current?.scrollIntoView({ block: 'nearest' })
    } else if (['aguardando', 'confirmando', 'confirmada', 'erro'].includes(situacao.estado)) {
      focoEstado.current?.focus({ preventScroll: true })
    }
  }, [aberto, situacao.estado, registro?.id])
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
  const limpar = () => { solicitacao.current = null; tentativa.current = null; setOpcoesTentativa(null); setRegistro(null); setSituacao({ estado: 'ocioso' }) }
  const perfilDe = (valor) => perfis.find((p) => p.valor === valor) ?? perfis[0]
  // Documento traz o perfil e o papel guardados no aparelho; perfil dedicado puxa o papel dele.
  const escolherDocumento = (valor) => { limpar(); setDocumento(valor); setPerfil(perfilDe(perfilPadrao(valor)).valor); setPapel(papelPadrao(valor)) }
  const escolherPerfil = (valor) => {
    limpar(); setPerfil(valor)
    const compativeis = perfilDe(valor).papeis
    if (!compativeis.includes(papel)) setPapel(compativeis[0])
  }
  const abrir = () => { escolherDocumento(documento); setPagina(1); setAberto(true) }

  async function carregar(original = null) {
    if (operacao.current) return
    const controle = new AbortController()
    operacao.current = controle
    setSituacao({ estado: 'preparando' })
    const opcoes = original
      ? { documento: original.documento, modelo: original.modelo, codigo: original.codigo, perfil: original.perfil, originalId: original.id }
      : tentativa.current ?? { documento, modelo: papel, codigo, perfil: perfilDe(perfil).rotulo }
    tentativa.current = opcoes
    setOpcoesTentativa(opcoes)
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
    let registrada = false
    try {
      await confirmar(registro)
      controle.signal.throwIfAborted()
      registrada = true
      setRegistro({ ...registro, status: 'impressa' })
      setRevisao((r) => r + 1)
      if (!registro.originalId) await aoImprimir?.(registro.documento)
      controle.signal.throwIfAborted()
      setSituacao({ estado: 'confirmada' })
    } catch (erro) {
      if (!controle.signal.aborted) setSituacao(registrada
        ? { estado: 'confirmada', mensagem: `Não foi possível atualizar a lista de pedidos. ${erro.message}` }
        : { estado: 'aguardando', mensagem: erro.message, erroConfirmacao: true })
    } finally { if (operacao.current === controle) operacao.current = null }
  }

  const ocupado = ['preparando', 'imprimindo', 'confirmando'].includes(situacao.estado)
  const confirmarPapel = ['aguardando', 'confirmando'].includes(situacao.estado)
  const congelado = ocupado || Boolean(registro)
  const resumoAtivo = registro ?? (opcoesTentativa?.originalId ? opcoesTentativa : null)
  return (
    <span style={{ display: 'contents' }} onPointerDown={naoPropagar} onMouseDown={naoPropagar} onTouchStart={naoPropagar}>
      <Botao variante={variante} icone="printer" onClick={abrir} {...resto}>{rotulo}</Botao>
      {aberto && <Modal titulo={contexto ? `${titulo} ${contexto}` : titulo} descricao={clienteNome ? `Cliente: ${clienteNome}` : undefined} aoFechar={fechar} largura="min(1060px, calc(100vw - 24px))" rodape={(
        <div className={css.rodapeImpressao}>
          <div aria-live="polite">
            {confirmarPapel && <p ref={focoEstado} tabIndex={-1} className={situacao.erroConfirmacao ? css.erro : css.aviso}>
              {situacao.estado === 'confirmando' ? 'Registrando a confirmação…' : situacao.erroConfirmacao
                ? `O papel saiu, mas não conseguimos registrar a confirmação. Tente registrar novamente. ${situacao.mensagem}`
                : situacao.destino === 'aba' ? 'Imprima na aba que foi aberta e volte aqui. O papel saiu corretamente?'
                  : 'O papel saiu corretamente? Confirme depois de conferir a folha.'}
            </p>}
            {situacao.estado === 'confirmada' && <p ref={focoEstado} tabIndex={-1} className={css.aviso}>Saída confirmada no histórico.{situacao.mensagem && ` ${situacao.mensagem}`}</p>}
            {situacao.estado === 'erro' && <p ref={focoEstado} tabIndex={-1} className={css.erro} role="alert">{situacao.mensagem}</p>}
          </div>
          <div className={css.acoesRodape}>
          {!confirmarPapel && <Botao variante="texto" onClick={fechar}>Fechar</Botao>}
          {confirmarPapel ? <>
            <Botao variante="secundario" disabled={ocupado} onClick={() => setSituacao({ estado: 'pronto' })}>{situacao.erroConfirmacao ? 'Voltar à prévia' : 'Não saiu / cancelei'}</Botao>
            <Botao variante="primario" disabled={ocupado} onClick={confirmarSaida}>{ocupado ? 'Registrando…' : situacao.erroConfirmacao ? 'Registrar novamente' : 'Sim, o papel saiu'}</Botao>
          </> : situacao.estado !== 'confirmada' && <Botao variante="primario" icone={registro ? 'printer' : 'eye'} disabled={ocupado} onClick={() => registro ? imprimir() : carregar()}>
            {ocupado ? 'Preparando…' : situacao.estado === 'erro' ? registro ? 'Tentar abrir impressão' : 'Tentar preparar novamente' : registro ? 'Abrir impressão' : 'Preparar prévia'}
          </Botao>}
          </div>
        </div>
      )}>
        <div className={[css.layout, resumoAtivo && css.preparada].filter(Boolean).join(' ')}>
          <div className={css.opcoes}>
            {resumoAtivo ? <div className={css.resumoPreparado}>
              <div><strong>{rotuloDe(documentos, resumoAtivo.documento)} · {rotuloDe(papeis, resumoAtivo.modelo)}</strong>
                <span>{rotuloDe(CODIGOS, resumoAtivo.codigo)} · {resumoAtivo.perfil}{resumoAtivo.originalId ? ' · reimpressão do original' : ''}</span></div>
              <Botao variante="texto" onClick={limpar} disabled={ocupado || confirmarPapel}>{resumoAtivo.originalId ? 'Nova impressão' : 'Alterar opções'}</Botao>
            </div> : <>
            <p className={css.etapa}>1. Documento e destino</p>
            <CampoSelecao rotulo="Documento" opcoes={documentos} value={documento} disabled={congelado} onChange={(e) => escolherDocumento(e.target.value)} />
            <CampoSelecao rotulo="Perfil de impressão" opcoes={perfis} value={perfil} disabled={congelado} onChange={(e) => escolherPerfil(e.target.value)} />
            <CampoSelecao rotulo="Papel" opcoes={papeis.filter((p) => perfilDe(perfil).papeis.includes(p.valor))} value={papel ?? ''} disabled={congelado} onChange={(e) => { limpar(); setPapel(e.target.value) }} />
            <CampoSelecao rotulo="Código" opcoes={CODIGOS} value={codigo} disabled={congelado} onChange={(e) => { limpar(); setCodigo(e.target.value) }} />
            <p className={css.dica}>{orientacao?.(papel) ?? 'Escolha a impressora física no diálogo do sistema, com escala 100% e cabeçalhos e rodapés desligados.'}</p>
            <p className={css.dica}>Uma via por abertura. Defina as cópias no diálogo. Os papéis térmicos aguardam teste nos seus equipamentos.</p>
            </>}
          </div>
          <section className={css.previa} aria-label="Prévia do documento">
            <h3 ref={focoPrevia} tabIndex={-1} className={css.etapa}>Confira o papel</h3>
            {registro ? <>
              <PreviaImpresso html={registro.html} modelo={registro.modelo} />
              <p className={css.dica}>{orientacao?.(registro.modelo) ?? `No diálogo, selecione ${rotuloDe(papeis, registro.modelo)}, escala 100% e desligue cabeçalhos e rodapés.`} Confira também a paginação.</p>
            </> : <p className={css.vazia}>Prepare a prévia para conferir o documento antes de imprimir.</p>}
          </section>
        </div>
        <section className={css.historico} aria-label="Histórico de impressão deste pedido" aria-busy={carregandoHistorico}>
          <h3>Histórico deste pedido</h3>
          <p className={css.dica}>Reimprimir guarda uma nova tentativa e repete o conteúdo original. Uma prévia guardada não comprova a saída do papel.</p>
          {erroHistorico && <div className={css.erroHistorico}><p className={css.erro} role="alert">{erroHistorico}</p>
            <Botao variante="secundario" onClick={() => setRevisao((r) => r + 1)} disabled={carregandoHistorico}>Tentar carregar histórico novamente</Botao></div>}
          {carregandoHistorico && <output className={css.dica}>Carregando histórico…</output>}
          {!carregandoHistorico && !lista.length && !erroHistorico && <p className={css.dica}>Nenhum documento nesta página do histórico.</p>}
          <ul className={css.registros}>{lista.map((i, indice) => <li key={i.id}>
            <div><strong>{rotuloDe(documentos, i.documento)} · {rotuloDe(papeis, i.modelo)}</strong><span>{data(i.criadaEm)} · {estadoRegistro(i.status)}{i.originalId ? ' · reimpressão' : ''}</span>
              <details className={css.detalhes}><summary>Detalhes do documento</summary><span>{i.perfil} · {rotuloDe(CODIGOS, i.codigo)} · versão {i.versao}</span></details></div>
            <Botao variante="secundario" aria-label={`Reimprimir original: ${rotuloDe(documentos, i.documento)} de ${data(i.criadaEm)}, registro ${(pagina - 1) * 20 + indice + 1}`} disabled={ocupado || confirmarPapel} onClick={() => { limpar(); carregar(i) }}>Reimprimir original</Botao>
          </li>)}</ul>
          <div className={css.paginas}><Botao variante="texto" disabled={pagina === 1 || ocupado} onClick={() => setPagina((p) => p - 1)}>Anterior</Botao><span>Página {pagina}</span><Botao variante="texto" disabled={lista.length < 20 || ocupado} onClick={() => setPagina((p) => p + 1)}>Próxima</Botao></div>
        </section>
      </Modal>}
    </span>
  )
}
