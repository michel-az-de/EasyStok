import { useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../componentes/Botao'
import { CampoTexto } from '../componentes/Campo'
import { Modal } from '../componentes/Modal'
import { useAcoes, useAtendimento, useCatalogo } from '../aplicacao/contextos'
import { useSimulacaoDoPedidoComErro } from '../aplicacao/useSimulacaoDoPedido'
import {
  ajustarNaJanela, cadastrarEnderecoDaJanela, cardapioPronto, comandaEmAndamento, pedidoJaCriadoNaConversa, faltaEndereco, precisaNovaComanda, podeAvancarDaEntrega, rascunhoDaMontagem, registrarDaJanela,
  restaurarRascunho, somarNaJanela, textoDoErro,
} from '../aplicacao/janelaDoPedido'
import { EscolhaDeMeio } from '../features/ficha-cliente/EscolhaDeMeio'
import { SeletorJanelaApi } from '../features/ficha-cliente/SeletorJanelaApi'
import { disponivelHoje, itemPorSku, itensAtivos, porcaoDoToque, produtoDaPorcao } from '../dominio/cardapio'
import { filtrarCardapio } from '../dominio/filtroDoCardapio'
import { lerSessao } from '../infra/api/sessao'
import { moeda } from '../dominio/formato'
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
  const restaurado = useRef(false)
  const comandaDaConversa = useRef(comandaEmAndamento(conversa))
  const jaTemPedido = pedidoJaCriadoNaConversa(conversa)

  // Reabrir a mesma cliente: refaz a montagem com o cardápio de hoje e volta ao passo em que parou.
  useEffect(() => {
    if (restaurado.current) return
    // Pedido anterior já entregue ou cancelado: comanda nova (mesmo caminho do Atendimento) e restaura na volta.
    if (precisaNovaComanda(conversa)) { acoes.novaComanda(janela.conversaId); return }
    if (comandaDaConversa.current || jaTemPedido) { restaurado.current = true; return }
    const guardado = lerRascunhoDoPedido(sessao, janela.clienteId)
    if (guardado && !cardapioPronto(cardapio)) return
    restaurado.current = true
    if (!guardado) return
    const volta = restaurarRascunho(guardado, cardapio)
    for (const linha of volta.itens) {
      const item = itemPorSku(cardapio, linha.sku)
      for (let i = 0; i < linha.qtd; i += 1) somarNaJanela(acoes, janela, item)
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
    if (!restaurado.current || comandaDaConversa.current || jaTemPedido || registrando) return
    gravarRascunhoDoPedido(sessao, janela.clienteId, rascunhoDaMontagem(janela, pedido, passo, cardapio, { entrega, endereco: enderecoTexto }))
  }, [pedido, passo, enderecoTexto])

  const { simulacao, erro: erroSimulacao } = useSimulacaoDoPedidoComErro({
    conversaId: janela.conversaId, clienteId: janela.clienteId, endereco: enderecoCadastrado, pedido, ativo: passo !== 'itens',
  })
  const ativos = useMemo(() => filtrarCardapio(itensAtivos(cardapio), { busca }).filter((i) => disponivelHoje(i)), [cardapio, busca])
  const itens = pedido.itens ?? []
  const total = simulacao?.total ?? itens.reduce((soma, l) => soma + (itemPorSku(cardapio, l.sku)?.preco ?? 0) * l.qtd, 0)
  const nome = janela.nome || conversa.nome || 'a cliente'

  const fechar = () => acoes.fecharJanelaDoPedido()
  async function registrar() {
    setRegistrando(true); setErro(null)
    const resultado = await registrarDaJanela({
      acoes, janela, meio: pedido.meio, enviarAoCliente,
      aoRegistrar: () => { apagarRascunhoDoPedido(sessao, janela.clienteId); fechar() },
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
  function descartar() {
    apagarRascunhoDoPedido(sessao, janela.clienteId)
    for (const linha of itens) acoes.removerItem(janela.conversaId, linha.sku)
    fechar()
  }
  const podeAvancar = passo === 'itens' ? itens.length > 0 : passo === 'entrega' ? podeAvancarDaEntrega({ pedido, entrega, erroSimulacao }) : false
  const anterior = PASSOS[PASSOS.findIndex((p) => p.id === passo) - 1]
  const proximo = PASSOS[PASSOS.findIndex((p) => p.id === passo) + 1]

  const rodape = <>
    {erro && <p className={css.erro} role="alert">{erro}</p>}
    {passo === 'revisar' && (
      <label className={css.enviar}>
        <input type="checkbox" checked={enviarAoCliente} onChange={(e) => setEnviar(e.target.checked)} />
        Vamos enviar o resumo para {nome} no WhatsApp
      </label>
    )}
    {!comandaDaConversa.current && itens.length > 0 && (descartando
      ? <><Botao variante="texto" onClick={descartar}>Confirmar descarte</Botao><Botao variante="texto" onClick={() => setDescartando(false)}>Manter rascunho</Botao></>
      : <Botao variante="texto" onClick={() => setDescartando(true)}>Descartar rascunho</Botao>)}
    {anterior && <Botao onClick={() => setPasso(anterior.id)} disabled={registrando}>Voltar</Botao>}
    {proximo && <Botao variante="primario" disabled={!podeAvancar} onClick={() => setPasso(proximo.id)}>Continuar</Botao>}
    {passo === 'revisar' && <Botao variante="primario" disabled={registrando || itens.length === 0} onClick={registrar}>{registrando ? 'Registrando…' : 'Registrar'}</Botao>}
  </>

  if (jaTemPedido) {
    return (
      <Modal titulo={`Novo pedido de ${nome}`} largura="lg" aoFechar={fechar} rodape={<Botao variante="primario" onClick={fechar}>Entendi</Botao>}>
        <p className={css.aviso} role="status">{AVISO_PEDIDO_EM_ANDAMENTO}</p>
      </Modal>
    )
  }

  return (
    <Modal titulo={`Novo pedido de ${nome}`} largura="lg" aoFechar={registrando ? undefined : fechar} segurarFechamento={registrando} rodape={rodape}>
      <ol className={css.passos} aria-label="Passos do pedido">
        {PASSOS.map((p) => <li key={p.id} aria-current={p.id === passo ? 'step' : undefined} className={p.id === passo ? css.atual : undefined}>{p.rotulo}</li>)}
      </ol>
      {avisos.map((aviso) => <p key={aviso} className={css.aviso} role="status">{aviso}</p>)}
      {passo === 'itens' && (
        <div className={css.colunas}>
          <section aria-label="Cardápio do dia">
            <CampoTexto rotulo="Buscar no cardápio" value={busca} onChange={(e) => setBusca(e.target.value)} />
            <ul className={css.lista}>
              {ativos.map((item) => {
                const produto = doToque(item)
                return <li key={item.sku}><span>{item.nome} <small>{moeda(produto?.preco ?? item.preco)}</small></span>
                  <Botao disabled={!produto} aria-label={`Somar ${item.nome}`} onClick={() => somarNaJanela(acoes, janela, produto)}>+</Botao></li>
              })}
            </ul>
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
          <CampoTexto rotulo="Endereço de entrega" value={enderecoTexto} onChange={(e) => { setEnderecoTexto(e.target.value); setErroEndereco(null) }} />
          <Botao disabled={cadastrando || !enderecoTexto.trim()} onClick={cadastrarEndereco}>{cadastrando ? 'Cadastrando…' : 'Cadastrar endereço'}</Botao>
          {erroEndereco && <p className={css.erro} role="alert">{erroEndereco}</p>}
          {enderecoCadastrado && !faltaEndereco(erroSimulacao) && <p className={css.aviso} role="status">Endereço cadastrado: {enderecoCadastrado}</p>}
          {erroSimulacao && <p className={css.erro} role="alert">{textoDoErro(erroSimulacao)}</p>}
          <h3 className={css.subtitulo}>Forma de pagamento</h3>
          <EscolhaDeMeio escolhido={pedido.meio ?? null} pedido={pedido} conversaId={janela.conversaId} aoEscolher={(meio) => acoes.escolherMeioPagamento(janela.conversaId, meio)} />
        </>
      )}
      {passo === 'revisar' && (
        <>
          <Linhas itens={itens} cardapio={cardapio} janela={janela} acoes={acoes} />
          <dl className={css.resumo}>
            {simulacao?.frete != null && <><dt>Frete</dt><dd>{moeda(simulacao.frete)}</dd></>}
            <dt>Total</dt><dd>{moeda(total)}</dd>
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
              <CampoTexto rotulo={`Observação de ${itemPorSku(cardapio, l.sku)?.nome ?? l.sku}`} value={l.obs ?? ''} onChange={(e) => acoes.ajustarObservacao(janela.conversaId, l.sku, e.target.value)} />
              <Botao aria-label="Tirar um" onClick={() => ajustarNaJanela(acoes, janela, l.sku, -1)}>−</Botao>
            </>
          )}
        </li>
      ))}
    </ul>
  )
}
