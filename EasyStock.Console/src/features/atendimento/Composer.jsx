import { useId, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { SugestaoDeTexto } from '../../componentes/SugestaoDeTexto'
import { useComplementoDeTexto } from '../../aplicacao/useComplementoDeTexto'
import { useComplementoLigado } from '../../aplicacao/preferenciaDoComplemento'
import { Icone } from '../../componentes/Icone'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { useAtalhoBarra } from '../../aplicacao/useAtalhoBarra'
import { useRascunhoLocal } from '../../aplicacao/useRascunhoLocal'
import { aceitaFormato, motivoDeAnexo, motivoDeFormato, restricoesDoCanal } from '../../dominio/canal'
import {
  mensagemDeArquivoAnexado, mensagemDeAudio, mensagemDeImagemAnexada, tipoDeArquivo, validarArquivo,
} from '../../dominio/anexos'
import { escolherFormatoGravacao } from '../../dominio/audio'
import { useArquivoComoDataUrl } from '../../hooks/useArquivoComoDataUrl'
import { useGravadorAudio } from '../../hooks/useGravadorAudio'
import { BotaoAnexarArquivo, GravadorAudio, PreviaAnexo } from './ComposerAnexos'
import { avisoDeVariaveisNaoResolvidas } from '../../dominio/respostas'
import { SeletorRespostas } from './SeletorRespostas'
import { ModalProgramarMensagem } from './ModalProgramarMensagem'
import { ModalEnviarModelo } from './ModalEnviarModelo'
import { acoesDoComposer } from './apresentacaoDaConversa'
import css from './atendimento.module.css'

export function Composer({
  conversa, canal, podeEscrever, motivo, rascunho: rascunhoGlobal, escritaDeFora,
  aoEnviar, aoAbrirNota, aoAbrirGaleria, aoAbrirBiblioteca,
  aoReabrir, reabrindo = false, podeEnviarModelo = false, aoAssumirAntes = null,
  modeloAberto = false, aoAbrirModelos, aoFecharModelos, modelosFixos = true,
}) {
  const { enviarMidia } = useAcoes()
  // #1577: a tecla muda só este campo. O rascunho global recebe o texto na pausa da digitação, ou na hora
  // em blur, envio e saída da tela; `aoMudarRascunho` (resposta pronta, limpar) também vai na hora.
  const { texto: rascunho, digitar, escrever: aoMudarRascunho, descarregar } = useRascunhoLocal({
    conversaId: conversa.id, rascunho: rascunhoGlobal, escritaDeFora,
  })
  // #1587: no modo API só a foto sai (e só no WhatsApp); o anexo é recusado ao escolher.
  const { fonteApi } = useAtendimento()
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
  const idListaRespostas = useId()
  const sugestoesNoCampo = barra.aberto && barra.pelaBarra && barra.itens.length > 0

  // Frente Anexos (rodada 7, pedido do dono 24/09/2026 04h12): anexo de
  // arquivo em espera (prévia antes de enviar) e o gravador de áudio.
  const [anexo, setAnexo] = useState(null)
  const [erroAnexo, setErroAnexo] = useState(null)
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
  const [assumindo, setAssumindo] = useState(false)
  const [assumiu, setAssumiu] = useState(false)
  const [erroAssumir, setErroAssumir] = useState(null)
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

  // "Enviar" com anexo em espera manda o anexo (o texto digitado vira legenda
  // de imagem, se houver); sem anexo, segue o fluxo de sempre. Um só botão,
  // uma só tecla Enter, sem o composer perguntar qual dos dois você quis.
  async function enviarComposer() {
    // #1474: {pedido} sem valor sairia literal para o cliente (texto ou legenda); o aviso está
    // acima do campo e o Enviar fica desligado até ela trocar.
    if (variavelSobrando || assumindoRef.current) return
    // Enter com o campo vazio não manda balão vazio (#1287).
    if (!anexo && !rascunho.trim()) return
    if (!(await assumirSePreciso())) return
    if (anexo) {
      const molde = anexo.tipo === 'imagem'
        ? mensagemDeImagemAnexada({ nomeArquivo: anexo.nome, dataUrl: anexo.dataUrl, legenda: rascunho })
        : mensagemDeArquivoAnexado({ nomeArquivo: anexo.nome, tamanho: anexo.tamanho, dataUrl: anexo.dataUrl })
      enviarMidia(conversa.id, molde)
      setAnexo(null)
      aoMudarRascunho('')
      return
    }
    // O campo é quem tem o texto de agora; o rascunho global pode estar uma pausa atrás.
    descarregar()
    aoEnviar(rascunho)
  }

  // #1444 (homologação 07/10: "não sei se ele tá enviando"): o gravador fica no lugar mostrando o
  // envio até o EasyStok responder; o balão na conversa nasce "enviando". Se não sair, o motivo
  // fica no balão e aqui só o aviso curto.
  const [enviandoAudio, setEnviandoAudio] = useState(false)
  const [complementoLigado, alternarComplemento] = useComplementoLigado()
  const sugestao = useComplementoDeTexto({
    texto: rascunho, contexto: 'cliente', conversaId: conversa.id,
    ativo: fonteApi && podeEscrever && !barra.aberto && !anexo && !gravando && !enviandoAudio,
    aoCompletar: aoMudarRascunho,
  })
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

  // O canal manda no que o composer oferece. Botão que o canal não entrega
  // fica desabilitado com o motivo escrito, em vez de mandar e cair calado.
  // "Anexar" cobre imagem e PDF (dois formatos, `aceitaFormato` por um só);
  // o motivo específico aparece depois de escolher o arquivo (`erroAnexo`),
  // porque só ali dá pra saber qual dos dois ela quis mandar.
  const podeAudio = aceitaFormato(canal, 'audio')
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
      <div className={css.composer}>
        <p className={css.linhaEncerrada}>
          <span>Conversa encerrada</span>
          {aoReabrir && <Botao disabled={reabrindo} onClick={aoReabrir}>{reabrindo ? 'Reabrindo…' : 'Reabrir conversa'}</Botao>}
        </p>
      </div>
    )
  }

  return (
    <div className={css.composer}>
      {motivo && (
        <p className={`${css.bloqueio} ${motivo.acao === 'bloqueio' ? css.bloqueioGrave : ''}`}>
          <Icone nome={motivo.acao === 'bloqueio' ? 'bloqueio' : 'alerta'} />
          <strong>{motivo.titulo}</strong>
          <span>{motivo.detalhe}</span>
        </p>
      )}

      {/* #1761 (L9): fixo acima do campo só perto do fim da janela de 24 h ou com ela fechada. */}
      {/* #1839: com a janela fechada os modelos moram em Respostas, como no protótipo; o botão à
          parte fica só no aviso de fim de janela, quando ainda dá para escrever. */}
      {modelosFixos && podeEnviarModelo && podeEscrever && <Botao className={css.abrirModelos} aria-haspopup="dialog" onClick={aoAbrirModelos}>
        <Icone nome="modelo" /> Modelos aprovados
      </Botao>}
      {modelosFixos && podeEnviarModelo && podeEscrever && <p className={css.restricao}>Mensagens prontas aprovadas pelo WhatsApp para retomar a conversa fora da janela de resposta.</p>}

      {/* #2134: junto do campo, onde a atendente olha ao enviar. */}
      {assumiu && <p className={css.avisoAssumiu} role="status"><Icone nome="check" /> Você assumiu a conversa</p>}
      {erroAssumir && <p className={css.erroAssumir} role="alert">{erroAssumir}</p>}

      {/* Gravando substitui o campo de texto e as ações: gravar e escrever ao
          mesmo tempo não é o gesto que o dono pediu ("gravar... cancelar ou
          enviar"), e um estado só por vez é mais fácil de entender. */}
      {gravando || enviandoAudio ? (
        <GravadorAudio gravador={gravador} enviando={enviandoAudio} aoCancelar={gravador.cancelar} aoEnviar={aoEnviarAudio} />
      ) : (
        <>
          {anexo && (
            <PreviaAnexo anexo={anexo} aoRemover={() => { setAnexo(null); setErroAnexo(null) }} />
          )}

          {erroAnexo && <p className={css.restricao} role="alert">{erroAnexo}</p>}
          {variavelSobrando && <p className={css.restricao} role="alert">{variavelSobrando}</p>}

          {/* Rodada 6d: composer em uma linha, como no Mensagens. Ícones à
              esquerda, o campo numa pílula que cresce com o texto, o enviar
              (ou o microfone, com o campo vazio) redondo à direita. A .caixa
              só agrupa; a .acoes some do layout no CSS. */}
          <div className={`${css.caixa} ${acoes.enxuta ? css.caixaEnxuta : ''}`}>
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
                  ? (podeEnviarModelo ? 'Janela fechada: escolha um modelo aprovado em Respostas' : 'Mensagem indisponível nesta conversa')
                  : anexo
                    ? 'Legenda (opcional)'
                    // O atalho / continua valendo, mas a dica não o repete: o botão
                    // Respostas, com rótulo, já mostra o caminho, e a frase longa
                    // quebrava em três linhas no campo (rodada 10, integração).
                    : `Escreva para ${conversa.nome.split(' ')[0]}`}
                onChange={(e) => { digitar(e.target.value); setAssumiu(false); setErroAssumir(null) }}
                onFocus={sugestao.aoFocar}
                onBlur={() => { sugestao.aoSair(); descarregar() }}
                onKeyDown={(e) => {
                  if (e.isComposing || e.nativeEvent?.isComposing || e.keyCode === 229 || e.altKey || e.ctrlKey || e.metaKey) return
                  if (barra.aoTeclar(e)) return
                  if (sugestao.aoTeclar(e)) return
                  if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); enviarComposer() }
                }}
              />
              <SugestaoDeTexto sugestao={sugestao} />
              {/* `portal`: mede o retângulo deste wrapper (via parentElement
                  do próprio Popover) e planta o conteúdo em document.body,
                  fora do recorte de `.composer`. Visual idêntico ao de antes,
                  só que agora fora do overflow. */}
              {barra.aberto && <SeletorRespostas seletor={barra} aoInserido={() => campoRef.current?.querySelector('textarea')?.focus()} idLista={idListaRespostas} aoGerenciar={() => {
                if (!barra.pelaBarra) respostasRef.current?.focus()
                aoAbrirBiblioteca?.()
              }} />}
            </div>

            <div className={css.acoes}>
              {/* Rodada 10, pedido literal do dono (25/09 23h42): "não vi pra
                  enviar uma lista de mensagens do sistema". O ícone sozinho
                  (rótulo escondido, decisão 60) não foi achado por ele. Só
                  este botão ganha rótulo visível, mesma ideia de "Automáticas"
                  no trilho (ícone e palavra juntos); os vizinhos (Anexar,
                  Fotos, Cardápio, Nota, Enviar) continuam ícone só. */}
              <Botao
                ref={respostasRef}
                disabled={!podeEscrever && !podeEnviarModelo}
                aria-haspopup={podeEscrever ? 'listbox' : 'dialog'}
                aria-expanded={podeEscrever ? barra.aberto : undefined}
                aria-controls={podeEscrever && barra.aberto && barra.itens.length > 0 ? idListaRespostas : undefined}
                title={podeEscrever ? 'Respostas prontas (atalho: / no campo)' : 'Janela fechada: só modelos aprovados pelo WhatsApp saem até o cliente responder'}
                className={css.botaoRespostas}
                onClick={podeEscrever ? barra.abrir : aoAbrirModelos}
              >
                <Icone nome="respostas" />
                <span>Respostas</span>
              </Botao>
              {/* Rodada 12 (issue #16, dúvida da Thatiane "esse botão é pra
                  quê?"): os quatro vizinhos ganham nome visível, como o
                  Respostas. Em coluna estreita o nome some por container
                  query e fica o title com a explicação inteira. */}
              {acoes.anexar && <BotaoAnexarArquivo
                disabled={!podeEscrever || !podeAnexar}
                descritoPor={!podeAnexar && restricao ? idRestricao : undefined}
                soImagem={fonteApi}
                titulo={!podeAnexar ? restricao : fonteApi ? 'Anexar foto do computador (imagem, até 8 MB)' : 'Anexar arquivo do computador: foto, cardápio em PDF ou comprovante (imagem ou PDF, até 8 MB)'}
                rotulo="Anexar"
                classeRotulo={css.rotuloAcao}
                aoEscolher={aoEscolherArquivo}
              />}
              {acoes.fotos && <Botao
                title={motivoDeFormato(canal, 'foto') ?? 'Fotos dos pratos cadastrados na casa'}
                aria-describedby={!podeFoto && restricao ? idRestricao : undefined}
                className={css.acaoComRotulo} disabled={!podeEscrever || !podeFoto} onClick={aoAbrirGaleria}
              >
                <Icone nome="imagem" /><span className={css.rotuloAcao}>Fotos</span>
              </Botao>}
              {/* #1424: a mensagem programada vai para o cliente cadastrado (a API exige o
                  clienteId). Não depende do `podeEscrever`: fora da janela sai por modelo. */}
              <Botao
                title={conversa.clienteId
                  ? 'Programar mensagem para sair depois, no horário escolhido'
                  : 'Cadastre o cliente primeiro: a mensagem programada vai para um cliente cadastrado'}
                className={css.acaoComRotulo}
                aria-haspopup="dialog"
                disabled={!conversa.clienteId}
                onClick={() => setProgramarEm(conversa.id)}
              >
                <Icone nome="relogio" /><span className={css.rotuloAcao}>Programar</span>
              </Botao>
              <Botao title="Nota interna: fica na ficha, o cliente nunca vê" aria-label="Nota interna" className={css.acaoComRotulo} onClick={aoAbrirNota}>
                <Icone nome="nota" /><span className={css.rotuloAcao}>Nota</span>
              </Botao>
              {/* #2134: chave do completar com Tab. Só existe no modo API (é lá que a sugestão chama o
                  provedor). Desligada, nenhuma chamada sai; vale por usuário, neste navegador. */}
              {fonteApi && <Botao
                aria-pressed={complementoLigado}
                title={complementoLigado
                  ? 'Completar com Tab: ligado. O que você digita é enviado à IA para sugerir a continuação. Toque para desligar.'
                  : 'Completar com Tab: desligado. Nada do que você digita é enviado à IA. Toque para ligar.'}
                className={`${css.acaoComRotulo} ${complementoLigado ? css.chaveLigada : ''}`}
                onClick={alternarComplemento}
              >
                <Icone nome="sparkles" /><span className={css.rotuloAcao}>Completar com Tab</span>
              </Botao>}
              {/* Microfone ocupa o lugar do Enviar quando o campo está vazio e
                  não há anexo em espera (mesmo gesto do WhatsApp): campo vazio
                  não tem o que enviar como texto, mas tem o que gravar. */}
              {temTexto || anexo || !acoes.audio ? (
                <Botao variante="primario" className={css.enviar} aria-label="Enviar" title={variavelSobrando ?? (temTexto || anexo ? 'Enviar (Enter)' : restricao ?? 'Escreva a mensagem para enviar')} disabled={!podeEscrever || assumindo || Boolean(variavelSobrando) || !(temTexto || anexo)} onClick={enviarComposer}>
                  <Icone nome="arrow-up" tamanho={20} />
                </Botao>
              ) : (
                <Botao
                  variante="primario"
                  className={css.enviar}
                  aria-label="Gravar áudio"
                  disabled={!podeEscrever || !podeAudio}
                  title={motivoDeFormato(canal, 'audio') ?? 'Gravar áudio'}
                  onClick={gravador.iniciar}
                >
                  <Icone nome="mic" tamanho={20} />
                </Botao>
              )}
            </div>
          </div>

          {/* #1870: em coluna estreita a frase inteira quebrava em 2 linhas. Canal só de texto mostra
              o curto; o motivo inteiro segue no title, no leitor de tela e no aria-describedby. */}
          {restricao && (podeAnexar || podeAudio
            ? <p id={idRestricao} className={css.restricao}>{restricao}</p>
            : <p id={idRestricao} className={css.restricao} title={restricao}><span aria-hidden="true">Só texto neste canal</span><span className="sr">{restricao}</span></p>)}
        </>
      )}

      {modeloAberto && <ModalEnviarModelo conversa={conversa} aoFechar={aoFecharModelos} />}

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
