import { useEffect, useId, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { MenuDeAcoes } from '../../componentes/MenuDeAcoes'
import { Tooltip } from '../../componentes/Tooltip'
import { useAviso } from '../../componentes/Aviso'
import { CampoArea } from '../../componentes/Campo'
import { SugestaoDeTexto } from '../../componentes/SugestaoDeTexto'
import { useComplementoDeTexto } from '../../aplicacao/useComplementoDeTexto'
import { useSugestaoProativa } from '../../aplicacao/useSugestaoProativa'
import { useComplementoLigado } from '../../aplicacao/preferenciaDoComplemento'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { useChaveDaSessao } from '../../aplicacao/useChaveDaSessao'
import { chaveDaConfirmacao, descricaoDaConfirmacao, gravarConfirmacao, jaConfirmou } from './confirmacaoDeEnvioAssumindo'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { respostasEditaveis } from '../../dominio/acessoModulos'
import { useAtalhoBarra } from '../../aplicacao/useAtalhoBarra'
import { useRascunhoLocal } from '../../aplicacao/useRascunhoLocal'
import { aceitaFormato, motivoDeAnexo, motivoDeAudio, motivoDeFormato, restricoesDoCanal } from '../../dominio/canal'
import { motivoDoCampoFechado } from '../../dominio/janela'
import { restoDoEnvio } from '../../dominio/rascunho'
import { anexosEmEspera, chaveDoAnexo } from '../../aplicacao/anexosEmEspera'
import {
  mensagemDeArquivoAnexado, mensagemDeAudio, mensagemDeImagemAnexada, tipoDeArquivo, validarArquivo,
} from '../../dominio/anexos'
import { escolherFormatoGravacao } from '../../dominio/audio'
import { useArquivoComoDataUrl } from '../../hooks/useArquivoComoDataUrl'
import { useGravadorAudio } from '../../hooks/useGravadorAudio'
import { EscolhaDeArquivo, GravadorAudio, PreviaAnexo } from './ComposerAnexos'
import { avisoDeVariaveisNaoResolvidas } from '../../dominio/respostas'
import { avisoDeLimiteDoTexto } from '../../dominio/limiteDoTexto'
import { SeletorRespostas } from './SeletorRespostas'
import { SeletorDeEmoji } from './SeletorDeEmoji'
import { BotaoIcone } from '../../componentes/BotaoIcone'
import { inserirNoCursor } from '../../dominio/acoesNaMensagem'
import { ModalProgramarMensagem } from './ModalProgramarMensagem'
import { PainelMensagensPadrao } from './PainelMensagensPadrao'
import { acoesDoComposer } from './apresentacaoDaConversa'
import { devolverFocoAoCartaoAberto } from './focoDaLista'
import { revisaoDoContexto } from '../../dominio/contextoDaSugestao'
import { primeiroNomeDaConversa } from '../../dominio/nomeDoContato'
import css from './atendimento.module.css'

export function Composer({
  conversa, canal, podeEscrever, motivo, rascunho: rascunhoGlobal, escritaDeFora,
  aoEnviar, aoAvancar = null, aoAbrirNota, aoAbrirGaleria, aoAbrirBiblioteca,
  aoReabrir, reabrindo = false, podeEnviarModelo = false, aoAssumirAntes = null,
  modeloAberto = false, aoAbrirModelos, aoFecharModelos, modelosFixos = true,
  notaEmRascunho = false, visivel = true,
}) {
  const { enviarMidia } = useAcoes()
  // #1577: a tecla muda só este campo. O rascunho global recebe o texto na pausa da digitação, ou na hora
  // em blur, envio e saída da tela; `aoMudarRascunho` (resposta pronta, limpar) também vai na hora.
  const { texto: rascunho, lerTexto, digitar, escrever: aoMudarRascunho, descarregar } = useRascunhoLocal({
    conversaId: conversa.id, rascunho: rascunhoGlobal, escritaDeFora,
  })
  // #1587: no modo API só a foto sai (e só no WhatsApp); o anexo é recusado ao escolher.
  const { fonteApi, sessao } = useAtendimento()
  const { acoes: capacidades } = useAcessoModulos()
  const programadasEditaveis = respostasEditaveis({ fonteApi, acoes: capacidades })
  // Seletor rápido (#1441): "/" no campo ou o botão Respostas abrem a lista
  // compacta de respostas prontas; Enter insere no campo.
  const barra = useAtalhoBarra({ rascunho, conversa, aoInserir: aoMudarRascunho })
  // `.composer` tem overflow-y:auto (válvula de segurança de tela baixa,
  // atendimento.module.css) e isso RECORTA um Popover ancorado nele, porque
  // overflow num eixo obriga o outro a deixar de ser `visible`. A correção
  // (prop `portal` do Popover.jsx) mede o retângulo de `campoRef` por dentro
  // do próprio componente agora; aqui só sobra a ref.
  const campoRef = useRef(null)
  const respostasRef = useRef(null)
  const arquivoRef = useRef(null)
  const idListaRespostas = useId()
  const sugestoesNoCampo = visivel && barra.aberto && barra.pelaBarra && barra.itens.length > 0
  function abrirNotaInterna() {
    // Recolhe as respostas sem apagar um rascunho iniciado por "/".
    if (!barra.pelaBarra && barra.fechar() === false) return
    aoAbrirNota?.()
  }

  // Frente Anexos (rodada 7, pedido do dono 24/09/2026 04h12): anexo de
  // arquivo em espera (prévia antes de enviar) e o gravador de áudio.
  // #2185 (F9): a prévia fica guardada por conversa e quem está logado; trocar de conversa, girar o tablet ou
  // um clique errado na lista não descarta a foto escolhida.
  const chaveAnexo = chaveDoAnexo(sessao, conversa.id)
  const [anexo, setAnexoNaTela] = useState(() => anexosEmEspera.ler(chaveAnexo))
  const setAnexo = (novo) => { anexosEmEspera.guardar(chaveAnexo, novo); setAnexoNaTela(novo) }
  const [erroAnexo, setErroAnexo] = useState(null)
  const aviso = useAviso()
  // O Composer remonta por conversa (#1510): o "Desfazer" do anexo não vale na conversa seguinte.
  const avisoDoAnexo = useRef(null)
  useEffect(() => () => aviso.dispensar(avisoDoAnexo.current ?? -1), [aviso])
  // #2833: seletor de emoji. O emoji entra no cursor do campo (a seleção do textarea sobrevive ao foco na busca),
  // e o cursor fica logo depois dele para o próximo.
  const [emojiAberto, setEmojiAberto] = useState(false)
  const textareaDoCampo = () => campoRef.current?.querySelector('textarea')
  function inserirEmoji(emoji) {
    const campo = textareaDoCampo()
    const { texto, cursor } = inserirNoCursor(lerTexto(), campo?.selectionStart, campo?.selectionEnd, emoji)
    aoMudarRascunho(texto)
    requestAnimationFrame(() => textareaDoCampo()?.setSelectionRange(cursor, cursor))
  }
  function fecharEmoji() {
    setEmojiAberto(false)
    requestAnimationFrame(() => textareaDoCampo()?.focus())
  }
  // #1424: modal de programar mensagem, guardada pelo id da conversa (trocar de conversa fecha).
  const [programarEm, setProgramarEm] = useState(null)
  const { ler } = useArquivoComoDataUrl()
  const gravador = useGravadorAudio({ escolherTipo: escolherFormatoGravacao })
  const gravando = gravador.estado !== 'ocioso'

  async function aoEscolherArquivo(arquivo) {
    setErroAnexo(null)
    const { aceito, motivo: motivoRecusa } = validarArquivo({ tipo: arquivo.type, tamanho: arquivo.size, soImagem: fonteApi })
    if (!aceito) { setErroAnexo(motivoRecusa); return }
    const tipo = tipoDeArquivo(arquivo.type)
    const bloqueioCanal = motivoDeAnexo(canal, tipo, { fonteApi })
    if (bloqueioCanal) { setErroAnexo(bloqueioCanal); return }
    try {
      const dataUrl = await ler(arquivo)
      setAnexo({ tipo, nome: arquivo.name, tamanho: arquivo.size, dataUrl })
    } catch {
      setErroAnexo('Não consegui ler o arquivo. Escolha novamente ou tente outro arquivo.')
    }
  }

  // #2134: responder com o Assistente atendendo é assumir. `aoAssumirAntes` só vem preenchido nesse
  // caso e assume pelo mesmo caminho do botão Assumir. Se falhar, nada sai e o erro fica aqui,
  // junto do campo (o aviso da barra de modo pode estar fora da tela).
  const assumindoRef = useRef(false)
  // #3339 (R1D-09): o Enviar vira Gravar no mesmo lugar quando o campo esvazia; o 2º clique de um duplo clique
  // cairia nele e pediria o microfone. Gravar logo depois de um envio não começa a gravação.
  const ultimoEnvioEm = useRef(0)
  const [assumindo, setAssumindo] = useState(false)
  const [assumiu, setAssumiu] = useState(false)
  const [erroAssumir, setErroAssumir] = useState(null)
  // #3282: "Enviar" que assume pergunta uma vez por sessão; o Enter com o diálogo aberto não envia.
  const chaveConfirmacao = useChaveDaSessao(chaveDaConfirmacao)
  const [confirmandoEnvio, setConfirmandoEnvio] = useState(false)
  // #3363 (S3): o Ctrl+Enter que abre o modal quer, no Enter que confirma, abrir também a próxima da fila. A intenção mora
  // aqui até a confirmação usá-la; cancelar ou fechar o modal a apaga, e o Enter seguinte só envia.
  const [avancarAoConfirmar, setAvancarAoConfirmar] = useState(false)
  const enviaAssumindo = Boolean(aoAssumirAntes) && !assumiu
  async function assumirSePreciso() {
    if (!aoAssumirAntes) return true
    assumindoRef.current = true
    setAssumindo(true)
    setErroAssumir(null)
    let assumida = false
    try { assumida = (await aoAssumirAntes()) !== false } catch { assumida = false } finally {
      assumindoRef.current = false
      setAssumindo(false)
    }
    if (assumida) setAssumiu(true)
    else setErroAssumir('Não consegui assumir a conversa, então a mensagem não saiu. Tente enviar de novo.')
    return assumida
  }

  function fecharConfirmacaoDeEnvio() {
    setConfirmandoEnvio(false)
    setAvancarAoConfirmar(false)
  }

  function confirmarEnvioAssumindo() {
    gravarConfirmacao(chaveConfirmacao)
    const avancar = avancarAoConfirmar
    fecharConfirmacaoDeEnvio()
    enviarComposer(true).then((aceito) => { if (aceito && avancar) aoAvancar?.() })
  }

  // #3363 (S3): Ctrl+Enter é o Enter que, passando as guardas e assumindo, abre a próxima da fila. Só avança com o envio
  // aceito (`true`); falha do servidor depois disso segue no balão e não reabre a conversa.
  async function enviarEAvancar() {
    if (await enviarComposer(false, true)) aoAvancar?.()
  }

  // "Enviar" com anexo em espera manda o anexo (o texto digitado vira legenda
  // de imagem, se houver); sem anexo, segue o fluxo de sempre. Um só botão,
  // uma só tecla Enter, sem o composer perguntar qual dos dois você quis.
  // Devolve `true` quando o envio foi aceito (passou as guardas, assumiu e entregou o texto ou a mídia), `false` nos outros.
  async function enviarComposer(jaPerguntou = false, avancar = false) {
    // #1474: {pedido} sem valor sairia literal para o cliente (texto ou legenda); o aviso está
    // acima do campo e o Enviar fica desligado até ela trocar.
    if (variavelSobrando || excedeLimite || assumindoRef.current) return false
    if (confirmandoEnvio && !jaPerguntou) return false
    // Enter com o campo vazio não manda balão vazio (#1287).
    if (!anexo && !rascunho.trim()) return false
    if (!jaPerguntou && enviaAssumindo && !jaConfirmou(chaveConfirmacao)) { setAvancarAoConfirmar(avancar); setConfirmandoEnvio(true); return false }
    // #2185 (F23): o que vai é o que ela mandou no toque; o que digitar enquanto o servidor assume fica no campo.
    const enviado = rascunho
    if (!(await assumirSePreciso())) return false
    if (anexo) {
      // Tirou a prévia enquanto o servidor assumia: a foto não sai.
      if (anexosEmEspera.ler(chaveAnexo) !== anexo) return false
      const comLegenda = anexo.tipo === 'imagem'
      const molde = comLegenda
        ? mensagemDeImagemAnexada({ nomeArquivo: anexo.nome, dataUrl: anexo.dataUrl, legenda: enviado })
        : mensagemDeArquivoAnexado({ nomeArquivo: anexo.nome, tamanho: anexo.tamanho, dataUrl: anexo.dataUrl })
      enviarMidia(conversa.id, molde)
      setAnexo(null)
      // Só a legenda sai com a mídia: arquivo não leva texto, e o que está no campo continua lá.
      if (comLegenda) aoMudarRascunho(restoDoEnvio(lerTexto(), enviado))
      return true
    }
    // O campo é quem tem o texto de agora; o rascunho global pode estar uma pausa atrás.
    descarregar()
    aoEnviar(enviado)
    const agoraNoCampo = lerTexto()
    if (agoraNoCampo !== enviado) aoMudarRascunho(restoDoEnvio(agoraNoCampo, enviado))
    return true
  }

  // #1444 (homologação 07/10: "não sei se ele tá enviando"): o gravador fica no lugar mostrando o
  // envio até o EasyStok responder; o balão na conversa nasce "enviando". Se não sair, o motivo
  // fica no balão e aqui só o aviso curto.
  const [enviandoAudio, setEnviandoAudio] = useState(false)
  // #2185 (F9): sair da conversa com o microfone ligado descarta a gravação; ela é avisada em vez de perder calada.
  const gravandoAgora = useRef(false)
  useEffect(() => { gravandoAgora.current = gravando && !enviandoAudio }, [gravando, enviandoAudio])
  useEffect(() => () => { if (gravandoAgora.current) aviso.mostrar({ texto: 'Gravação descartada: a conversa mudou.' }) }, [aviso])
  const [complementoLigado, alternarComplemento] = useComplementoLigado()
  const sugestao = useComplementoDeTexto({
    texto: rascunho, contexto: 'cliente', conversaId: conversa.id,
    revisaoContexto: revisaoDoContexto(conversa),
    ativo: visivel && fonteApi && podeEscrever && !barra.aberto && !anexo && !gravando && !enviandoAudio && !modeloAberto && !programarEm,
    aoCompletar: aoMudarRascunho,
  })
  // #2185 (E18): com o Assistente respondendo (ou a conversa com um colega) a sugestão proativa gastaria IA
  // numa resposta paralela à que já saiu; ela passa a valer quando a conversa é da atendente.
  const proativa = useSugestaoProativa({
    conversa, ativo: visivel && fonteApi && podeEscrever && !modeloAberto && !programarEm && !aoAssumirAntes,
    campoEmUso: Boolean(rascunho || barra.aberto || anexo || gravando || enviandoAudio || assumindo),
  })
  function usarProposta() {
    const atual = proativa.obterSugestaoAtual()
    if (!atual || rascunho.trim()) return
    aoMudarRascunho(atual.texto)
    proativa.dispensar()
    campoRef.current?.querySelector('textarea')?.focus({ preventScroll: true })
  }
  async function aoEnviarAudio() {
    setErroAnexo(null)
    setEnviandoAudio(true)
    try {
      if (!(await assumirSePreciso())) return
      const resultado = await gravador.finalizar()
      if (!resultado) return
      const saiu = await enviarMidia(conversa.id, mensagemDeAudio(resultado))
      if (saiu === false) setErroAnexo('O áudio não foi enviado. O motivo está no balão da conversa.')
    } catch {
      setErroAnexo('Não consegui enviar o áudio. Grave novamente e tente enviar.')
    } finally {
      setEnviandoAudio(false)
    }
  }

  const temTexto = rascunho.trim().length > 0
  const variavelSobrando = avisoDeVariaveisNaoResolvidas(rascunho)
  // #2285: a API recusa texto acima do teto; o campo avisa antes e o Enviar espera o texto caber.
  const limite = avisoDeLimiteDoTexto(rascunho)
  const excedeLimite = Boolean(limite?.acima)

  // O canal manda no que o composer oferece. Botão que o canal não entrega
  // fica desabilitado com o motivo escrito, em vez de mandar e cair calado.
  // "Anexar" cobre imagem e PDF (dois formatos, `aceitaFormato` por um só);
  // o motivo específico aparece depois de escolher o arquivo (`erroAnexo`),
  // porque só ali dá pra saber qual dos dois ela quis mandar.
  // #2185 (F11): no modo API o áudio só sai pelo WhatsApp; fora dele o botão não promete o que se perde.
  const podeAudio = !motivoDeAudio(canal, { fonteApi })
  const restricao = restricoesDoCanal(canal)
  // #1761: no chat do site "Anexar" e "Fotos" pareciam ativos ao lado do aviso de que o canal não
  // aceita foto nem arquivo. Desligam de fato, e apontam para a linha que diz o motivo.
  const podeFoto = aceitaFormato(canal, 'foto')
  const podeAnexar = podeFoto || aceitaFormato(canal, 'arquivo')
  const idRestricao = useId()
  // #1868: o que o canal não entrega não ocupa a barra (eram 4 ícones apagados no chat do site).
  // O motivo fica escrito na linha da restrição, embaixo do campo, e no title do Enviar.
  const acoes = acoesDoComposer({ podeAnexar, podeFoto, podeAudio })

  // #1761 (L8): conversa encerrada eram três avisos do mesmo estado (botão no cabeçalho, faixa
  // amarela e o campo apagado com Programar e Nota ativos). Fica uma linha neutra no lugar do campo.
  if (motivo?.acao === 'reabrir') {
    return (
      <div className={css.composer} data-area-envio>
        <p className={css.linhaEncerrada}>
          <span>Conversa concluída</span>
          {aoReabrir && <Botao disabled={reabrindo} onClick={aoReabrir}>{reabrindo ? 'Reabrindo…' : 'Reabrir conversa'}</Botao>}
        </p>
      </div>
    )
  }

  return (
    <div className={css.composer} data-area-envio>
      {/* #2420: a explicação fica junto do campo. #3234 (C3): a saída é Respostas, a única entrada da lista
          de mensagens padrão; o campo diz onde ela está. */}
      {motivo && (
        <div className={`${css.bloqueio} ${motivo.acao === 'bloqueio' ? css.bloqueioGrave : ''}`}>
          <Icone nome={motivo.acao === 'bloqueio' ? 'bloqueio' : motivo.acao === 'janela' ? 'lock' : 'alerta'} className={css.bloqueioIcone} />
          <p className={css.bloqueioTexto}>
            <strong>{motivo.titulo}</strong> <span>{motivo.detalhe}</span>
          </p>
        </div>
      )}

      {/* #1761 (L9): fixo acima do campo só perto do fim da janela de 24 h ou com ela fechada. */}
      {/* #1839: perto do fim da janela, quando ainda dá para escrever, o botão à parte fica aqui. */}
      {modelosFixos && podeEnviarModelo && podeEscrever && <Botao className={css.abrirModelos} aria-haspopup="true" onClick={aoAbrirModelos}>
        <Icone nome="modelo" /> Modelos aprovados
      </Botao>}
      {modelosFixos && podeEnviarModelo && podeEscrever && <p className={css.restricao}>Mensagens prontas aprovadas pelo WhatsApp para retomar a conversa fora da janela de resposta.</p>}

      {/* #2134: junto do campo, onde a atendente olha ao enviar. */}
      {assumiu && <p className={css.avisoAssumiu} role="status"><Icone nome="check" /> Você assumiu a conversa</p>}
      {erroAssumir && <p className={css.erroAssumir} role="alert">{erroAssumir}</p>}
      {confirmandoEnvio && (
        <Modal
          titulo="Enviar e assumir esta conversa?"
          descricao={descricaoDaConfirmacao(conversa)}
          aoFechar={fecharConfirmacaoDeEnvio}
          // #3317 (F07): o foco vai ao botão que confirma, então Enter envia. Sem isto ia ao título (#2857) e o
          // Enter não fazia nada; no botão o tooltip "Fechar" do X também não reaparece.
          focarCampo
          rodape={(
            <>
              <Botao onClick={fecharConfirmacaoDeEnvio}>Cancelar</Botao>
              <Botao variante="primario" data-foco-inicial onClick={confirmarEnvioAssumindo}>Enviar e assumir</Botao>
            </>
          )}
        />
      )}

      {/* Gravando substitui o campo de texto e as ações: gravar e escrever ao
          mesmo tempo não é o gesto que o dono pediu ("gravar... cancelar ou
          enviar"), e um estado só por vez é mais fácil de entender. */}
      {gravando || enviandoAudio ? (
        <GravadorAudio gravador={gravador} enviando={enviandoAudio} aoCancelar={gravador.cancelar} aoEnviar={aoEnviarAudio} />
      ) : (
        <>
          {anexo && (
            <PreviaAnexo anexo={anexo} aoRemover={() => {
              // #2146: remover não pergunta; o Aviso devolve o anexo por 8 s (desfazer em vez de "tem certeza?").
              const removido = anexo
              setAnexo(null)
              setErroAnexo(null)
              avisoDoAnexo.current = aviso.mostrar({ texto: `Anexo removido: ${removido.nome}`, acao: { rotulo: 'Desfazer', aoAgir: () => setAnexo(removido) } })
            }} />
          )}

          {erroAnexo && <p className={css.restricao} role="alert">{erroAnexo}</p>}
          {variavelSobrando && <p className={css.restricao} role="alert">{variavelSobrando}</p>}
          {limite && <p className={css.restricao} role={limite.acima ? 'alert' : undefined}>{limite.texto}</p>}
          <SugestaoDeTexto
            sugestao={{ complemento: proativa.sugestao?.texto, aceitar: usarProposta, dispensar: proativa.dispensar }}
            rotuloAcao="Usar no rascunho" atalho={null} compacta
          />

          {/* Rodada 6d: composer em uma linha, como no Mensagens. Ícones à
              esquerda, o campo numa pílula que cresce com o texto, o enviar
              (ou o microfone, com o campo vazio) redondo à direita. A .caixa
              só agrupa; a .acoes some do layout no CSS. */}
          <div className={`${css.caixa} ${acoes.enxuta ? css.caixaEnxuta : ''}`} data-antes-de-assumir={enviaAssumindo || undefined}>
            <div ref={campoRef}>
              <CampoArea
                rotulo="Mensagem para o cliente"
                rotuloOculto
                value={rascunho}
                disabled={!podeEscrever}
                aria-autocomplete={barra.pelaBarra ? 'list' : undefined}
                aria-controls={sugestoesNoCampo ? idListaRespostas : undefined}
                aria-activedescendant={sugestoesNoCampo && barra.itens[barra.destaque] ? `${idListaRespostas}-${barra.destaque}` : undefined}
                placeholder={!podeEscrever
                  // #3234 (C2): com saída por mensagem padrão o campo só aponta o caminho; sem saída (#3339, R1D-14) ele diz o motivo da faixa, e não só "indisponível".
                  ? (podeEnviarModelo ? 'Escolha uma mensagem padrão em Respostas' : motivoDoCampoFechado(motivo))
                  : anexo
                    ? 'Legenda (opcional)'
                    // O atalho / continua valendo, mas a dica não o repete: o botão
                    // Respostas, com rótulo, já mostra o caminho, e a frase longa
                    // quebrava em três linhas no campo (rodada 10, integração).
                    : `Escreva para ${primeiroNomeDaConversa(conversa)}`}
                onChange={(e) => { digitar(e.target.value); setAssumiu(false); setErroAssumir(null) }}
                onFocus={sugestao.aoFocar}
                onBlur={() => { sugestao.aoSair(); descarregar() }}
                onKeyDown={(e) => {
                  if (e.isComposing || e.nativeEvent?.isComposing || e.keyCode === 229 || e.altKey) return
                  // #3363 (S3): Ctrl ou Meta com Enter (sem Shift) envia e avança; com a barra de respostas aberta ela tem a
                  // precedência e a tecla não faz nada. As outras combinações com Ctrl ou Meta seguem fora do campo.
                  if (e.ctrlKey || e.metaKey) {
                    if (e.key === 'Enter' && !e.shiftKey && !barra.aberto) { e.preventDefault(); enviarEAvancar() }
                    return
                  }
                  if (barra.aoTeclar(e)) return
                  if (sugestao.aoTeclar(e)) return
                  // #3363 (S2): barra, menu e sugestão já fecharam o que estava aberto e saíram acima; sobrando o Esc, ele leva à lista.
                  if (e.key === 'Escape' && !e.shiftKey) { if (devolverFocoAoCartaoAberto()) e.preventDefault(); return }
                  if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); enviarComposer() }
                }}
              />
              <SugestaoDeTexto sugestao={sugestao} />
              {/* `portal`: mede o retângulo deste wrapper (via parentElement
                  do próprio Popover) e planta o conteúdo em document.body,
                  fora do recorte de `.composer`. Visual idêntico ao de antes,
                  só que agora fora do overflow. */}
              {visivel && barra.aberto && <SeletorRespostas seletor={barra} aoInserido={() => campoRef.current?.querySelector('textarea')?.focus()} idLista={idListaRespostas} aoGerenciar={() => {
                if (!barra.pelaBarra) respostasRef.current?.focus()
                aoAbrirBiblioteca?.()
              }} />}
              {visivel && modeloAberto && <PainelMensagensPadrao conversa={conversa} aoFechar={aoFecharModelos} janelaFechada={!podeEscrever} />}
            </div>

            <div className={css.acoes}>
              {/* #3386 (r9): a ordem do DOM é a ordem visual (Emoji, Mais, Respostas, Nota, Enviar); o CSS .acoes não reordena mais. */}
              {/* #2833: emoji no cursor, com busca por :nome. */}
              <span className={css.ancoraEmoji}>
                {/* Só o ícone, como no WhatsApp: com nome visível, a fileira não cabia em 320 px (prova-2134). */}
                <BotaoIcone
                  rotulo="Emoji" dica="Emoji no texto (busca por :nome)" icone="smile"
                  aria-haspopup="dialog" aria-expanded={emojiAberto}
                  disabled={!podeEscrever} onClick={() => (emojiAberto ? fecharEmoji() : setEmojiAberto(true))}
                />
                {emojiAberto && <SeletorDeEmoji aoEscolher={inserirEmoji} aoFechar={fecharEmoji} />}
              </span>
              {/* Rodada 12 (issue #16, dúvida da Thatiane "esse botão é pra
                  quê?"): os quatro vizinhos ganham nome visível, como o
                  Respostas. Em coluna estreita o nome some por container
                  query e fica o title com a explicação inteira. */}
              <EscolhaDeArquivo ref={arquivoRef} soImagem={fonteApi} aoEscolher={aoEscolherArquivo} />
              <MenuDeAcoes
                rotulo="Mais ferramentas" icone="mais" alinhamento="esquerda"
                itens={[
                  acoes.anexar && {
                    chave: 'anexar', rotulo: 'Anexar', icone: 'anexo', desabilitado: !podeEscrever || !podeAnexar,
                    motivo: !podeAnexar ? restricao : null,
                    detalhe: fonteApi ? 'Foto do computador (imagem, até 6 MB)' : 'Foto, cardápio em PDF ou comprovante (imagem ou PDF, até 8 MB)',
                    aoEscolher: () => arquivoRef.current?.click(),
                  },
                  acoes.fotos && {
                    chave: 'fotos', rotulo: 'Fotos', icone: 'imagem', desabilitado: !podeEscrever || !podeFoto,
                    motivo: motivoDeFormato(canal, 'foto'), aoEscolher: aoAbrirGaleria,
                  },
                  // #1424: a mensagem programada vai para o cliente cadastrado (a API exige o
                  // clienteId). Não depende do `podeEscrever`: fora da janela sai por modelo.
                  // #2185: quem não edita só consulta as programadas, e bloqueio impede novas.
                  {
                    chave: 'programar', rotulo: programadasEditaveis ? 'Programar' : 'Programadas', icone: 'relogio', abreJanela: true,
                    desabilitado: !conversa.clienteId || (programadasEditaveis && Boolean(conversa.bloqueio)),
                    motivo: !conversa.clienteId ? 'Cadastre o cliente primeiro.'
                      : programadasEditaveis && conversa.bloqueio ? 'Cliente bloqueado.' : null,
                    aoEscolher: () => setProgramarEm(conversa.id),
                  },
                  // #2134: chave do completar com Tab. Só existe no modo API (é lá que a sugestão chama
                  // o provedor). Desligada, nenhuma chamada sai; vale por usuário, neste navegador.
                  fonteApi && {
                    chave: 'sugestoes', rotulo: 'Sugestões do Assistente', icone: 'agente', marcado: complementoLigado,
                    aoEscolher: alternarComplemento,
                  },
                ]}
              />
              {/* Rodada 10, pedido literal do dono (25/09 23h42): "não vi pra
                  enviar uma lista de mensagens do sistema". O ícone sozinho
                  (rótulo escondido, decisão 60) não foi achado por ele. Só
                  este botão ganha rótulo visível, mesma ideia de "Automáticas"
                  no trilho (ícone e palavra juntos); os vizinhos (Anexar,
                  Fotos, Cardápio, Nota, Enviar) continuam ícone só. */}
              <Tooltip texto={podeEscrever ? 'Respostas prontas (atalho: / no campo)' : 'Janela fechada: só modelos aprovados pelo WhatsApp saem até o cliente responder'}><Botao
                ref={respostasRef}
                disabled={!podeEscrever && !podeEnviarModelo}
                aria-haspopup={podeEscrever ? 'listbox' : 'true'}
                aria-expanded={podeEscrever ? barra.aberto : modeloAberto}
                aria-controls={podeEscrever && barra.aberto && barra.itens.length > 0 ? idListaRespostas : undefined}
                aria-label="Respostas" className={css.botaoRespostas}
                onClick={podeEscrever ? (barra.aberto ? barra.fechar : barra.abrir) : (modeloAberto ? aoFecharModelos : aoAbrirModelos)}
              >
                <Icone nome="respostas" />
                <span>Respostas</span>
              </Botao></Tooltip>
              <Tooltip texto={notaEmRascunho ? 'Retomar a nota desta conversa. Só a equipe vê.' : 'Anotar para a equipe sem sair da conversa'}>
                <Botao
                  aria-label={notaEmRascunho ? 'Retomar nota interna' : 'Nota interna'} className={css.botaoNota}
                  onPointerDown={(evento) => { if (barra.pelaBarra) evento.stopPropagation() }}
                  onClick={abrirNotaInterna}
                >
                  <Icone nome="nota" /><span className={css.rotuloNota}>{notaEmRascunho ? 'Retomar nota' : 'Nota'}</span>
                </Botao>
              </Tooltip>
              {/* Microfone ocupa o lugar do Enviar quando o campo está vazio e
                  não há anexo em espera (mesmo gesto do WhatsApp): campo vazio
                  não tem o que enviar como texto, mas tem o que gravar. */}
              {/* #3392 (r10, H8): o aviso curto do canal só de texto mora na barra, ao lado do Enviar que ele explica: a linha
                  de baixo saía da altura das mensagens. O motivo inteiro segue no title e no leitor de tela. */}
              {restricao && !(podeAnexar || podeAudio) && (
                <p id={idRestricao} className={css.restricaoNaBarra} title={restricao}><span aria-hidden="true">Só texto neste canal</span><span className="sr">{restricao}</span></p>
              )}
              {temTexto || anexo || !acoes.audio ? (
                <Tooltip texto={variavelSobrando ?? (excedeLimite ? limite.texto : temTexto || anexo ? (enviaAssumindo ? 'Enviar e assumir a conversa' : 'Enviar mensagem') : restricao ?? 'Escreva a mensagem para enviar')}
                  atalho={temTexto || anexo ? 'Enter' : undefined}>
                <Botao
                  variante="primario"
                  className={`${css.enviar} ${enviaAssumindo ? css.enviarAssume : ''}`}
                  aria-label={enviaAssumindo ? 'Enviar e assumir a conversa' : 'Enviar mensagem'}
                  icone="enviar"
                  aria-keyshortcuts={temTexto || anexo ? 'Enter' : undefined}
                  disabled={!podeEscrever || assumindo || excedeLimite || Boolean(variavelSobrando) || !(temTexto || anexo)}
                  onClick={() => {
                    ultimoEnvioEm.current = Date.now()
                    // #3317 (F06): ao esvaziar o campo o Enviar dá lugar ao Gravar áudio, e o foco ia junto. No toque o
                    // foco fica onde está: devolvê-lo abriria o teclado sem ela pedir (#2135).
                    if (window.matchMedia?.('(pointer: fine)').matches) textareaDoCampo()?.focus({ preventScroll: true })
                    enviarComposer()
                  }}
                >{enviaAssumindo ? 'Enviar e assumir' : 'Enviar'}</Botao>
                </Tooltip>
              ) : (
                // #2185 (R15): o microfone ocupa o lugar do Enviar com o mesmo tamanho e o mesmo desenho (ícone e
                // palavra), para o botão da direita não mudar de largura quando o campo esvazia.
                // #3355: secundário. Com o campo vazio o próximo passo é escrever; o laranja fica com Assumir ou Enviar.
                <Tooltip texto={motivoDeFormato(canal, 'audio') ?? 'Gravar áudio'}>
                  <Botao
                    variante="secundario"
                    className={`${css.enviar} ${css.gravar}`}
                    aria-label="Gravar áudio"
                    icone="mic"
                    disabled={!podeEscrever || !podeAudio}
                    onClick={() => { if (Date.now() - ultimoEnvioEm.current >= 600) gravador.iniciar() }}
                  >Gravar</Botao>
                </Tooltip>
              )}
            </div>
          </div>

          {/* #1870: em coluna estreita a frase inteira quebrava em 2 linhas. Canal só de texto mostra
              o curto; o motivo inteiro segue no title, no leitor de tela e no aria-describedby. A #2143 tinha
              deixado só o texto de leitor de tela e os botões sumiam sem explicação (#2706). */}
          {restricao && (podeAnexar || podeAudio) && <p id={idRestricao} className={css.restricao}>{restricao}</p>}
        </>
      )}

      {programarEm === conversa.id && (
        <ModalProgramarMensagem
          conversa={conversa}
          canal={canal}
          rascunho={rascunho}
          // Texto programado a partir do rascunho sai do campo, para não ir de novo agora.
          aoAgendar={(texto) => { if (texto && texto.trim() === rascunho.trim()) aoMudarRascunho('') }}
          aoFechar={() => setProgramarEm(null)}
        />
      )}

    </div>
  )
}
