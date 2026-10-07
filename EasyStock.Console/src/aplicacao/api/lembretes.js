import * as acao from '../acoes'
import {
  concluirLembreteNaApi, criarLembreteNaApi, listarLembretes, listarNotificacoesNaoLidas, marcarNotificacaoLida,
} from '../../infra/api/notificacoesApi'
import { avisoDaApi, lembreteDaApi, lembreteEhManual } from '../../infra/api/traducaoNotificacoes'
import { FONTES } from '../../dominio/lembrete'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

// Lê do EasyStok o que o sininho mostra além dos automáticos da tela (#1426): os lembretes
// manuais (S43) e as notificações InApp não lidas. Cada fonte vai separada: a que falhou fica
// como estava, sem apagar a outra nem encher a faixa de aviso a cada volta da leitura.
export async function lerLembretesDaApi(despachar) {
  const [lembretes, avisos] = await Promise.allSettled([listarLembretes(), listarNotificacoesNaoLidas()])
  despachar({
    tipo: acao.SINCRONIZAR_LEMBRETES_API,
    lembretes: lembretes.status === 'fulfilled' ? (lembretes.value ?? []).filter(lembreteEhManual).map(lembreteDaApi) : undefined,
    avisos: avisos.status === 'fulfilled' ? (avisos.value ?? []).map(avisoDaApi) : undefined,
  })
}

// Concluído no modo API sai do sininho sem virar mensagem de sistema na conversa: essa mensagem
// seria só do navegador, e a próxima sincronização a apagaria.
const tirarDoSininho = (despachar, lembrete) =>
  despachar({ tipo: acao.CONCLUIR_LEMBRETE, lembreteId: lembrete.id, titulo: lembrete.titulo, conversaId: null })

// Programar e concluir lembrete no modo API (#1426). Programar grava no EasyStok e o item só
// aparece com a resposta; concluir chama a rota da fonte do item (lembrete ou notificação). O
// automático calculado pela tela não existe no servidor: concluir só o tira deste sininho.
export function criarAcoesLembretesApi({ despachar }) {
  const avisar = (prefixo) => (erro) => despachar({ tipo: acao.AVISO_API, mensagem: `${prefixo}: ${erro.message}` })

  return {
    criarLembrete: (lembrete) => criarLembreteNaApi({
      texto: lembrete.titulo,
      venceEm: Number.isFinite(lembrete.quando) ? new Date(lembrete.quando).toISOString() : null,
      conversaId: GUID.test(lembrete.conversaId ?? '') ? lembrete.conversaId : null,
    })
      .then((dto) => despachar({ tipo: acao.CRIAR_LEMBRETE, lembrete: lembreteDaApi(dto) }))
      .catch(avisar('Lembrete não gravado')),

    concluirLembrete: (lembrete) => {
      if (lembrete.fonte === FONTES.AVISO) {
        return marcarNotificacaoLida(lembrete.servidorId)
          .then(() => tirarDoSininho(despachar, lembrete))
          .catch(avisar('Aviso não marcado como lido'))
      }
      if (lembrete.fonte === FONTES.LEMBRETE) {
        return concluirLembreteNaApi(lembrete.servidorId)
          .then(() => tirarDoSininho(despachar, lembrete))
          .catch(avisar('Lembrete não concluído'))
      }
      tirarDoSininho(despachar, lembrete)
      return Promise.resolve()
    },
  }
}
