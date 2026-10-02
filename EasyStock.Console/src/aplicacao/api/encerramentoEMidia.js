import * as acao from '../acoes'
import { encerrar, enviarImagem, enviarTexto } from '../../infra/api/conversasApi'
import { mensagemDaApi } from '../../infra/api/traducaoConversas'
import { proximoId } from '../../infra/repositorioConversas'

// Encerrar pelo modal e mandar foto no modo API (F06, #1236).
//
// Encerrar: a despedida marcada sai pelo envio de texto real (S07) e a conversa fecha no
// EasyStok (`POST .../encerrar`). O resumo, a avaliação, a anotação e os avisos por e-mail
// e SMS do modo demonstração não têm endpoint: não viram registro local, só avisam.
//
// Mídia: só foto no WhatsApp tem endpoint (S02, multipart). Áudio, arquivo, figurinha,
// peça da galeria e foto em outro canal avisam e não aparecem como enviados.
const CANAL_COM_FOTO = 'WhatsApp'

const ROTULO_DA_MIDIA = {
  audio: 'Áudio', arquivo: 'Arquivo', figurinha: 'Figurinha', peca: 'Peça da galeria',
}

export function criarAcoesEncerramentoEMidiaApi({ despachar, agoraRef, estadoRef, falhaDoEnvio }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })
  const conversaDe = (id) => estadoRef.current.conversas.find((c) => c.id === id) ?? null

  async function despedir(id, texto) {
    const mensagemId = proximoId('msg')
    despachar({ tipo: acao.ENVIAR_MENSAGEM, id, texto, mensagemId, agora: agoraRef.current })
    try {
      const m = await enviarTexto(id, texto)
      despachar({ tipo: acao.CONFIRMAR_ENVIO_API, id, mensagemId, mensagem: mensagemDaApi(m) })
      return true
    } catch (erro) {
      despachar(falhaDoEnvio(id, mensagemId, erro))
      return false
    }
  }

  return {
    encerrarComResumo: async (id, _agora, { enviarMensagem = false, textoMensagem = '', avisoEmail = false, avisoSms = false } = {}) => {
      const pedido = conversaDe(id)?.pedido
      const texto = textoMensagem.trim()
      const despedidaSaiu = enviarMensagem && texto ? await despedir(id, texto) : null
      try {
        await encerrar(id)
      } catch (erro) {
        avisar(`Atendimento não encerrado: ${erro.message}`)
        return
      }
      despachar({ tipo: acao.ENCERRAR_ATENDIMENTO, id })
      despachar({ tipo: acao.FECHAR_ENCERRAMENTO })
      const partes = ['Atendimento encerrado no EasyStok.']
      if (despedidaSaiu === false) partes.push('A despedida não saiu (veja a mensagem na conversa).')
      // O modal cancela o pedido sem pagamento, mas cancelar ainda não está ligado (F03).
      if (pedido?.pedidoId && pedido.estado === 'aguardando') partes.push('O pedido sem pagamento segue aberto no EasyStok.')
      partes.push('Resumo, avaliação e anotação ainda não são guardados nesta versão.')
      if (avisoEmail || avisoSms) partes.push('Aviso por e-mail e SMS também não sai ainda.')
      avisar(partes.join(' '))
    },

    enviarMidia: (id, { formato, arte, texto, ...extra }) => {
      if (formato !== 'imagem') {
        avisar(`${ROTULO_DA_MIDIA[formato] ?? 'Mídia'}: ainda não ligado nesta versão. Pelo EasyStok sai só foto no WhatsApp.`)
        return
      }
      const canal = conversaDe(id)?.canal
      if (canal !== CANAL_COM_FOTO) {
        avisar(`Foto: ainda não ligada para ${canal ?? 'este canal'} nesta versão. Pelo EasyStok sai só no WhatsApp.`)
        return
      }
      const mensagemId = proximoId('mid')
      // Nasce "enviando" (não "lida"): só a resposta do EasyStok confirma que saiu.
      despachar({
        tipo: acao.ENVIAR_MIDIA, id, formato, arte, texto, ...extra, status: 'enviando', agora: agoraRef.current, mensagemId,
      })
      enviarImagem(id, { dataUrl: arte, nomeArquivo: extra.nomeArquivo, legenda: extra.legenda })
        .then((m) => despachar({ tipo: acao.CONFIRMAR_ENVIO_API, id, mensagemId, mensagem: mensagemDaApi(m) }))
        .catch((erro) => despachar(falhaDoEnvio(id, mensagemId, erro)))
    },
  }
}
