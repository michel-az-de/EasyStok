import { useEffect, useRef, useState } from 'react'
import { Avatar } from '../../componentes/Avatar'
import { useFotoCliente } from '../../aplicacao/useFotoCliente'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useAcaoDisponivel } from '../../aplicacao/useAcaoDisponivel'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { fecharMenu, useMenuDetalhes } from '../../hooks/useMenuDetalhes'
import { conversaEncerrada, fronteirasDoFio } from '../../dominio/conversa'
import { MOTIVO_BLOQUEIO, modelosAVista, permissaoDeEscrita, prazoNoCabecalho } from '../../dominio/janela'
import { canalDaConversa, fotoDoCliente } from '../../dominio/canal'
import { apresentacaoDoNome, nomeDaConversa } from '../../dominio/nomeDoContato'
import { cargaDaLista } from '../../dominio/cargaDaLista'
import { NomeDoContato } from '../../componentes/NomeDoContato'
import { assistenteAtendendo } from '../../dominio/resumoDoModo'
import { itemDaBibliotecaPelaRegra } from '../../dominio/respostas'
import { BarraModo } from './BarraModo'
import { Composer } from './Composer'
import { CompartilharComanda } from './CompartilharComanda'
import { DevolverAoAssistente } from './DevolverAoAssistente'
import { PassarConversa } from './PassarConversa'
import { Thread } from './Thread'
import css from './atendimento.module.css'

export function PainelAtendimento({
  aoAbrirNota, aoAbrirGaleria, aoAbrirBiblioteca,
  focarAoTrocar = false,
  slotEditorNota, editorNotaVisivel = false, notaEmRascunho = false, visivel = true,
}) {
  const {
    selecionada, visiveis, conversas, agora, automaticoPausado, rascunho, escritaDeFora, fonteApi, sincronizacao,
  } = useAtendimento()
  const {
    enviar, reabrir, assumirAtendimento, devolverAutomatico,
    selecionar, abrirEncerramento, reenviar, carregarMensagensAnteriores,
  } = useAcoes()
  const { canais } = useCatalogo()
  const fotoCadastrada = useFotoCliente(selecionada?.cliente?.fotoUrl)
  const { acoes: capacidades } = useAcessoModulos()
  const reabrirLigado = useAcaoDisponivel()('reabrir')
  const podeReabrir = reabrirLigado && (!fonteApi || capacidades.reabrirConversa === true)
  const cabecalhoRef = useRef(null)
  // #2230: o ⋮ da conversa fecha com toque fora e Escape.
  const menuConversa = useRef(null)
  useMenuDetalhes(menuConversa)
  const compositorPublicoRef = useRef(null)
  const contextoDaNota = useRef({ id: selecionada?.id, visivel: editorNotaVisivel })
  const reabrindoRef = useRef(false)
  const [reabrindo, setReabrindo] = useState(false)
  // #1761: "Modelos aprovados" abre do compositor e da bolha recusada fora da janela de 24 h.
  // Guardado pelo id da conversa: trocar de conversa fecha.
  const [modelosDe, setModelosDe] = useState(null)

  // #2005 (console-09): só o modo API tem a ação; na demonstração o fio já é inteiro.
  const anterioresEmCurso = useRef(false)
  const [abrindoAnteriores, setAbrindoAnteriores] = useState(false)
  async function aoVerAnteriores() {
    if (anterioresEmCurso.current || !selecionada) return
    anterioresEmCurso.current = true
    setAbrindoAnteriores(true)
    try { await carregarMensagensAnteriores(selecionada.id) }
    finally { anterioresEmCurso.current = false; setAbrindoAnteriores(false) }
  }

  async function aoReabrir() {
    if (reabrindoRef.current || !selecionada) return
    reabrindoRef.current = true
    setReabrindo(true)
    try { await reabrir(selecionada.id) }
    finally { reabrindoRef.current = false; setReabrindo(false) }
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
      return <Vazio titulo="Não consegui carregar as conversas">A próxima tentativa é automática.</Vazio>
    }
    return (
      <Vazio
        titulo={primeira ? 'Escolha uma conversa' : 'Nenhuma conversa para exibir'}
        acao={primeira && (
          <Botao variante="primario" onClick={() => selecionar(primeira.id)}>
            Abrir {apresentacaoDoNome(nomeDaConversa(primeira), { comanda: primeira.numeroComanda }).rotulo}
          </Botao>
        )}
      >
        {primeira
          ? 'Escolha um cliente na lista para ver as mensagens.'
          : 'Altere a busca ou os filtros para encontrar uma conversa.'}
      </Vazio>
    )
  }

  const canal = canalDaConversa(canais, selecionada)
  // #2258 (T4): o mesmo nome do cartão, do cabeçalho, da ficha e dos botões "Abrir".
  const apresentacao = apresentacaoDoNome(nomeDaConversa(selecionada), { comanda: selecionada.numeroComanda })
  const { pode, janela, motivo } = permissaoDeEscrita(selecionada, agora, canal)
  // Valor CRU, não booleano: a marca PAUSA_POR_ASSUMIR é o que separa "pausado
  // porque você escreveu" de "pausado porque você assumiu". O Boolean() de
  // antes apagava a diferença e a barra dizia a frase errada.
  const pausado = automaticoPausado[selecionada.id] ?? false
  const encerrada = conversaEncerrada(selecionada)
  // Uma conta só na tela: quem diz que a conversa está bloqueada é a mesma
  // permissao de escrita que decide o composer e o reducer.
  const bloqueada = motivo?.acao === MOTIVO_BLOQUEIO
  // Rodada 11 (registro 92): resposta automática na fila e automático ainda
  // conduzindo. Assumir apaga o "digitando" no mesmo toque.
  const digitando = Boolean(selecionada.respostaPendente?.length) && !pausado && !bloqueada
  // #1551: modelo aprovado só existe com a API; na demonstração o botão só devolvia erro.
  const podeEnviarModelo = fonteApi && podeReabrir && canal.aceitaModelo && !encerrada && !bloqueada
  const prazo = prazoNoCabecalho(janela)

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
            <span className={css.nomeCliente}>
              <NomeDoContato completo={apresentacao.completo} curto={apresentacao.curto} capsula={apresentacao.capsula} titulo={apresentacao.titulo} />
            </span>
            {/* Comanda, canal e, quando pede atenção, o prazo da janela, em uma linha.
                #1761 (L4): a quantidade de pedidos saiu (está em Histórico) e o prazo só aparece
                perto do fim (`prazoNoCabecalho`); a linha quebrava em duas. */}
            <small>
              {selecionada.numeroComanda && apresentacao.capsula?.texto !== `#${selecionada.numeroComanda}` && <>Comanda {selecionada.numeroComanda} · </>}
              <Icone nome={canal.icone} className={css['canal_' + canal.icone]} /> {canal.nome}
              {prazo && <>{' · '}<span title={canal.explicacao[0]}>{prazo}</span></>}
            </small>
          </h2>
        </span>
        <div className={css.direita} data-ancora-assistente>
          {bloqueada && <Pilula tom="perigo"><Icone nome="bloqueio" /> Bloqueado</Pilula>}
          {!encerrada && !bloqueada && <details key={selecionada.id} ref={menuConversa} className={css.opcoesConversa} onKeyDown={(evento) => {
            if (evento.key !== 'Escape' || evento.defaultPrevented || evento.target.closest('dialog')) return
            evento.preventDefault(); evento.stopPropagation()
            evento.currentTarget.open = false
            evento.currentTarget.querySelector('summary')?.focus()
          }}>
            <summary aria-label="Opções da conversa" title="Opções da conversa"><Icone nome="ellipsis" /></summary>
            <div className={css.menuConversa} role="group" aria-label="Ações da conversa">
          {/* #1989: irmãos com a mesma chave (o id da conversa) faziam o React perder a conta e
              deixar um "Link de compra" velho a cada troca de conversa. Cada um tem o seu prefixo. */}
          <CompartilharComanda key={'compartilhar-' + selecionada.id} conversa={selecionada} canalNome={canal.nome} podeEnviar={pode} />
          {/* Assumir e devolver ao automático moram na BarraModo, um lugar só.
              Aqui em cima fica o que é estado da conversa: bloqueio, encerrar
              e reabrir. */}
          {/* Rodada 5, seção 5 (passo zero): "Encerrar" abre a modal do
              resumo (vazia até a F5 construir o conteúdo); com a conversa já
              encerrada, dá lugar ao "Reabrir conversa" que já existia. */}
          {/* #1239: passar a conversa para outra atendente (só no modo API). */}
          {fonteApi && <PassarConversa key={'passar-' + selecionada.id} conversa={selecionada} />}
          {/* #2232 (A4): Devolver ao Assistente veio da faixa "quem responde" para este menu: um menu só. */}
          <DevolverAoAssistente conversa={selecionada} pausado={pausado} bloqueada={bloqueada} aoDevolver={() => devolverAutomatico(selecionada.id)} />
          <Botao variante="texto" icone="circle-check" className={css.encerrar} onClick={() => { fecharMenu(menuConversa.current); abrirEncerramento(selecionada.id) }}>
            Concluir conversa
          </Botao>
            </div>
          </details>}
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
        aoAssumir={() => assumirAtendimento(selecionada.id)}
      />
      </div>

      {/* Uma área rolável só: conversa e painel do agente rolam juntos, e o composer fica
          preso no pé da coluna em qualquer largura. */}
      <div className={css.rolagemCentro}>
        <Thread
          mensagens={selecionada.mensagens}
          chaveRolagem={selecionada.id}
          visivel={visivel}
          numeroComanda={selecionada.numeroComanda}
          fronteiras={fronteirasDoFio(selecionada.atendimentos)}
          agora={agora}
          aoAbrirDefinicaoAutomatica={(regra) => aoAbrirBiblioteca(itemDaBibliotecaPelaRegra(regra))}
          aoReenviar={reenviar ? (mensagemId) => reenviar(selecionada.id, mensagemId) : undefined}
          aoAbrirModelos={podeEnviarModelo ? () => setModelosDe(selecionada.id) : undefined}
          digitando={digitando}
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
        // #1577: o texto digitado mora no campo; daqui desce o rascunho global e o carimbo de quem
        // escreveu nele de fora (sugestão, assistente, limpar do envio).
        rascunho={rascunho}
        escritaDeFora={escritaDeFora}
        aoEnviar={(texto) => enviar(selecionada.id, texto.trim())}
        // #2134: responder com o Assistente atendendo é assumir, pelo mesmo caminho do botão Assumir.
        aoAssumirAntes={assistenteAtendendo(selecionada, pausado, bloqueada) ? () => assumirAtendimento(selecionada.id) : null}
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
      {/* #2232 (A8): o wrapper existe sempre, para o editor não remontar ao entrar no modo nota (o rascunho
          mora nele); só ganha o fundo de nota e o aviso "Só a equipe vê" enquanto o editor está à vista. */}
      <div className={editorNotaVisivel ? css.modoNota : undefined} style={editorNotaVisivel ? undefined : { display: 'contents' }}>
        {editorNotaVisivel && <p className={css.avisoNota}><Icone nome="lock" tamanho={16} /> Só a equipe vê</p>}
        {slotEditorNota}
      </div>
      </div>
    </>
  )
}
