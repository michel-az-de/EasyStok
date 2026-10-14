import * as acao from '../acoes'
import {
  encerrar, enviarAudio, enviarImagem, enviarImagemCardapio, enviarTexto,
} from '../../infra/api/conversasApi'
import { mensagemDaApi } from '../../infra/api/traducaoConversas'
import { proximoId } from '../../infra/repositorioConversas'
import { criarChavesDeMensagem } from '../chavesDeMensagem'

// Encerrar pelo modal e mandar foto no modo API (F06, #1236).
//
// Encerrar: a despedida marcada sai pelo envio de texto real (S07) e a conversa fecha no
// EasyStok (`POST .../encerrar`). O resumo, a avaliação, a anotação e os avisos por e-mail
// e SMS do modo demonstração não têm endpoint: não viram registro local, e a tela nem mostra
// avaliação e anotação (#1586).
//
// Mídia: foto e áudio no WhatsApp têm endpoint. A foto do computador vai em multipart (S02); a peça
// da galeria do cardápio, pelo id do item (#1437); o áudio gravado, em multipart (#1444). Arquivo,
// figurinha e foto ou áudio em outro canal avisam e não aparecem como enviados.
const CANAL_COM_FOTO = 'WhatsApp'
const CANAL_COM_AUDIO = 'WhatsApp'

const ROTULO_DA_MIDIA = {
  audio: 'Áudio', arquivo: 'Arquivo', figurinha: 'Figurinha', peca: 'Peça da galeria',
}

// #1683: encerrar não cancela pedido calado. O pedido sem pagamento só é cancelado quando a
// atendente marca a caixa, que nasce desmarcada. No modo API só há o que cancelar quando o pedido
// já existe no EasyStok (`pedidoId`); a comanda que ainda é rascunho não oferece a caixa.
export function pedidoAoEncerrar(pedido, { fonteApi = false, cancelarMarcado = false } = {}) {
  const ofereceCancelar = Boolean(pedido) && pedido.estado === 'aguardando' && (!fonteApi || Boolean(pedido.pedidoId))
  return { ofereceCancelar, cancelar: ofereceCancelar && cancelarMarcado === true }
}

// Com link de pagamento em aberto, o EasyStok cancela o pedido sozinho quando o link vence. Sem
// link, nada o fecha depois que a conversa encerra: a caixa nasce marcada, à vista de quem atende.
export function cancelarNasceMarcado(pedido, { fonteApi = false } = {}) {
  return fonteApi && pedidoAoEncerrar(pedido, { fonteApi }).ofereceCancelar && !pedido.cobranca?.link
}

export const rotuloCancelarAoEncerrar = (numero) => `Cancelar também o pedido Nº ${numero}, que ainda não foi pago`

// Cancelamento pedido e não confirmado segura o encerramento: o erro aparece no modal. A
// demonstração é síncrona e não devolve nada: segue como sucesso.
export function falhaAoCancelarNoEncerramento(cancelamento, numero) {
  if (cancelamento == null || cancelamento.ok === true) return null
  const motivo = cancelamento.erro ? `: ${String(cancelamento.erro).replace(/\.$/, '')}.` : '.'
  return `Não deu para cancelar o pedido Nº ${numero}${motivo} O atendimento continua aberto. Tente de novo ou desmarque a caixa de cancelar.`
}

export function criarAcoesEncerramentoEMidiaApi({ despachar, agoraRef, estadoRef, falhaDoEnvio, envioRecusado, chaves = criarChavesDeMensagem() }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })
  const conversaDe = (id) => estadoRef.current.conversas.find((c) => c.id === id) ?? null

  async function despedir(id, texto) {
    if (envioRecusado(id)) return false
    const mensagemId = proximoId('msg')
    despachar({ tipo: acao.ENVIAR_MENSAGEM, id, texto, mensagemId, agora: agoraRef.current })
    try {
      // #1786: a mesma chave do envio pelo Composer: a despedida que saiu e perdeu a resposta não sai de novo.
      const m = await chaves.enviar(id, { texto }, (chave) => enviarTexto(id, texto, { chave }))
      despachar({ tipo: acao.CONFIRMAR_ENVIO_API, id, mensagemId, mensagem: mensagemDaApi(m) })
      return true
    } catch (erro) {
      despachar(falhaDoEnvio(id, mensagemId, erro))
      return false
    }
  }

  // Áudio (#1444): o balão nasce "enviando" e a ação devolve só depois da resposta, para o gravador
  // mostrar o envio. Confirmado, o balão segue tocando o que foi gravado; o arquivo convertido do
  // EasyStok chega na sincronização.
  async function enviarAudioGravado(id, { arte, texto, ...extra }) {
    const canal = conversaDe(id)?.canal
    if (canal !== CANAL_COM_AUDIO) {
      avisar(`Áudio: ainda não ligado para ${canal ?? 'este canal'} nesta versão. Pelo EasyStok sai só no WhatsApp.`)
      return false
    }
    const mensagemId = proximoId('mid')
    despachar({
      tipo: acao.ENVIAR_MIDIA, id, formato: 'audio', arte, texto, ...extra, status: 'enviando', agora: agoraRef.current, mensagemId,
    })
    try {
      const mensagem = mensagemDaApi(await chaves.enviar(id, { formato: 'audio', arte }, (chave) => enviarAudio(id, { dataUrl: arte }, { chave })))
      despachar({
        tipo: acao.CONFIRMAR_ENVIO_API, id, mensagemId, mensagem: { ...mensagem, formato: 'audio', arte, duracaoMs: extra.duracaoMs },
      })
      return mensagem.status !== 'falhou'
    } catch (erro) {
      despachar(falhaDoEnvio(id, mensagemId, erro))
      return false
    }
  }

  // #1586: o erro volta para o modal (a faixa fica atrás da cortina), com a notícia de que a
  // despedida já saiu, para a nova tentativa não mandar outra. `pedidoCancelado` vem do modal,
  // que cancela o pedido sem pagamento antes de encerrar, só quando a atendente marca (#1683).
  async function encerrarPeloModal(id, {
    enviarMensagem = false, textoMensagem = '', avisoEmail = false, avisoSms = false, pedidoCancelado = false,
  } = {}) {
    const texto = textoMensagem.trim()
    const despedidaSaiu = enviarMensagem && texto ? await despedir(id, texto) : null
    try {
      await encerrar(id)
    } catch (erro) {
      const mensagem = `Atendimento não encerrado: ${erro.message}`
      avisar(mensagem)
      return { erro: mensagem, despedidaSaiu: despedidaSaiu === true }
    }
    const pedido = conversaDe(id)?.pedido
    despachar({ tipo: acao.ENCERRAR_ATENDIMENTO, id })
    despachar({ tipo: acao.FECHAR_ENCERRAMENTO })
    // Só ressalva acende a faixa: encerrar que deu certo não sai em tom de alerta.
    const ressalvas = []
    if (despedidaSaiu === false) ressalvas.push('A despedida não saiu (veja a mensagem na conversa).')
    if (!pedidoCancelado && pedido?.pedidoId && pedido.estado === 'aguardando') ressalvas.push('O pedido sem pagamento segue aberto no EasyStok.')
    if (avisoEmail || avisoSms) ressalvas.push('O aviso por e-mail e SMS ainda não sai nesta versão.')
    if (ressalvas.length > 0) avisar(['Atendimento encerrado no EasyStok.', ...ressalvas].join(' '))
    else despachar({ tipo: acao.FECHAR_AVISO_API })
    return undefined
  }

  // #1586: um encerramento por conversa. O segundo toque com o primeiro em voo recebe a mesma
  // promessa, sem outra despedida (a chave do envio, #1786, cobre a repetição depois da resposta perdida).
  const encerrando = new Map()

  return {
    encerrarComResumo: (id, _agora, opcoes) => {
      if (!encerrando.has(id)) {
        encerrando.set(id, encerrarPeloModal(id, opcoes).finally(() => encerrando.delete(id)))
      }
      return encerrando.get(id)
    },

    enviarMidia: async (id, { formato, arte, texto, ...extra }) => {
      if (formato === 'audio') return enviarAudioGravado(id, { arte, texto, ...extra })
      if (formato !== 'imagem' && formato !== 'peca') {
        avisar(`${ROTULO_DA_MIDIA[formato] ?? 'Mídia'}: ainda não ligado nesta versão. Pelo EasyStok saem só foto e áudio no WhatsApp.`)
        return false
      }
      const canal = conversaDe(id)?.canal
      if (canal !== CANAL_COM_FOTO) {
        avisar(`Foto: ainda não ligada para ${canal ?? 'este canal'} nesta versão. Pelo EasyStok sai só no WhatsApp.`)
        return false
      }
      const mensagemId = proximoId('mid')
      // Nasce "enviando" (não "lida"): só a resposta do EasyStok confirma que saiu.
      despachar({
        tipo: acao.ENVIAR_MIDIA, id, formato: 'imagem', arte, texto, ...extra, status: 'enviando', agora: agoraRef.current, mensagemId,
      })
      const legenda = extra.legenda ?? (formato === 'peca' ? [texto, extra.descricao].filter(Boolean).join('\n') : undefined)
      try {
        // Peça do cardápio vai pelo id do item (#1437); foto do computador, em multipart.
        // #1786: a mesma foto (ou peça) de novo antes da confirmação leva a mesma chave e não sai duas vezes.
        const daGaleria = extra.cardapioItemId && Number.isInteger(extra.indice)
        const conteudo = daGaleria
          ? { formato: 'peca', cardapioItemId: extra.cardapioItemId, indice: extra.indice, legenda }
          : { formato: 'imagem', arte, legenda }
        const m = await chaves.enviar(id, conteudo, (chave) => (daGaleria
          ? enviarImagemCardapio(id, { cardapioItemId: extra.cardapioItemId, indice: extra.indice, legenda }, { chave })
          : enviarImagem(id, { dataUrl: arte, nomeArquivo: extra.nomeArquivo, legenda }, { chave })))
        const mensagem = mensagemDaApi(m, id)
        // #1787: como no áudio, o balão segue com a imagem local até a sincronização trazer a do storage.
        despachar({ tipo: acao.CONFIRMAR_ENVIO_API, id, mensagemId, mensagem: { ...mensagem, formato: 'imagem', arte } })
        return mensagem.status !== 'falhou'
      } catch (erro) {
        despachar(falhaDoEnvio(id, mensagemId, erro))
        return false
      }
    },
  }
}
