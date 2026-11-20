import { useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../componentes/Botao'
import { CampoTexto } from '../componentes/Campo'
import { CampoReais } from '../componentes/CampoReais'
import { EsqueletoDeLista } from '../componentes/Esqueleto'
import { Modal } from '../componentes/Modal'
import { AvisoDaAcaoApi } from './FaixaApi'
import { anunciarPedidoRegistrado } from '../aplicacao/pedidoRegistrado'
import { ligarVoltarDaJanela } from '../aplicacao/historicoDaJanela'
import { useAcoes, useAtendimento, useCatalogo } from '../aplicacao/contextos'
import { useSimulacaoDoPedidoComErro } from '../aplicacao/useSimulacaoDoPedido'
import {
  ajustarNaJanela, avisoSemCelular, cadastrarCelularDaJanela, cadastrarEnderecoDaJanela, motivoDeBloqueio, textoDaFaixa, cardapioPronto, comandaDeFora, enderecoVigente, mostrarAvisoDePedidoEmAndamento, pedidoJaCriadoNaConversa, faltaEndereco, precisaNovaComanda, podeAvancarDaEntrega, rascunhoDaMontagem, registrarDaJanela, somaEhDaJanela,
  restaurarRascunho, somarNaJanela, textoDoErro,
} from '../aplicacao/janelaDoPedido'
import { EscolhaDeMeio } from '../features/ficha-cliente/EscolhaDeMeio'
import { SeletorJanelaApi } from '../features/ficha-cliente/SeletorJanelaApi'
import { disponivelHoje, itemPorSku, itensAtivos, porcaoDoToque, produtoDaPorcao } from '../dominio/cardapio'
import { simulacaoVale } from '../dominio/simulacaoPedido'
import { filtrarCardapio } from '../dominio/filtroDoCardapio'
import { lerSessao } from '../infra/api/sessao'
import { lerMoeda, moeda } from '../dominio/formato'
import { apagarRascunhoDoPedido, gravarRascunhoDoPedido, lerRascunhoDoPedido } from '../infra/api/rascunhosDoPedido'
import css from './novoPedidoJanela.module.css'

// #2921 (fase 1): o "Novo pedido" monta na própria tela (Pedidos, Clientes, Ficha), sem trocar a hash nem selecionar a
// conversa. A montagem mora em `conversas[].pedido` da conversa da janela (`janelaDoPedido.conversaId`). Fica em `app/`
// porque junta Atendimento, Ficha e Cardápio, e feature não importa feature. Não há campo de mensagem aqui: o resumo
// sai pelo `enviarAoCliente` do próprio pedido.
const AVISO_PEDIDO_EM_ANDAMENTO = 'Esta cliente já tem um pedido em andamento nesta conversa. Veja e ajuste o pedido em Pedidos ou no Atendimento; um novo pedido só pode ser montado depois que ele for concluído.'
const PASSOS = [{ id: 'itens', rotulo: 'Itens' }, { id: 'entrega', rotulo: 'Entrega e pagamento' }, { id: 'revisar', rotulo: 'Revisar' }]

const doToque = (item) => {
  if ((item.porcoes ?? []).length === 0) return item
  const porcao = porcaoDoToque(item)
  return porcao ? produtoDaPorcao(item, porcao) : null
}

export function NovoPedidoJanela() {
  const { janelaDoPedido, conversas } = useAtendimento()
  if (!janelaDoPedido) return null
  const conversa = conversas.find((c) => c.id === janelaDoPedido.conversaId)
  if (!conversa) return null
  return <Montagem key={janelaDoPedido.conversaId} janela={janelaDoPedido} conversa={conversa} />
}

function Montagem({ janela, conversa }) {
  const sessao = lerSessao()
  const acoes = useAcoes()
  const { cardapio } = useCatalogo()
  const pedido = conversa.pedido ?? { itens: [] }
  const [passo, setPasso] = useState('itens')
  const [busca, setBusca] = useState('')
  const [avisos, setAvisos] = useState([])
  const [enviarAoCliente, setEnviar] = useState(true)
  const [registrando, setRegistrando] = useState(false)
  const [erro, setErro] = useState(null)
  const [descartando, setDescartando] = useState(false)
  // Entrega e endereço como na comanda: a API usa o endereço do cadastro; sem ele, a janela o cadastra aqui.
  const entrega = 'entrega'
  const [enderecoTexto, setEnderecoTexto] = useState(() => conversa.cliente?.endereco ?? conversa.cliente?.enderecoCapturado ?? '')
  const [enderecoCadastrado, setEnderecoCadastrado] = useState(null)
  const [cadastrando, setCadastrando] = useState(false)
  const [erroEndereco, setErroEndereco] = useState(null)
  const [celular, setCelular] = useState({ texto: '', erro: null, gravando: false })
  const restaurado = useRef(false)
  // #2944 (E1-02): a troca de passo remove o botão focado; o foco vai ao título do passo (WCAG 2.4.3).
  const tituloDoPasso = useRef(null)
  const primeiroPasso = useRef(true)
  const manter = useRef(null)
  // #2941: a montagem da própria janela não é "comanda de fora"; a de fora é reavaliada a cada render.
  const montouNaJanela = useRef(false)
  const deFora = comandaDeFora(conversa, montouNaJanela.current)
  const jaTemPedido = pedidoJaCriadoNaConversa(conversa)

  // Reabrir a mesma cliente: refaz a montagem com o cardápio de hoje e volta ao passo em que parou.
  useEffect(() => {
    if (restaurado.current) return
    // Pedido anterior já entregue ou cancelado: comanda nova (mesmo caminho do Atendimento) e restaura na volta.
    if (precisaNovaComanda(conversa)) { acoes.novaComanda(janela.conversaId); return }
    if (deFora || jaTemPedido) { restaurado.current = true; return }
    const guardado = lerRascunhoDoPedido(sessao, janela.clienteId)
    if (guardado && !cardapioPronto(cardapio)) return
    restaurado.current = true
    if (!guardado) return
    const volta = restaurarRascunho(guardado, cardapio)
    for (const linha of volta.itens) {
      const item = itemPorSku(cardapio, linha.sku)
      for (let i = 0; i < linha.qtd; i += 1) { montouNaJanela.current = true; somarNaJanela(acoes, janela, item) }
      if (linha.obs) acoes.ajustarObservacao(janela.conversaId, linha.sku, linha.obs)
    }
    if (volta.endereco) setEnderecoTexto(volta.endereco)
    if (volta.janela) acoes.escolherJanela(janela.conversaId, volta.janela)
    if (volta.meio) acoes.escolherMeioPagamento?.(janela.conversaId, volta.meio)
    setAvisos(volta.avisos)
    setPasso(volta.itens.length > 0 ? volta.passo : 'itens')
  }, [cardapio, conversa.pedido])

  // Fechar, trocar de tela ou recarregar guarda a montagem (sessionStorage, por cliente).
  useEffect(() => {
    if (!restaurado.current || deFora || jaTemPedido || registrando) return
    gravarRascunhoDoPedido(sessao, janela.clienteId, rascunhoDaMontagem(janela, pedido, passo, cardapio, { entrega, endereco: enderecoTexto }))
  }, [pedido, passo, enderecoTexto, deFora])

  const { simulacao, erro: erroSimulacao } = useSimulacaoDoPedidoComErro({
    conversaId: janela.conversaId, clienteId: janela.clienteId, endereco: enderecoCadastrado, pedido, ativo: passo !== 'itens',
  })
  useEffect(() => {
    if (primeiroPasso.current) { primeiroPasso.current = false; return }
    tituloDoPasso.current?.focus()
  }, [passo])
  useEffect(() => {
    if (descartando) manter.current?.focus()
  }, [descartando])
  // #2944 (E1-05): "Calculando frete…" vem da mesma chave de simulação do hook: pedido que vale, sem resposta e sem erro.
  const calculando = passo !== 'itens' && simulacaoVale(pedido) && !simulacao && !erroSimulacao
  const ativos = useMemo(() => filtrarCardapio(itensAtivos(cardapio), { busca }).filter((i) => disponivelHoje(i)), [cardapio, busca])
  const itens = pedido.itens ?? []
  const qtdDe = (sku) => itens.find((l) => l.sku === sku)?.qtd ?? 0
  const total = simulacao?.total ?? itens.reduce((soma, l) => soma + (itemPorSku(cardapio, l.sku)?.preco ?? 0) * l.qtd, 0)
  const nome = janela.nome || conversa.nome || 'a cliente'

  const fechar = () => acoes.fecharJanelaDoPedido()
  // #2944 (E1-18): voltar do navegador fecha a janela (rascunho já guardado), menos no meio do Registrar.
  const registrandoRef = useRef(false)
  registrandoRef.current = registrando
  useEffect(() => ligarVoltarDaJanela(window, () => { if (!registrandoRef.current) acoes.fecharJanelaDoPedido() }), [])
  async function registrar() {
    setRegistrando(true); setErro(null)
    const resultado = await registrarDaJanela({
      acoes, janela, meio: pedido.meio, enviarAoCliente,
      aoRegistrar: (pedidoNovo) => { apagarRascunhoDoPedido(sessao, janela.clienteId); anunciarPedidoRegistrado(pedidoNovo); fechar() },
    })
    if (resultado.erro) { setErro(resultado.erro); setRegistrando(false) }
  }
  async function cadastrarEndereco() {
    setCadastrando(true); setErroEndereco(null)
    const r = await cadastrarEnderecoDaJanela({ acoes, conversa, texto: enderecoTexto })
    if (r.erro) setErroEndereco(r.erro)
    else setEnderecoCadastrado(enderecoTexto.trim())
    setCadastrando(false)
  }
  async function cadastrarCelular() {
    setCelular((c) => ({ ...c, erro: null, gravando: true }))
    const r = await cadastrarCelularDaJanela({ acoes, conversa, texto: celular.texto })
    setCelular((c) => ({ ...c, erro: r.erro ?? null, gravando: false }))
  }
  function descartar() {
    apagarRascunhoDoPedido(sessao, janela.clienteId)
    for (const linha of itens) acoes.removerItem(janela.conversaId, linha.sku)
    fechar()
  }
  const podeAvancar = passo === 'itens' ? itens.length > 0 : passo === 'entrega' ? podeAvancarDaEntrega({ pedido, entrega, erroSimulacao, simulacao }) : false
  const anterior = PASSOS[PASSOS.findIndex((p) => p.id === passo) - 1]
  const proximo = PASSOS[PASSOS.findIndex((p) => p.id === passo) + 1]

  // #2944 (E1-01): o motivo do bloqueio fica visível ao lado do botão. O texto "Pode continuar" nunca aparece (só com o
  // botão desabilitado); ele mantém o invólucro do Botao estável para o foco não se perder ao habilitar.
  const motivoAvancar = motivoDeBloqueio({ passo, itens, pedido, entrega, erroSimulacao, simulacao })
  const motivoRegistrando = 'Registrando o pedido'
  const rodape = <>
    <AvisoDaAcaoApi />
    <p className={css.faixa} aria-live="polite" aria-busy={calculando}>{textoDaFaixa(itens, moeda(total))}</p>
    {erro && <p className={css.erro} role="alert">{erro}</p>}
    {passo === 'revisar' && (
      <label className={css.enviar}>
        <input type="checkbox" checked={enviarAoCliente} onChange={(e) => setEnviar(e.target.checked)} />
        Vamos enviar o resumo para {nome} no WhatsApp
      </label>
    )}
    {!deFora && itens.length > 0 && (descartando
      ? <>
        <Botao variante="perigo" disabled={registrando} motivo={motivoRegistrando} onClick={descartar}>Confirmar descarte</Botao>
        <Botao ref={manter} variante="texto" disabled={registrando} motivo={motivoRegistrando} onClick={() => setDescartando(false)}>Manter rascunho</Botao>
      </>
      : <Botao variante="texto" onClick={() => setDescartando(true)}>Descartar rascunho</Botao>)}
    {anterior && <Botao onClick={() => setPasso(anterior.id)} disabled={registrando} motivo={motivoRegistrando}>Voltar</Botao>}
    {proximo && <Botao variante="primario" disabled={!podeAvancar} motivo={motivoAvancar ?? 'Pode continuar'} onClick={() => setPasso(proximo.id)}>Continuar</Botao>}
    {passo === 'revisar' && <Botao variante="primario" disabled={registrando || itens.length === 0} motivo={motivoAvancar ?? motivoRegistrando} onClick={registrar}>{registrando ? 'Registrando…' : 'Registrar pedido'}</Botao>}
  </>

  if (mostrarAvisoDePedidoEmAndamento({ jaTemPedido, registrando })) {
    return (
      <Modal titulo={`Novo pedido de ${nome}`} largura="lg" aoFechar={fechar} rodape={<Botao variante="primario" onClick={fechar}>Entendi</Botao>}>
        <p className={css.aviso} role="status">{AVISO_PEDIDO_EM_ANDAMENTO}</p>
      </Modal>
    )
  }

  return (
    <Modal titulo={`Novo pedido de ${nome}`} largura="lg" aoFechar={registrando ? undefined : fechar} segurarFechamento={registrando} rodape={rodape}>
      <ol className={css.passos} aria-label="Passos do pedido">
        {PASSOS.map((p) => <li key={p.id} ref={p.id === passo ? tituloDoPasso : undefined} tabIndex={p.id === passo ? -1 : undefined} aria-current={p.id === passo ? 'step' : undefined} className={p.id === passo ? css.atual : undefined}>{p.rotulo}</li>)}
      </ol>
      {calculando && <p className={css.vazio} role="status">Calculando frete…</p>}
      {avisos.map((aviso) => <p key={aviso} className={css.aviso} role="status">{aviso}</p>)}
      {passo === 'itens' && (
        <div className={css.colunas}>
          <section aria-label="Cardápio do dia">
            <CampoTexto rotulo="Buscar no cardápio" value={busca} onChange={(e) => setBusca(e.target.value)} />
            {!cardapioPronto(cardapio) ? <EsqueletoDeLista linhas={4}>Carregando o cardápio…</EsqueletoDeLista> : ativos.length === 0 ? <p className={css.vazio}>{busca ? `Nada no cardápio com "${busca}".` : 'Nada disponível no cardápio hoje.'}</p> : <ul className={css.lista}>
              {ativos.map((item) => {
                const produto = doToque(item)
                return <li key={item.sku}><span>{qtdDe(item.sku) > 0 && <strong className={css.qtd}>{qtdDe(item.sku)}x </strong>}{item.nome} <small>{moeda(produto?.preco ?? item.preco)}</small></span>
                  <Botao disabled={!produto} motivo="Sem porção disponível" aria-label={`Somar ${item.nome}`} onClick={() => { if (somaEhDaJanela(conversa, montouNaJanela.current)) montouNaJanela.current = true; somarNaJanela(acoes, janela, produto) }}>+</Botao></li>
              })}
            </ul>}
          </section>
          <section aria-label="Pedido">
            <Linhas itens={itens} cardapio={cardapio} janela={janela} acoes={acoes} editavel />
          </section>
        </div>
      )}
      {passo === 'entrega' && (
        <>
          <SeletorJanelaApi key={janela.conversaId} pedido={pedido} editavel aoEscolher={(j, avisar) => acoes.escolherJanela(janela.conversaId, j, avisar)} />
          <h3 className={css.subtitulo}>Endereço de entrega</h3>
          <CampoTexto rotulo="Endereço de entrega" value={enderecoTexto} erro={erroEndereco} onChange={(e) => { setEnderecoTexto(e.target.value); setErroEndereco(null) }} />
          <Botao disabled={cadastrando || !enderecoTexto.trim()} motivo={cadastrando ? 'Cadastrando…' : 'Escreva o endereço de entrega'} onClick={cadastrarEndereco}>{cadastrando ? 'Cadastrando…' : 'Cadastrar endereço'}</Botao>
          {enderecoVigente(enderecoCadastrado, enderecoTexto) && !faltaEndereco(erroSimulacao) && <p className={css.aviso} role="status">Endereço cadastrado: {enderecoVigente(enderecoCadastrado, enderecoTexto)}</p>}
          {erroSimulacao && <p className={css.erro} role="alert">{textoDoErro(erroSimulacao)}</p>}
          {/* #2974: fora da área o frete é combinado com a cliente, como na comanda do Atendimento (#2219). */}
          {(simulacao?.freteACombinar || pedido.freteCombinado !== undefined) && (
            <CampoReais
              rotulo="Frete combinado"
              dica="CEP fora da área de entrega. Digite o frete que você combinou com a cliente; zero é frete grátis."
              placeholder="A combinar"
              mostrarZero
              centavos={pedido.freteCombinado != null ? Math.round(pedido.freteCombinado * 100) : null}
              ler={lerMoeda}
              aoMudar={(centavos) => acoes.definirFreteCombinado(janela.conversaId, centavos == null ? null : centavos / 100)}
            />
          )}
          <h3 className={css.subtitulo}>Forma de pagamento</h3>
          <EscolhaDeMeio escolhido={pedido.meio ?? null} pedido={pedido} conversaId={janela.conversaId} aoEscolher={(meio) => acoes.escolherMeioPagamento(janela.conversaId, meio)} />
        </>
      )}
      {passo === 'revisar' && (
        <>
          <Linhas itens={itens} cardapio={cardapio} janela={janela} acoes={acoes} />
          {avisoSemCelular(conversa, enviarAoCliente) && <>
            <p className={css.aviso} role="status">{avisoSemCelular(conversa, enviarAoCliente)}</p>
            <CampoTexto rotulo="Celular com DDD" tipo="tel" inputMode="tel" autoComplete="off" placeholder="(11) 98888-7777" value={celular.texto} erro={celular.erro}
              dica="Fica gravado no cadastro da cliente." onChange={(e) => setCelular({ texto: e.target.value, erro: null, gravando: false })} />
            <Botao disabled={celular.gravando || !celular.texto.trim()} motivo={celular.gravando ? 'Gravando…' : 'Digite o celular com DDD'} onClick={cadastrarCelular}>{celular.gravando ? 'Gravando…' : 'Salvar celular'}</Botao>
          </>}
          <dl className={css.resumo}>
            {simulacao?.frete != null && <><dt>Frete</dt><dd>{moeda(simulacao.frete)}</dd></>}
            <dt>Total</dt><dd aria-busy={calculando}>{moeda(total)}</dd>
          </dl>
        </>
      )}
    </Modal>
  )
}

function Linhas({ itens, cardapio, janela, acoes, editavel = false }) {
  if (itens.length === 0) return <p className={css.vazio}>Some itens do cardápio para montar o pedido.</p>
  return (
    <ul className={css.lista}>
      {itens.map((l) => (
        <li key={l.sku}>
          <span>{l.qtd}x {itemPorSku(cardapio, l.sku)?.nome ?? l.sku}</span>
          {editavel && (
            <>
              <div className={css.obs}><CampoTexto rotulo={`Observação de ${itemPorSku(cardapio, l.sku)?.nome ?? l.sku}`} value={l.obs ?? ''} onChange={(e) => acoes.ajustarObservacao(janela.conversaId, l.sku, e.target.value)} /></div>
              <Botao aria-label={`Tirar um ${itemPorSku(cardapio, l.sku)?.nome ?? l.sku}`} onClick={() => ajustarNaJanela(acoes, janela, l.sku, -1)}>−</Botao>
            </>
          )}
        </li>
      ))}
    </ul>
  )
}
