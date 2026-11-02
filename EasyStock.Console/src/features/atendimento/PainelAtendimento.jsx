import { useEffect, useRef, useState } from 'react'
import { Avatar } from '../../componentes/Avatar'
import { useFotoCliente } from '../../aplicacao/useFotoCliente'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { MenuDeAcoes } from '../../componentes/MenuDeAcoes'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useAcaoDisponivel } from '../../aplicacao/useAcaoDisponivel'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { acoesDaConversa } from '../../dominio/acoesDaConversa'
import { conversaEncerrada, fronteirasDoFio } from '../../dominio/conversa'
import { MOTIVO_BLOQUEIO, modelosAVista, permissaoDeEscrita, prazoNoCabecalho } from '../../dominio/janela'
import { canalDaConversa, fotoDoCliente } from '../../dominio/canal'
import { primeiroNome } from '../../dominio/mensagem'
import { faixaDaJanela, preencherFaixa } from '../../dominio/entrega'
import { itemDaBibliotecaPelaRegra } from '../../dominio/respostas'
import { BarraModo } from './BarraModo'
import { Composer } from './Composer'
import { ModalCompartilharComanda } from './CompartilharComanda'
import { ModalDevolverAssistente } from './ModalDevolverAssistente'
import { ModalPassarConversa } from './PassarConversa'
import { Thread } from './Thread'
import css from './atendimento.module.css'

export function PainelAtendimento({
  aoAbrirNota, aoAbrirGaleria, aoAbrirBiblioteca,
  focarAoTrocar = false,
  slotEditorNota, editorNotaVisivel = false, notaEmRascunho = false, visivel = true,
}) {
  const {
    selecionada, visiveis, agora, automaticoPausado, rascunho, escritaDeFora, fonteApi,
  } = useAtendimento()
  const {
    enviar, reabrir, definirRascunho, assumirAtendimento, devolverAutomatico,
    selecionar, abrirEncerramento, reenviar, carregarMensagensAnteriores, transferirConversa,
  } = useAcoes()
  const { janelas, canais } = useCatalogo()
  const fotoCadastrada = useFotoCliente(selecionada?.cliente?.fotoUrl)
  const { acoes: capacidades } = useAcessoModulos()
  const reabrirLigado = useAcaoDisponivel()('reabrir')
  const podeReabrir = reabrirLigado && (!fonteApi || capacidades.reabrirConversa === true)
  const cabecalhoRef = useRef(null)
  const compositorPublicoRef = useRef(null)
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
    return (
      <Vazio
        titulo={primeira ? 'Escolha uma conversa' : 'Nenhuma conversa para exibir'}
        acao={primeira && (
          <Botao variante="primario" onClick={() => selecionar(primeira.id)}>
            Abrir {primeira.nome.split(' ')[0]}
          </Botao>
        )}
      >
        {primeira
          ? 'Escolha um cliente na lista para ver as mensagens.'
          : 'Altere a busca ou os filtros para encontrar uma conversa.'}
      </Vazio>
    )
  }

  const reabrindo = reabrindoDe.has(selecionada.id)
  const abrindoAnteriores = anterioresDe.has(selecionada.id)
  const canal = canalDaConversa(canais, selecionada)
  const { pode, ofereceModelo, janela, motivo } = permissaoDeEscrita(selecionada, agora, canal)
  // Valor CRU, não booleano: a marca PAUSA_POR_ASSUMIR é o que separa "pausado
  // porque você escreveu" de "pausado porque você assumiu". O Boolean() de
  // antes apagava a diferença e a barra dizia a frase errada.
  const pausado = automaticoPausado[selecionada.id] ?? false
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
  const faixa = faixaDaJanela(janelas, selecionada.pedido?.janela)
  // #1551: modelo aprovado só existe com a API; na demonstração o botão só devolvia erro.
  // #2185 (E19): o modelo aprovado vai para um cliente cadastrado (o EasyStok exige o `clienteId`); sem
  // cadastro o botão abria uma janela com tudo desligado.
  const podeEnviarModelo = fonteApi && podeReabrir && canal.aceitaModelo && !encerrada && !bloqueada && Boolean(selecionada.clienteId)
  const prazo = prazoNoCabecalho(janela)

  const aplicarTemplate = (template) => definirRascunho(
    selecionada.id,
    preencherFaixa(template.texto, faixa ?? 'o horário combinado')
      .replace('{nome}', primeiroNome(selecionada.nome)),
  )

  return (
    <>
      <header className={css.cabecalho} data-cabecalho-conversa>
        <span className={css.identidade}>
          <Avatar
            nome={selecionada.nome}
            foto={fotoCadastrada.url ?? fotoDoCliente(canal, selecionada.cliente)}
            tamanho="grande"
          />
          <h2 tabIndex={-1} ref={cabecalhoRef} data-foco-reserva>
            <span className={css.nomeCliente} title={selecionada.nome}>{selecionada.nome}</span>
            {/* Comanda, canal e, quando pede atenção, o prazo da janela, em uma linha.
                #1761 (L4): a quantidade de pedidos saiu (está em Histórico) e o prazo só aparece
                perto do fim (`prazoNoCabecalho`); a linha quebrava em duas. */}
            <small>
              {selecionada.numeroComanda && <>Comanda {selecionada.numeroComanda} · </>}
              <Icone nome={canal.icone} className={css['canal_' + canal.icone]} /> {canal.nome}
              {prazo && <>{' · '}<span title={canal.explicacao[0]}>{prazo}</span></>}
            </small>
          </h2>
        </span>
        <div className={css.direita}>
          {/* O balão planta o gatilho do Assistente aqui. Com lugar próprio, ele não troca de lado com
              o menu conforme a conversa (#2185: a ordem dependia de quem montou por último). */}
          <span className={css.ancoraAssistente} data-ancora-assistente />
          <MenuDeAcoes
            rotulo="Mais opções da conversa"
            itens={[
              acoesTela.enviarCardapio && { chave: 'cardapio', rotulo: 'Enviar cardápio', icone: 'cardapio', abreJanela: true, aoEscolher: () => abrirModal('cardapio') },
              acoesTela.passar && { chave: 'passar', rotulo: 'Passar conversa', icone: 'user-plus', abreJanela: true, aoEscolher: () => abrirModal('passar') },
              acoesTela.devolver && { chave: 'devolver', rotulo: 'Devolver ao Assistente', icone: 'sparkles', abreJanela: true, aoEscolher: () => abrirModal('devolver') },
              acoesTela.concluir && { chave: 'concluir', rotulo: 'Concluir conversa', icone: 'circle-check', abreJanela: true, aoEscolher: () => abrirEncerramento(selecionada.id) },
            ]}
          />
        </div>
      </header>

      {/* #2134: quem responde a conversa e o Assumir ficam fixos entre o cabeçalho e a conversa.
          Dentro da área que rola, o aviso saía da tela (y=-228 px em 1280x720) e o campo seguia
          enviando como pessoa por cima do Assistente. */}
      <div hidden={editorNotaVisivel} style={editorNotaVisivel ? undefined : { display: 'contents' }}>
      <BarraModo
        conversa={selecionada}
        pausado={pausado}
        bloqueada={bloqueada}
        podeReabrir={podeReabrir}
        acoes={acoesTela}
        aoAssumir={() => assumirAtendimento(selecionada.id)}
      />
      </div>

      {/* Uma área rolável só: conversa e painel do agente rolam juntos, e o composer fica
          preso no pé da coluna em qualquer largura. */}
      <div className={css.rolagemCentro}>
        <Thread
          mensagens={selecionada.mensagens}
          chaveRolagem={selecionada.id}
          fronteiras={fronteirasDoFio(selecionada.atendimentos)}
          agora={agora}
          aoAbrirDefinicaoAutomatica={(regra) => aoAbrirBiblioteca(itemDaBibliotecaPelaRegra(regra))}
          aoReenviar={reenviar ? (mensagemId) => reenviar(selecionada.id, mensagemId) : undefined}
          aoAbrirModelos={podeEnviarModelo ? () => setModelosDe(selecionada.id) : undefined}
          digitando={digitando}
          visivel={visivel}
          notas={selecionada.cliente?.notas ?? []}
          temAnteriores={Boolean(selecionada.temAnteriores) && !selecionada.anterioresEsgotadas}
          abrindoAnteriores={abrindoAnteriores}
          aoVerAnteriores={carregarMensagensAnteriores ? aoVerAnteriores : undefined}
        />
      </div>

      {/* #1510: remonta por conversa; anexo em espera e gravação não passam para a próxima. */}
      <div data-area-envio style={{ flexShrink: 0 }}>
      <div ref={compositorPublicoRef} hidden={editorNotaVisivel} style={editorNotaVisivel ? undefined : { display: 'contents' }}>
      <Composer
        key={selecionada.id}
        conversa={selecionada}
        canal={canal}
        podeEscrever={pode}
        motivo={motivo}
        ofereceModelo={ofereceModelo}
        // #1577: o texto digitado mora no campo; daqui desce o rascunho global e o carimbo de quem
        // escreveu nele de fora (sugestão, assistente, limpar do envio).
        rascunho={rascunho}
        escritaDeFora={escritaDeFora}
        aoEnviar={(texto) => enviar(selecionada.id, texto.trim())}
        // #2134: responder com o Assistente atendendo é assumir, pelo mesmo caminho do botão Assumir.
        // #2185 (F12): a mesma conta do caminho do envio (`acoesDaConversa`): Assistente atendendo ou conversa de colega.
        aoAssumirAntes={acoesTela.assumirAntesDeEnviar ? () => assumirAtendimento(selecionada.id) : null}
        podeEnviarModelo={podeEnviarModelo}
        modelosFixos={modelosAVista(janela)}
        modeloAberto={modelosDe === selecionada.id}
        aoAbrirModelos={() => setModelosDe(selecionada.id)}
        aoFecharModelos={() => setModelosDe(null)}
        aoAplicarTemplate={aplicarTemplate}
        aoAbrirNota={aoAbrirNota}
        notaEmRascunho={notaEmRascunho}
        visivel={visivel && !editorNotaVisivel}
        aoAbrirGaleria={aoAbrirGaleria}
        aoAbrirBiblioteca={aoAbrirBiblioteca}
        aoReabrir={podeReabrir ? aoReabrir : null}
        reabrindo={reabrindo}
      />
      </div>
      {slotEditorNota}
      </div>

      {modalAberto === 'cardapio' && <ModalCompartilharComanda conversa={selecionada} aoFechar={fecharModal} />}
      {modalAberto === 'passar' && <ModalPassarConversa conversa={selecionada} aoFechar={fecharModal} />}
      {modalAberto === 'devolver' && <ModalDevolverAssistente conversa={selecionada} aoFechar={fecharModal} aoConfirmar={() => devolverAutomatico(selecionada.id)} />}
    </>
  )
}
