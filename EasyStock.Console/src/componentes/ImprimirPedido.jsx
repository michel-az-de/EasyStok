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

// #1839: o modal abre já com o papel na tela (prévia sem registro, `obterPrevia`) e um botão só,
// "Imprimir", que abre o diálogo do sistema na hora com a prévia que a pessoa está vendo e grava a
// tentativa no histórico em paralelo (`preparar`). A conferência "saiu certo?" fica no rodapé, sem
// bloquear o Fechar. `semGatilho` + `aoFechar` deixam a tela que chamou abrir o modal direto
// (o "Ver canhoto" do Balcão), sem um modal intermediário.
export function ImprimirPedido({
  documentos, papeis, papelPadrao, perfis = perfilLivre(papeis), perfilPadrao = () => perfis[0].valor, orientacao,
  preparar, obterPrevia, historico, confirmar, documentoInicial, aoImprimir, aoAbrirImpressao, abrirImpressao = imprimirHtml,
  rotulo = 'Imprimir', variante = 'secundario', titulo = 'Imprimir pedido', contexto, clienteNome,
  semGatilho = false, semHistorico = false, semCodigo = false, aoFechar, ...resto
}) {
  const inicial = documentoInicial ?? documentos[0].valor
  const perfilInicial = (perfis.find((p) => p.valor === perfilPadrao(inicial)) ?? perfis[0]).valor
  const [aberto, setAberto] = useState(semGatilho)
  const [documento, setDocumento] = useState(inicial)
  const [papel, setPapel] = useState(semGatilho ? papelPadrao(inicial) : null)
  const [codigo, setCodigo] = useState('qr')
  const [perfil, setPerfil] = useState(semGatilho ? perfilInicial : 'sistema')
  const [previa, setPrevia] = useState(null)
  const [recargaPrevia, setRecargaPrevia] = useState(0)
  const [registro, setRegistro] = useState(null)
  const [reimpressao, setReimpressao] = useState(null)
  const [situacao, setSituacao] = useState({ estado: 'ocioso' })
  const [lista, setLista] = useState([])
  const [pagina, setPagina] = useState(1)
  const [revisao, setRevisao] = useState(0)
  const [erroHistorico, setErroHistorico] = useState(null)
  const [carregandoHistorico, setCarregandoHistorico] = useState(false)
  const operacao = useRef(null)
  const solicitacao = useRef(null)
  const focoEstado = useRef(null)
  const historicoRef = useRef(historico)
  const previaRef = useRef(obterPrevia)
  useLayoutEffect(() => { historicoRef.current = historico; previaRef.current = obterPrevia }, [historico, obterPrevia])

  useEffect(() => () => operacao.current?.abort(), [])
  useEffect(() => {
    if (aberto && ['aguardando', 'confirmando', 'confirmada', 'erro'].includes(situacao.estado)) focoEstado.current?.focus({ preventScroll: true })
  }, [aberto, situacao.estado])
  // Prévia viva: cada troca de documento, papel ou código pede o papel de novo; a resposta só vale
  // para a combinação que a pediu (`chave`), então a anterior nunca aparece como atual.
  const chavePrevia = `${documento}|${papel}|${codigo}|${recargaPrevia}`
  useEffect(() => {
    if (!aberto || !papel || reimpressao) return undefined
    const controle = new AbortController()
    const chave = `${documento}|${papel}|${codigo}|${recargaPrevia}`
    Promise.resolve(previaRef.current?.({ documento, modelo: papel, codigo }, controle.signal))
      .then((html) => { if (!controle.signal.aborted) setPrevia(html ? { chave, estado: 'pronta', html, modelo: papel } : { chave, estado: 'erro', mensagem: 'O papel veio vazio.' }) })
      .catch((erro) => { if (!controle.signal.aborted) setPrevia({ chave, estado: 'erro', mensagem: erro.message }) })
    return () => controle.abort()
  }, [aberto, documento, papel, codigo, reimpressao, recargaPrevia])
  const previaAtual = previa?.chave === chavePrevia ? previa : null
  useEffect(() => {
    if (!aberto || semHistorico) return undefined
    const controle = new AbortController()
    setErroHistorico(null)
    setCarregandoHistorico(true)
    setLista([])
    historicoRef.current(pagina, controle.signal).then((itens) => {
      if (!controle.signal.aborted) setLista(itens)
    }).catch((erro) => { if (!controle.signal.aborted) setErroHistorico(erro.message) })
      .finally(() => { if (!controle.signal.aborted) setCarregandoHistorico(false) })
    return () => controle.abort()
  }, [aberto, pagina, revisao, semHistorico])

  const fechar = () => { operacao.current?.abort(); operacao.current = null; setAberto(false); aoFechar?.() }
  const limpar = () => { solicitacao.current = null; setRegistro(null); setReimpressao(null); setSituacao({ estado: 'ocioso' }) }
  const perfilDe = (valor) => perfis.find((p) => p.valor === valor) ?? perfis[0]
  // Documento traz o perfil e o papel guardados no aparelho; perfil dedicado puxa o papel dele.
  const escolherDocumento = (valor) => { limpar(); setDocumento(valor); setPerfil(perfilDe(perfilPadrao(valor)).valor); setPapel(papelPadrao(valor)) }
  const escolherPerfil = (valor) => {
    limpar(); setPerfil(valor)
    const compativeis = perfilDe(valor).papeis
    if (!compativeis.includes(papel)) setPapel(compativeis[0])
  }
  // Prévia de uma abertura anterior não vale: o pedido ou o resumo podem ter mudado (#1839).
  const abrir = () => { escolherDocumento(documento); setPrevia(null); setPagina(1); setAberto(true) }

  // Guarda a tentativa e imprime o documento guardado: o papel e o histórico são o mesmo HTML (#1839).
  // A prévia é para conferir; se o pedido mudou desde que ela abriu, sai o estado atual. A solicitação
  // repete o mesmo identificador até dar certo, então rede que cai depois do servidor gravar não duplica.
  // O resumo do atendimento (`semHistorico`) não é de um pedido: imprime a própria prévia.
  async function imprimir() {
    if (operacao.current || !papelNaTela) return
    const controle = new AbortController()
    operacao.current = controle
    setSituacao({ estado: 'imprimindo' })
    try {
      let html, tentativa = registro
      if (semHistorico) html = papelNaTela.html
      else {
        // A seleção do histórico já prepara a primeira via; cada nova abertura ganha outra tentativa.
        if (!reimpressao || !tentativa || tentativa.aberta) {
          setRegistro(null)
          const opcoes = reimpressao
            ? { documento: reimpressao.documento, modelo: reimpressao.modelo, codigo: reimpressao.codigo, perfil: reimpressao.perfil, originalId: reimpressao.id }
            : { documento, modelo: papel, codigo, perfil: perfilDe(perfil).rotulo }
          tentativa = await prepararUmaVez(opcoes, controle.signal)
          setRegistro(tentativa)
          setRevisao((r) => r + 1)
        }
        html = tentativa.html
      }
      const destino = await abrirImpressao(html, { sinal: controle.signal })
      controle.signal.throwIfAborted()
      if (!semHistorico) setRegistro({ ...tentativa, aberta: true })
      setSituacao(semHistorico ? { estado: 'ocioso' } : { estado: 'aguardando', destino })
      if (!reimpressao && !tentativa?.originalId) await aoAbrirImpressao?.(documento)
    } catch (erro) {
      if (!controle.signal.aborted) setSituacao({ estado: 'erro', mensagem: erro.message })
    } finally { if (operacao.current === controle) operacao.current = null }
  }

  async function prepararUmaVez(opcoes, sinal) {
    const chave = JSON.stringify(opcoes)
    if (solicitacao.current?.chave !== chave) solicitacao.current = { chave, id: crypto.randomUUID() }
    const pronto = await preparar({ ...opcoes, solicitacaoId: solicitacao.current.id }, sinal)
    sinal.throwIfAborted()
    solicitacao.current = null
    return pronto
  }

  async function confirmarSaida() {
    if (operacao.current || !registro) return
    const controle = new AbortController()
    operacao.current = controle
    setSituacao((atual) => ({ ...atual, estado: 'confirmando' }))
    let registrada = false
    try {
      await confirmar(registro)
      controle.signal.throwIfAborted()
      registrada = true
      setRegistro({ ...registro, status: 'impressa' })
      setRevisao((r) => r + 1)
      if (!reimpressao && !registro.originalId) await aoImprimir?.(registro.documento)
      controle.signal.throwIfAborted()
      setSituacao({ estado: 'confirmada' })
    } catch (erro) {
      if (!controle.signal.aborted) setSituacao(registrada
        ? { estado: 'confirmada', mensagem: `Não foi possível atualizar a lista de pedidos. ${erro.message}` }
        : { estado: 'aguardando', mensagem: erro.message, erroConfirmacao: true })
    } finally { if (operacao.current === controle) operacao.current = null }
  }

  // O original guardado volta pelo `preparar` com `originalId`, sem montar o documento de novo.
  async function reimprimirOriginal(item) {
    if (operacao.current) return
    const controle = new AbortController()
    operacao.current = controle
    setSituacao({ estado: 'carregando' })
    setRegistro(null)
    setReimpressao({ ...item, html: null })
    try {
      const pronto = await prepararUmaVez({ documento: item.documento, modelo: item.modelo, codigo: item.codigo, perfil: item.perfil, originalId: item.id }, controle.signal)
      setRegistro(pronto)
      setReimpressao({ ...item, html: pronto.html })
      setRevisao((r) => r + 1)
      setSituacao({ estado: 'ocioso' })
    } catch (erro) {
      if (!controle.signal.aborted) setSituacao({ estado: 'erro', mensagem: erro.message })
    } finally { if (operacao.current === controle) operacao.current = null }
  }

  const ocupado = ['imprimindo', 'confirmando', 'carregando'].includes(situacao.estado)
  const conferir = ['aguardando', 'confirmando'].includes(situacao.estado)
  const papelNaTela = reimpressao ? reimpressao.html ? { html: reimpressao.html, modelo: reimpressao.modelo } : null : previaAtual?.estado === 'pronta' ? previaAtual : null
  return (
    <span style={{ display: 'contents' }} onPointerDown={naoPropagar} onMouseDown={naoPropagar} onTouchStart={naoPropagar}>
      {!semGatilho && <Botao variante={variante} icone="printer" onClick={abrir} {...resto}>{rotulo}</Botao>}
      {aberto && <Modal titulo={contexto ? `${titulo} ${contexto}` : titulo} descricao={clienteNome ? `Cliente: ${clienteNome}` : undefined} aoFechar={fechar} largura="min(1060px, calc(100vw - 24px))" rodape={(
        <div className={css.rodapeImpressao}>
          <div aria-live="polite">
            {conferir && <div className={css.conferir}>
              <p ref={focoEstado} tabIndex={-1} className={situacao.erroConfirmacao || situacao.semRegistro ? css.erro : css.dica}>
                {situacao.estado === 'confirmando' ? 'Registrando…' : situacao.mensagem
                  ?? (situacao.destino === 'aba' ? 'Imprima na aba que abriu. Saiu certo?' : 'Saiu certo?')}
              </p>
              {registro && <>
                <Botao variante="texto" disabled={ocupado} onClick={() => setSituacao({ estado: 'ocioso' })}>Não saiu</Botao>
                <Botao variante="secundario" disabled={ocupado} onClick={confirmarSaida}>{situacao.erroConfirmacao ? 'Registrar novamente' : 'Sim, saiu'}</Botao>
              </>}
            </div>}
            {situacao.estado === 'confirmada' && <p ref={focoEstado} tabIndex={-1} className={css.dica}>Saída registrada no histórico.{situacao.mensagem && ` ${situacao.mensagem}`}</p>}
            {situacao.estado === 'erro' && <p ref={focoEstado} tabIndex={-1} className={css.erro} role="alert">{situacao.mensagem}</p>}
          </div>
          <div className={css.acoesRodape}>
            <Botao variante="texto" onClick={fechar}>Fechar</Botao>
            <Botao variante="primario" icone="printer" disabled={!papelNaTela || ocupado} onClick={imprimir}>
              {situacao.estado === 'imprimindo' ? 'Abrindo…' : conferir || situacao.estado === 'confirmada' ? 'Imprimir de novo' : 'Imprimir'}
            </Botao>
          </div>
        </div>
      )}>
        <div className={css.layout}>
          <div className={css.opcoes}>
            {reimpressao ? <div className={css.resumoPreparado}>
              <div><strong>{rotuloDe(documentos, reimpressao.documento)} · {rotuloDe(papeis, reimpressao.modelo)}</strong>
                <span>{rotuloDe(CODIGOS, reimpressao.codigo)} · {reimpressao.perfil} · reimpressão do original</span></div>
              <Botao variante="texto" onClick={limpar} disabled={ocupado}>Nova impressão</Botao>
            </div> : <>
              <CampoSelecao rotulo="Documento" opcoes={documentos} value={documento} disabled={ocupado} onChange={(e) => escolherDocumento(e.target.value)} />
              <CampoSelecao rotulo="Perfil de impressão" opcoes={perfis} value={perfil} disabled={ocupado} onChange={(e) => escolherPerfil(e.target.value)} />
              <CampoSelecao rotulo="Papel" opcoes={papeis.filter((p) => perfilDe(perfil).papeis.includes(p.valor))} value={papel ?? ''} disabled={ocupado} onChange={(e) => { limpar(); setPapel(e.target.value) }} />
              {!semCodigo && <CampoSelecao rotulo="Código" opcoes={CODIGOS} value={codigo} disabled={ocupado} onChange={(e) => { limpar(); setCodigo(e.target.value) }} />}
            </>}
            <p className={css.dica}>{orientacao?.(papelNaTela?.modelo ?? papel) ?? 'No diálogo, escolha a impressora, escala 100% e desligue cabeçalhos e rodapés.'}</p>
            <p className={css.dica}>Para guardar em PDF, escolha "Salvar como PDF" no diálogo.</p>
          </div>
          <section className={css.previa} aria-label="Prévia do documento" aria-busy={!papelNaTela && previaAtual?.estado !== 'erro'}>
            {papelNaTela ? <PreviaImpresso html={papelNaTela.html} modelo={papelNaTela.modelo} />
              : previaAtual?.estado === 'erro' ? <div className={css.vazia}>
                <p className={css.erro} role="alert">{previaAtual.mensagem}</p>
                <Botao variante="secundario" onClick={() => setRecargaPrevia((n) => n + 1)}>Tentar de novo</Botao>
              </div>
              : <p className={css.vazia}>Montando o papel…</p>}
          </section>
        </div>
        {!semHistorico && <details className={css.historico}>
          <summary>Histórico deste pedido</summary>
          <div aria-busy={carregandoHistorico}>
            {erroHistorico && <div className={css.erroHistorico}><p className={css.erro} role="alert">{erroHistorico}</p>
              <Botao variante="secundario" onClick={() => setRevisao((r) => r + 1)} disabled={carregandoHistorico}>Tentar carregar histórico novamente</Botao></div>}
            {carregandoHistorico && <output className={css.dica}>Carregando histórico…</output>}
            {!carregandoHistorico && !lista.length && !erroHistorico && <p className={css.dica}>Nada impresso deste pedido ainda.</p>}
            <ul className={css.registros}>{lista.map((i, indice) => <li key={i.id}>
              <div><strong>{rotuloDe(documentos, i.documento)} · {rotuloDe(papeis, i.modelo)}</strong><span>{data(i.criadaEm)} · {estadoRegistro(i.status)}{i.originalId ? ' · reimpressão' : ''}</span></div>
              <Botao variante="secundario" aria-label={`Reimprimir original: ${rotuloDe(documentos, i.documento)} de ${data(i.criadaEm)}, registro ${(pagina - 1) * 20 + indice + 1}`} disabled={ocupado} onClick={() => reimprimirOriginal(i)}>Reimprimir original</Botao>
            </li>)}</ul>
            <div className={css.paginas}><Botao variante="texto" disabled={pagina === 1 || ocupado} onClick={() => setPagina((p) => p - 1)}>Anterior</Botao><span>Página {pagina}</span><Botao variante="texto" disabled={lista.length < 20 || ocupado} onClick={() => setPagina((p) => p + 1)}>Próxima</Botao></div>
          </div>
        </details>}
      </Modal>}
    </span>
  )
}
