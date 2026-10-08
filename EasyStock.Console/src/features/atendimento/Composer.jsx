import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { useAcoes } from '../../aplicacao/contextos'
import { useAtalhoBarra } from '../../aplicacao/useAtalhoBarra'
import { aceitaFormato, motivoDeFormato, restricoesDoCanal } from '../../dominio/canal'
import {
  mensagemDeArquivoAnexado, mensagemDeAudio, mensagemDeImagemAnexada, tipoDeArquivo, validarArquivo,
} from '../../dominio/anexos'
import { escolherFormatoGravacao } from '../../dominio/audio'
import { useArquivoComoDataUrl } from '../../hooks/useArquivoComoDataUrl'
import { useGravadorAudio } from '../../hooks/useGravadorAudio'
import { BotaoAnexarArquivo, GravadorAudio, PreviaAnexo } from './ComposerAnexos'
import { textoConviteCardapio } from '../../dominio/cardapioLink'
import { ModalEnviarCardapio } from './ModalEnviarCardapio'
import { SeletorRespostas } from './SeletorRespostas'
import css from './atendimento.module.css'

export function Composer({
  conversa, canal, podeEscrever, motivo, rascunho,
  aoMudarRascunho, aoEnviar, aoAbrirNota, aoAbrirGaleria, aoAbrirBiblioteca,
  aoReabrir,
}) {
  const { enviarMidia, enviar, obterLinkCardapio } = useAcoes()
  // Seletor rápido (#1441): "/" no campo ou o botão Respostas abrem a lista
  // compacta de respostas prontas; Enter insere no campo.
  const barra = useAtalhoBarra({ rascunho, conversa, aoInserir: aoMudarRascunho })
  // `.composer` tem overflow-y:auto (válvula de segurança de tela baixa,
  // atendimento.module.css) e isso RECORTA um Popover ancorado nele, porque
  // overflow num eixo obriga o outro a deixar de ser `visible`. A correção
  // (prop `portal` do Popover.jsx) mede o retângulo de `campoRef` por dentro
  // do próprio componente agora; aqui só sobra a ref.
  const campoRef = useRef(null)

  // Frente Anexos (rodada 7, pedido do dono 24/09/2026 04h12): anexo de
  // arquivo em espera (prévia antes de enviar) e o gravador de áudio.
  const [anexo, setAnexo] = useState(null)
  const [erroAnexo, setErroAnexo] = useState(null)
  // Link do cardápio para a prévia (#1353): `{ url, daLoja }` depois de obtido; null fecha.
  const [linkCardapio, setLinkCardapio] = useState(null)
  const [buscandoLink, setBuscandoLink] = useState(false)
  const { ler } = useArquivoComoDataUrl()
  const gravador = useGravadorAudio({ escolherTipo: escolherFormatoGravacao })
  const gravando = gravador.estado !== 'ocioso'

  async function aoEscolherArquivo(arquivo) {
    setErroAnexo(null)
    const { aceito, motivo: motivoRecusa } = validarArquivo({ tipo: arquivo.type, tamanho: arquivo.size })
    if (!aceito) { setErroAnexo(motivoRecusa); return }
    const tipo = tipoDeArquivo(arquivo.type)
    const bloqueioCanal = motivoDeFormato(canal, tipo === 'imagem' ? 'foto' : 'arquivo')
    if (bloqueioCanal) { setErroAnexo(bloqueioCanal); return }
    const dataUrl = await ler(arquivo)
    setAnexo({
      tipo, nome: arquivo.name, tamanho: arquivo.size, dataUrl,
    })
  }

  // "Enviar" com anexo em espera manda o anexo (o texto digitado vira legenda
  // de imagem, se houver); sem anexo, segue o fluxo de sempre. Um só botão,
  // uma só tecla Enter, sem o composer perguntar qual dos dois você quis.
  function enviarComposer() {
    if (anexo) {
      const molde = anexo.tipo === 'imagem'
        ? mensagemDeImagemAnexada({ nomeArquivo: anexo.nome, dataUrl: anexo.dataUrl, legenda: rascunho })
        : mensagemDeArquivoAnexado({ nomeArquivo: anexo.nome, tamanho: anexo.tamanho, dataUrl: anexo.dataUrl })
      enviarMidia(conversa.id, molde)
      setAnexo(null)
      aoMudarRascunho('')
      return
    }
    // Enter com o campo vazio não manda balão vazio (#1287).
    if (!rascunho.trim()) return
    aoEnviar()
  }

  // #1444 (homologação 07/10: "não sei se ele tá enviando"): o gravador fica no lugar mostrando o
  // envio até o EasyStok responder; o balão na conversa nasce "enviando". Se não sair, o motivo
  // fica no balão e aqui só o aviso curto.
  const [enviandoAudio, setEnviandoAudio] = useState(false)
  async function aoEnviarAudio() {
    setErroAnexo(null)
    setEnviandoAudio(true)
    try {
      const resultado = await gravador.finalizar()
      if (!resultado) return
      const saiu = await enviarMidia(conversa.id, mensagemDeAudio(resultado))
      if (saiu === false) setErroAnexo('O áudio não foi enviado. O motivo está no balão da conversa.')
    } finally {
      setEnviandoAudio(false)
    }
  }

  const temTexto = rascunho.trim().length > 0

  // O canal manda no que o composer oferece. Botão que o canal não entrega
  // fica desabilitado com o motivo escrito, em vez de mandar e cair calado.
  // "Anexar" cobre imagem e PDF (dois formatos, `aceitaFormato` por um só);
  // o motivo específico aparece depois de escolher o arquivo (`erroAnexo`),
  // porque só ali dá pra saber qual dos dois ela quis mandar.
  const podeAudio = aceitaFormato(canal, 'audio')
  const restricao = restricoesDoCanal(canal)

  // US-021 (link do cardápio com carrinho): manda o convite com o link de
  // uma vez, no mesmo texto livre que Respostas já usa, respeitando o mesmo
  // `podeEscrever` do canal. A janela do site é quem monta o pedido e devolve
  // pela conversa (infra/canalEntreJanelas.js), nunca cola nada aqui.
  // Rodada 12 (issue #16): o botão abre a prévia com o link e a origem
  // dele; o envio é o mesmo de antes, só que depois de ela ver.
  // #1353: no modo API o link vem da loja (o mesmo do agente), então a prévia só abre
  // depois de ele chegar; falha vira aviso na faixa e a prévia não abre.
  const abrirCardapio = async () => {
    const conversaId = conversa.id
    setBuscandoLink(true)
    try {
      const link = await obterLinkCardapio(conversaId)
      if (link) setLinkCardapio({ ...link, conversaId })
    } finally {
      setBuscandoLink(false)
    }
  }
  const linkDaConversa = linkCardapio?.conversaId === conversa.id ? linkCardapio : null
  const textoCardapio = linkDaConversa ? textoConviteCardapio(conversa.nome, linkDaConversa.url) : ''
  const aoEnviarCardapio = () => {
    enviar(conversa.id, textoCardapio)
    setLinkCardapio(null)
  }

  return (
    <div className={css.composer}>
      {motivo && (
        <p className={`${css.bloqueio} ${motivo.acao === 'bloqueio' ? css.bloqueioGrave : ''}`}>
          <Icone nome={motivo.acao === 'bloqueio' ? 'bloqueio' : 'alerta'} />
          <strong>{motivo.titulo}</strong>
          <span>{motivo.detalhe}</span>
          {motivo.acao === 'reabrir' && aoReabrir && (
            <Botao onClick={aoReabrir}>Reabrir conversa</Botao>
          )}
        </p>
      )}

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

          {/* Rodada 6d: composer em uma linha, como no Mensagens. Ícones à
              esquerda, o campo numa pílula que cresce com o texto, o enviar
              (ou o microfone, com o campo vazio) redondo à direita. A .caixa
              só agrupa; a .acoes some do layout no CSS. */}
          <div className={css.caixa}>
            <div ref={campoRef}>
              <CampoArea
                rotulo="Mensagem para o cliente"
                rotuloOculto
                value={rascunho}
                disabled={!podeEscrever}
                placeholder={!podeEscrever
                  ? 'Escolha um modelo em Respostas'
                  : anexo
                    ? 'Legenda (opcional)'
                    // O atalho / continua valendo, mas a dica não o repete: o botão
                    // Respostas, com rótulo, já mostra o caminho, e a frase longa
                    // quebrava em três linhas no campo (rodada 10, integração).
                    : `Escreva para ${conversa.nome.split(' ')[0]}`}
                onChange={(e) => aoMudarRascunho(e.target.value)}
                onKeyDown={(e) => {
                  if (barra.aoTeclar(e)) return
                  if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); enviarComposer() }
                }}
              />
              {/* `portal`: mede o retângulo deste wrapper (via parentElement
                  do próprio Popover) e planta o conteúdo em document.body,
                  fora do recorte de `.composer`. Visual idêntico ao de antes,
                  só que agora fora do overflow. */}
              {barra.aberto && <SeletorRespostas seletor={barra} aoGerenciar={aoAbrirBiblioteca} />}
            </div>

            <div className={css.acoes}>
              {/* Rodada 10, pedido literal do dono (25/09 23h42): "não vi pra
                  enviar uma lista de mensagens do sistema". O ícone sozinho
                  (rótulo escondido, decisão 60) não foi achado por ele. Só
                  este botão ganha rótulo visível, mesma ideia de "Automáticas"
                  no trilho (ícone e palavra juntos); os vizinhos (Anexar,
                  Fotos, Cardápio, Nota, Enviar) continuam ícone só. */}
              <Botao
                disabled={!podeEscrever}
                aria-haspopup="listbox"
                aria-expanded={barra.aberto}
                title="Respostas prontas (atalho: / no campo)"
                className={css.botaoRespostas}
                onClick={barra.abrir}
              >
                <Icone nome="respostas" />
                <span>Respostas</span>
              </Botao>
              {/* Rodada 12 (issue #16, dúvida da Thatiane "esse botão é pra
                  quê?"): os quatro vizinhos ganham nome visível, como o
                  Respostas. Em coluna estreita o nome some por container
                  query e fica o title com a explicação inteira. */}
              <BotaoAnexarArquivo
                disabled={!podeEscrever}
                titulo="Anexar arquivo do computador: foto, cardápio em PDF ou comprovante (imagem ou PDF, até 8 MB)"
                rotulo="Anexar"
                classeRotulo={css.rotuloAcao}
                aoEscolher={aoEscolherArquivo}
              />
              <Botao title="Fotos dos pratos cadastrados na casa" className={css.acaoComRotulo} disabled={!podeEscrever} onClick={aoAbrirGaleria}>
                <Icone nome="imagem" /><span className={css.rotuloAcao}>Fotos</span>
              </Botao>
              <Botao
                title="Enviar ao cliente o link do cardápio de hoje (mostra o link antes de enviar)"
                className={css.acaoComRotulo}
                aria-haspopup="dialog"
                disabled={!podeEscrever || buscandoLink}
                onClick={abrirCardapio}
              >
                <Icone nome="cardapio" /><span className={css.rotuloAcao}>Cardápio</span>
              </Botao>
              <Botao title="Nota interna: fica na ficha, o cliente nunca vê" aria-label="Nota interna" className={css.acaoComRotulo} onClick={aoAbrirNota}>
                <Icone nome="nota" /><span className={css.rotuloAcao}>Nota</span>
              </Botao>
              {/* Microfone ocupa o lugar do Enviar quando o campo está vazio e
                  não há anexo em espera (mesmo gesto do WhatsApp): campo vazio
                  não tem o que enviar como texto, mas tem o que gravar. */}
              {temTexto || anexo ? (
                <Botao variante="primario" className={css.enviar} aria-label="Enviar" title="Enviar (Enter)" disabled={!podeEscrever} onClick={enviarComposer}>
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

          {restricao && <p className={css.restricao}>{restricao}</p>}
        </>
      )}

      {linkDaConversa && (
        <ModalEnviarCardapio
          nomeCliente={conversa.nome}
          canalNome={canal.nome}
          link={linkDaConversa.url}
          daLoja={linkDaConversa.daLoja}
          texto={textoCardapio}
          aoEnviar={aoEnviarCardapio}
          aoFechar={() => setLinkCardapio(null)}
        />
      )}
    </div>
  )
}
