import { useEffect, useRef, useState } from 'react'
import { Avatar } from '../../componentes/Avatar'
import { useFotoCliente } from '../../aplicacao/useFotoCliente'
import { Botao } from '../../componentes/Botao'
import { BotaoIcone } from '../../componentes/BotaoIcone'
import { Icone } from '../../componentes/Icone'
import { MenuDeAcoes } from '../../componentes/MenuDeAcoes'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useAcaoDisponivel } from '../../aplicacao/useAcaoDisponivel'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { esperaAtrasada, motivoDePrecisar } from '../../dominio/automatico'
import { acoesDaConversa } from '../../dominio/acoesDaConversa'
import { conversaEncerrada, fronteirasDoFio } from '../../dominio/conversa'
import { podeEnviarModelo as regraPodeEnviarModelo } from '../../dominio/mensagensPadrao'
import { MOTIVO_BLOQUEIO, esperaNoCabecalho, modelosAVista, permissaoDeEscrita, prazoFolgado, prazoNoCabecalho } from '../../dominio/janela'
import { canalDaConversa, fotoDoCliente } from '../../dominio/canal'
import { apresentacaoDoNome, nomeDaConversa } from '../../dominio/nomeDoContato'
import { cargaDaLista } from '../../dominio/cargaDaLista'
import { NomeDoContato } from '../../componentes/NomeDoContato'
import { itemDaBibliotecaPelaRegra } from '../../dominio/respostas'
import { AnfitriaoDeConfirmacao } from './AnfitriaoDeConfirmacao'
import { apresentacaoDoModo, motivoDaBarra } from '../../dominio/resumoDoModo'
import { DESKTOP, useTamanhoTela } from '../../hooks/useTamanhoTela'
import { BarraModo } from './BarraModo'
import { useAssumirSemRepetir } from './useAssumirSemRepetir'
import { Composer } from './Composer'
import { ModalCompartilharComanda } from './CompartilharComanda'
import { ModalAssistente } from './ModalAssistente'
import { ModalDevolverAssistente } from './ModalDevolverAssistente'
import { ModalPassarConversa } from './PassarConversa'
import { rotuloDoPedido } from '../../dominio/rotulos'
import { Thread } from './Thread'
import { devolverFocoAoCampoDepoisDeFechar } from './focoDoComposer'
import css from './atendimento.module.css'

// #3374 (C8): selo no cabeçalho medido e recusado. Com a flag ligada, na comanda aberta (prova-1738, 1280x720) o
// cabeçalho foi a 79 px em 1280 e 1440 (64 em 1920) e a comanda cobriu o "Total dos itens". Regra da banca: só liga se
// couber em uma linha nas três larguras; senão a faixa (BarraModo) fica abaixo do cabeçalho, em uma linha só.
const SELO_NO_CABECALHO = false

// #3037: o anfitrião da confirmação de "conversa de outra" vale para todo o Painel, com ou sem conversa aberta.
export function PainelAtendimento(props) {
  return (
    <>
      <AnfitriaoDeConfirmacao />
      <PainelDaConversa {...props} />
    </>
  )
}

function PainelDaConversa({
  aoAbrirNota, aoAbrirGaleria, aoAbrirBiblioteca,
  focarAoTrocar = false, aoVoltar = null,
  slotEditorNota, editorNotaVisivel = false, notaEmRascunho = false, visivel = true,
}) {
  const {
    selecionada, visiveis, conversas, agora, automaticoPausado, rascunho, escritaDeFora, fonteApi, sincronizacao, sessao,
    aberta, horarioDaLoja, proximaDaFila,
  } = useAtendimento()
  const {
    enviar, reabrir, assumirAtendimento, devolverAutomatico,
    selecionar, abrirEncerramento, reenviar, carregarMensagensAnteriores, transferirConversa, reagir, apagar,
  } = useAcoes()
  const { canais, janelas } = useCatalogo()
  const fotoCadastrada = useFotoCliente(selecionada?.cliente?.fotoUrl)
  const { acoes: capacidades } = useAcessoModulos()
  const reabrirLigado = useAcaoDisponivel()('reabrir')
  const podeReabrir = reabrirLigado && (!fonteApi || capacidades.reabrirConversa === true)
  const cabecalhoRef = useRef(null)
  const compositorPublicoRef = useRef(null)
  // #3363 (S3): o envio pode terminar depois de a atendente trocar de conversa (assumir leva um tempo); o avanço lê a conversa
  // aberta e a fila de AGORA, não as do render em que ela apertou a tecla.
  const aoVivoRef = useRef(null)
  useEffect(() => { aoVivoRef.current = { id: selecionada?.id, proximaDaFila } })
  const contextoDaNota = useRef({ id: selecionada?.id, visivel: editorNotaVisivel })
  // #2185 (F22): "Reabrindo…" e "Abrindo…" são da conversa que pediu; o Painel não remonta por conversa, e
  // o botão da outra nascia desabilitado até a primeira terminar.
  const reabrindoIds = useRef(new Set())
  const [reabrindoDe, setReabrindoDe] = useState(() => new Set())
  // #1761: "Modelos aprovados" abre do compositor e da bolha recusada fora da janela de 24 h.
  // Guardado pelo id da conversa: trocar de conversa fecha.
  const [modelosDe, setModelosDe] = useState(null)
  // Modal aberto pelo menu ⋮, guardado pelo id da conversa: trocar de conversa fecha.
  const [modalDoMenu, setModalDoMenu] = useState(null)
  // #3234 (C5): no desktop o modo vira selo no cabeçalho; a barra muda de lugar, o Assumir em andamento fica aqui.
  const desktop = useTamanhoTela() === DESKTOP
  const assumirSemRepetir = useAssumirSemRepetir()

  // #2005 (console-09): só o modo API tem a ação; na demonstração o fio já é inteiro.
  const anterioresIds = useRef(new Set())
  const [anterioresDe, setAnterioresDe] = useState(() => new Set())
  async function aoVerAnteriores() {
    if (!selecionada || anterioresIds.current.has(selecionada.id)) return
    const id = selecionada.id
    anterioresIds.current.add(id)
    setAnterioresDe(new Set(anterioresIds.current))
    try { await carregarMensagensAnteriores(id) }
    finally { anterioresIds.current.delete(id); setAnterioresDe(new Set(anterioresIds.current)) }
  }

  async function aoReabrir() {
    if (!selecionada || reabrindoIds.current.has(selecionada.id)) return
    const id = selecionada.id
    reabrindoIds.current.add(id)
    setReabrindoDe(new Set(reabrindoIds.current))
    try { await reabrir(id) }
    finally { reabrindoIds.current.delete(id); setReabrindoDe(new Set(reabrindoIds.current)) }
  }

  useEffect(() => {
    if (focarAoTrocar && visivel) cabecalhoRef.current?.focus()
  }, [focarAoTrocar, selecionada?.id, visivel])

  // #3317 (F06): o Assumir some da barra quando a conversa passa a ser dela, e o foco ia junto, para o body: a
  // digitação seguinte se perdia. No desktop o foco volta ao campo de mensagem, depois que a tela já mudou. Só
  // volta se o foco se perdeu (body, barra ou a reserva do título); no toque o teclado não abre sem ela pedir.
  const [pedidosDeFoco, setPedidosDeFoco] = useState(0)
  const idAtual = useRef(selecionada?.id)
  useEffect(() => { idAtual.current = selecionada?.id }, [selecionada?.id])
  useEffect(() => {
    // Tablet e qualquer toque ficam de fora, como no #2135: `focarAoTrocar` só vale no celular por largura.
    if (pedidosDeFoco === 0 || focarAoTrocar || !window.matchMedia?.('(pointer: fine)').matches) return
    const atual = document.activeElement
    const perdido = !atual || atual === document.body || atual === document.documentElement
      || atual.matches('[data-foco-reserva]') || atual.closest('[data-controle-conversa]')
    if (perdido) compositorPublicoRef.current?.querySelector('textarea:not(:disabled)')?.focus({ preventScroll: true })
  }, [pedidosDeFoco, focarAoTrocar])
  async function aoAssumirDaBarra() {
    const id = selecionada.id
    const resultado = await assumirAtendimento(id)
    // Trocou de conversa durante o Assumir: o campo que está na tela já não é o dela.
    if (resultado !== false && idAtual.current === id) setPedidosDeFoco((n) => n + 1)
    return resultado
  }

  useEffect(() => {
    const anterior = contextoDaNota.current
    contextoDaNota.current = { id: selecionada?.id, visivel: editorNotaVisivel }
    if (!anterior.visivel || editorNotaVisivel || !visivel || !focarAoTrocar || anterior.id !== selecionada?.id) return
    // O wrapper público já foi desocultado neste commit. Não remonta nem altera o rascunho.
    const focoAtual = document.activeElement
    if (focoAtual && focoAtual !== document.body && focoAtual !== document.documentElement && focoAtual.getClientRects().length) return
    const campo = compositorPublicoRef.current?.querySelector('textarea:not(:disabled)')
    if (!campo?.getClientRects().length) {
      const ferramenta = [...(compositorPublicoRef.current?.querySelectorAll('button:not(:disabled)') ?? [])]
        .find(no => no.getClientRects().length > 0)
      ferramenta?.focus({ preventScroll: true })
      return
    }
    const { selectionStart: inicio, selectionEnd: fim, selectionDirection: direcao, scrollTop: rolagem } = campo
    campo.focus({ preventScroll: true })
    campo.setSelectionRange(inicio, fim, direcao)
    campo.scrollTop = rolagem
  }, [editorNotaVisivel, visivel, focarAoTrocar, selecionada?.id])

  if (!selecionada) {
    const primeira = visiveis[0]
    // #2258 (auditoria T2): no modo API a lista começa vazia até a primeira leitura. Nesse intervalo o
    // painel mostra o esqueleto (ou o erro), nunca "Nenhuma conversa para exibir".
    const carga = cargaDaLista({ fonteApi, estado: sincronizacao?.estado, total: conversas?.length ?? 0 })
    if (carga === 'carregando') {
      return (
        <div className={css.esqueletoConversa} role="status" aria-busy="true" aria-label="Carregando a conversa">
          <span className={`${css.esqueleto} ${css.esqueletoCabecalho}`} aria-hidden="true" />
          <span className={`${css.esqueleto} ${css.esqueletoBalao}`} aria-hidden="true" />
          <span className={`${css.esqueleto} ${css.esqueletoBalao} ${css.esqueletoBalaoSaida}`} aria-hidden="true" />
          <span className={`${css.esqueleto} ${css.esqueletoBalao}`} aria-hidden="true" />
        </div>
      )
    }
    if (carga === 'erro') {
      return <Vazio centro icone="conversa" titulo="Não consegui carregar as conversas">Tento de novo em instantes.</Vazio>
    }
    return (
      <Vazio
        centro icone="conversa"
        titulo={primeira ? 'Escolha uma conversa' : 'Nenhuma conversa para exibir'}
        acao={primeira && (
          <Botao variante="primario" onClick={() => selecionar(primeira.id)}>
            Abrir {apresentacaoDoNome(nomeDaConversa(primeira), { comanda: primeira.numeroComanda }).rotulo}
          </Botao>
        )}
      >
        {primeira
          ? 'A conversa que você escolher na lista abre aqui.'
          : 'Altere a busca ou os filtros para encontrar uma conversa.'}
      </Vazio>
    )
  }

  const reabrindo = reabrindoDe.has(selecionada.id)
  const abrindoAnteriores = anterioresDe.has(selecionada.id)
  const canal = canalDaConversa(canais, selecionada)
  // #2833: reagir e apagar só existem no WhatsApp; em outro canal a API responde 409 e o menu não oferece.
  const noWhatsApp = selecionada.canal === 'WhatsApp'
  // #2258 (T4): o mesmo nome do cartão, do cabeçalho, da ficha e dos botões "Abrir".
  const apresentacao = apresentacaoDoNome(nomeDaConversa(selecionada), { comanda: selecionada.numeroComanda })
  const { pode, janela, motivo } = permissaoDeEscrita(selecionada, agora, canal)
  // Valor CRU, não booleano: a marca PAUSA_POR_ASSUMIR é o que separa "pausado
  // porque você escreveu" de "pausado porque você assumiu". O Boolean() de
  // antes apagava a diferença e a barra dizia a frase errada.
  const pausado = automaticoPausado[selecionada.id] ?? false
  // #3318: a barra diz o mesmo motivo do cartão da lista, com os mesmos dados (CartaoConversa).
  const motivoDaFila = motivoDePrecisar(selecionada, agora, pausado, aberta, janelas, horarioDaLoja)
  const encerrada = conversaEncerrada(selecionada)
  // Uma conta só na tela: quem diz que a conversa está bloqueada é a mesma
  // permissao de escrita que decide o composer e o reducer.
  const bloqueada = motivo?.acao === MOTIVO_BLOQUEIO
  const acoesTela = acoesDaConversa({ conversa: selecionada, pausado, bloqueada, canal, permitePassar: Boolean(transferirConversa) })
  const abrirModal = (tipo) => setModalDoMenu({ tipo, conversaId: selecionada.id })
  const fecharModal = () => setModalDoMenu(null)
  const modalAberto = modalDoMenu?.conversaId === selecionada.id ? modalDoMenu.tipo : null
  // Rodada 11 (registro 92): resposta automática na fila e automático ainda
  // conduzindo. Assumir apaga o "digitando" no mesmo toque.
  const digitando = Boolean(selecionada.respostaPendente?.length) && !pausado && !bloqueada
  // #1551: modelo aprovado só existe com a API; na demonstração o botão só devolvia erro.
  // #3206: lead sem cadastro também envia; o servidor decide.
  const podeEnviarModelo = regraPodeEnviarModelo({ fonteApi, podeReabrir, aceitaModelo: canal.aceitaModelo, encerrada, bloqueada })
  const prazo = prazoNoCabecalho(janela, motivo)
  // #2134: quem responde e o Assumir ficam fora da área que rola. No modo nota a barra sai de cena.
  // Com o Assumir a faixa fica: na coluna de 750 px (1440 com o cliente aberto) selo e botão empurravam a linha
  // da janela para baixo. Sem ação nem frase, a faixa era só a pílula: essa sobe.
  const motivoDaBarraAtual = motivoDaBarra(motivoDaFila, motivo, pausado)
  const esperaDoCabecalho = esperaNoCabecalho(esperaAtrasada(selecionada, agora, pausado, horarioDaLoja)?.espera, motivoDaBarraAtual)
  // #3374 (C0): o selo no cabeçalho fica desligado até a C8. Ligado, o cabeçalho passou de 64 para 77 px em 1280
  // e a comanda cobriu o "Total dos itens" (prova-1738), a quebra medida na crítica de 23/11.
  const cardapioNoCabecalho = desktop && Boolean(acoesTela.enviarCardapio)
  const modoNoCabecalho = SELO_NO_CABECALHO && desktop && !acoesTela.assumir && apresentacaoDoModo(selecionada, pausado, bloqueada, { podeReabrir, agora, motivo: motivoDaBarraAtual }).noCabecalho
  const barraModo = (
    <div hidden={editorNotaVisivel} style={editorNotaVisivel ? undefined : { display: 'contents' }}>
      <BarraModo
        conversa={selecionada}
        pausado={pausado}
        bloqueada={bloqueada}
        podeReabrir={podeReabrir}
        agora={agora}
        motivo={motivoDaBarraAtual}
        acoes={acoesTela}
        aoAssumir={aoAssumirDaBarra}
        aoDevolver={() => abrirModal('devolver')}
        assumirSemRepetir={assumirSemRepetir}
        noCabecalho={modoNoCabecalho}
      />
    </div>
  )

  return (
    <>
      <header className={css.cabecalho} data-cabecalho-conversa>
        {/* #3282: no celular a conversa é uma tela inteira; a seta leva de volta à lista. */}
        {aoVoltar && <BotaoIcone icone="arrow-left" rotulo="Voltar para as conversas" data-voltar-conversas onClick={aoVoltar} />}
        <span className={css.identidade}>
          <Avatar
            nome={selecionada.nome}
            foto={fotoCadastrada.url ?? fotoDoCliente(canal, selecionada.cliente)}
            tamanho="grande"
          />
          <h2 tabIndex={-1} ref={cabecalhoRef} data-foco-reserva>
            <span className={css.nomeCliente}>
              <NomeDoContato completo={apresentacao.completo} curto={apresentacao.curto} capsula={apresentacao.capsula} titulo={apresentacao.titulo} />
            </span>
            {/* Comanda, canal e, com a janela aberta, o prazo (e a espera atrasada, se a barra não a diz), em uma linha. #1761 (L4): a quantidade de
                pedidos saiu (está em Histórico). #3234 (C2): fechada, quem diz é a faixa junto do campo. */}
            <small>
              {selecionada.numeroComanda && apresentacao.capsula?.texto !== `#${selecionada.numeroComanda}` && <>{rotuloDoPedido(selecionada.numeroComanda)} · </>}
              <Icone nome={canal.icone} className={css['canal_' + canal.icone]} /> {canal.nome}
              {/* #3384 (revisão): o atraso fica à vista em qualquer largura; só a janela folgada some abaixo de 1280. */}
              {prazo && <span className={prazoFolgado(janela) ? css.prazoFolgado : undefined}>{' · '}<span title={canal.explicacao[0]}>{prazo}</span></span>}
              {/* Espera atrasada: a barra a diz quando é o motivo dela; com outro motivo na barra, é aqui, uma vez só. */}
              {esperaDoCabecalho && <span>{' · '}{esperaDoCabecalho}</span>}
              {/* O foco vai a este título ao trocar de conversa: o leitor de tela ouve a janela que só a faixa mostra. */}
              {!prazo && janela && !janela.textoLivre && <span className="sr"> · {janela.rotulo}</span>}
            </small>
          </h2>
        </span>
        <div className={css.direita}>
          {/* #2437: "Bloqueado" fica só na barra de modo, com o motivo; aqui repetia a palavra (#2185). */}
          {/* O balão planta o gatilho do Assistente aqui. Com lugar próprio, ele não troca de lado com
              o menu conforme a conversa (#2185: a ordem dependia de quem montou por último). */}
          {modoNoCabecalho && barraModo}
          {/* #3381 (A4): no desktop o cardápio sai do ⋮ e fica à vista, em contorno (o laranja é da ação da tela). */}
          {cardapioNoCabecalho && <Botao variante="secundario" className={css.cardapioNoCabecalho} aria-haspopup="dialog" onClick={() => abrirModal('cardapio')}>
            <Icone nome="cardapio" tamanho={20} /><span className={css.rotuloCardapio}>Enviar cardápio</span>
          </Botao>}
          <span className={css.ancoraAssistente} data-ancora-assistente />
          <MenuDeAcoes
            rotulo="Mais opções da conversa"
            itens={[
              acoesTela.enviarCardapio && !cardapioNoCabecalho && { chave: 'cardapio', rotulo: 'Enviar cardápio', icone: 'cardapio', abreJanela: true, aoEscolher: () => abrirModal('cardapio') },
              acoesTela.passar && { chave: 'passar', rotulo: 'Passar conversa', icone: 'user-plus', abreJanela: true, aoEscolher: () => abrirModal('passar') },
              acoesTela.devolver && { chave: 'devolver', rotulo: 'Devolver ao Assistente', icone: 'agente', abreJanela: true, aoEscolher: () => abrirModal('devolver') },
              // #2496: só a API sabe as etapas do robô; na demonstração não há o que checar. Conversa concluída
              // fica sem menu (#2185): a próxima mensagem abre outra, que começa com o robô.
              fonteApi && !encerrada && { chave: 'assistente', rotulo: 'Checar o Assistente', icone: 'settings', abreJanela: true, aoEscolher: () => abrirModal('assistente') },
              acoesTela.concluir && { chave: 'concluir', rotulo: 'Concluir conversa', icone: 'circle-check', abreJanela: true, aoEscolher: () => abrirEncerramento(selecionada.id) },
            ]}
          />
        </div>
      </header>

      {/* #2134: quem responde a conversa e o Assumir ficam fixos entre o cabeçalho e a conversa.
          Dentro da área que rola, o aviso saía da tela (y=-228 px em 1280x720) e o campo seguia
          enviando como pessoa por cima do Assistente. #3234 (C5): no desktop, só com a pílula, sobe para o cabeçalho. */}
      {!modoNoCabecalho && barraModo}

      {/* Uma área rolável só: conversa e painel do agente rolam juntos, e o composer fica
          preso no pé da coluna em qualquer largura. */}
      {/* #3330 (F22): área que rola precisa de foco para quem só usa o teclado, e de nome para o leitor de tela. */}
      <section className={css.rolagemCentro} aria-label={`Conversa com ${selecionada.nome}`} tabIndex={0}>
        <Thread
          mensagens={selecionada.mensagens}
          chaveRolagem={selecionada.id}
          numeroComanda={selecionada.numeroComanda}
          fronteiras={fronteirasDoFio(selecionada.atendimentos)}
          agora={agora}
          aoAbrirDefinicaoAutomatica={(regra) => aoAbrirBiblioteca(itemDaBibliotecaPelaRegra(regra))}
          aoReenviar={reenviar ? (mensagemId) => reenviar(selecionada.id, mensagemId) : undefined}
          aoReagir={reagir && noWhatsApp ? (mensagemId, emoji) => reagir(selecionada.id, mensagemId, emoji) : undefined}
          aoApagar={apagar && noWhatsApp && !encerrada ? (mensagemId) => apagar(selecionada.id, mensagemId) : undefined}
          aoAbrirModelos={podeEnviarModelo ? () => setModelosDe(selecionada.id) : undefined}
          digitando={digitando}
          visivel={visivel}
          notas={selecionada.cliente?.notas ?? []}
          temAnteriores={Boolean(selecionada.temAnteriores) && !selecionada.anterioresEsgotadas}
          abrindoAnteriores={abrindoAnteriores}
          aoVerAnteriores={carregarMensagensAnteriores ? aoVerAnteriores : undefined}
        />
      </section>

      {/* #1510: remonta por conversa; anexo em espera e gravação não passam para a próxima. */}
      <div data-area-envio style={{ flexShrink: 0 }}>
      <div ref={compositorPublicoRef} hidden={editorNotaVisivel} style={editorNotaVisivel ? undefined : { display: 'contents' }}>
      <Composer
        key={selecionada.id}
        conversa={selecionada}
        canal={canal}
        podeEscrever={pode}
        motivo={motivo}
        // #1577: o texto digitado mora no campo; daqui desce o rascunho global e o carimbo de quem
        // escreveu nele de fora (sugestão, assistente, limpar do envio).
        rascunho={rascunho}
        escritaDeFora={escritaDeFora}
        aoEnviar={(texto) => enviar(selecionada.id, texto.trim())}
        // #3363 (S3): Ctrl+Enter envia e abre o topo de "Precisa de você"; sem outra na fila, fica nesta conversa.
        aoAvancar={() => {
          const aoVivo = aoVivoRef.current
          if (aoVivo?.id !== selecionada.id) return
          const proxima = aoVivo.proximaDaFila(selecionada.id)
          if (proxima) { selecionar(proxima); devolverFocoAoCampoDepoisDeFechar() }
        }}
        // #2134: responder com o Assistente atendendo é assumir, pelo mesmo caminho do botão Assumir.
        // #2185 (F12): a mesma conta do caminho do envio (`acoesDaConversa`): Assistente atendendo ou conversa de colega.
        aoAssumirAntes={acoesTela.assumirAntesDeEnviar ? () => assumirAtendimento(selecionada.id) : null}
        podeEnviarModelo={podeEnviarModelo}
        modelosFixos={modelosAVista(janela)}
        modeloAberto={modelosDe === selecionada.id}
        aoAbrirModelos={() => setModelosDe(selecionada.id)}
        aoFecharModelos={() => setModelosDe(null)}
        aoAbrirNota={aoAbrirNota}
        notaEmRascunho={notaEmRascunho}
        visivel={visivel && !editorNotaVisivel}
        aoAbrirGaleria={aoAbrirGaleria}
        aoAbrirBiblioteca={aoAbrirBiblioteca}
        aoReabrir={podeReabrir ? aoReabrir : null}
        reabrindo={reabrindo}
      />
      </div>
      <div className={editorNotaVisivel ? css.modoNota : undefined} style={editorNotaVisivel ? undefined : { display: 'contents' }}>
        {editorNotaVisivel && <p className={css.avisoNota}><Icone nome="lock" tamanho={16} /> Só a equipe vê</p>}
        {slotEditorNota}
      </div>
      </div>

      {modalAberto === 'cardapio' && <ModalCompartilharComanda conversa={selecionada} aoFechar={fecharModal} />}
      {modalAberto === 'passar' && <ModalPassarConversa conversa={selecionada} aoFechar={fecharModal} />}
      {modalAberto === 'devolver' && <ModalDevolverAssistente conversa={selecionada} aoFechar={fecharModal} aoConfirmar={() => devolverAutomatico(selecionada.id)} />}
      {modalAberto === 'assistente' && <ModalAssistente key={selecionada.id} conversa={selecionada} aoFechar={fecharModal} escopoMovel={`assistente:${sessao?.empresa?.id ?? 'demo'}:${sessao?.usuario?.id ?? 'demo'}`} />}
    </>
  )
}
