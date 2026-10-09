import * as acao from '../acoes'
import {
  concluirLembreteNaApi, criarLembreteNaApi, listarLembretes, marcarLembretesVistosNaApi,
} from '../../infra/api/notificacoesApi'
import { lembreteDaApi } from '../../infra/api/traducaoNotificacoes'
import { FONTES } from '../../dominio/lembrete'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export async function lerLembretesDaApi() {
  return ((await listarLembretes()) ?? []).map(lembreteDaApi)
}

export function criarAcoesLembretesApi({ despachar }) {
  const criacoes = new Map()
  const conclusoes = new Map()
  return {
    criarLembrete: (lembrete) => {
      const corpo = {
        texto: lembrete.titulo,
        venceEm: Number.isFinite(lembrete.quando) ? new Date(lembrete.quando).toISOString() : null,
        conversaId: GUID.test(lembrete.conversaId ?? '') ? lembrete.conversaId : null,
      }
      const assinatura = JSON.stringify(corpo)
      let envio = criacoes.get(assinatura)
      if (!envio) {
        envio = { chave: crypto.randomUUID(), promessa: null }
        criacoes.set(assinatura, envio)
      }
      // Conserva a chave quando a resposta se perde depois da gravação.
      envio.promessa ??= criarLembreteNaApi(corpo, envio.chave).then((dto) => {
        const gravado = lembreteDaApi(dto)
        despachar({ tipo: acao.CRIAR_LEMBRETE, lembrete: gravado })
        criacoes.delete(assinatura)
        return gravado
      }).finally(() => { envio.promessa = null })
      return envio.promessa
    },
    concluirLembrete: (lembrete) => {
      if (conclusoes.has(lembrete.id)) return conclusoes.get(lembrete.id)
      const chamada = lembrete.fonte === FONTES.LEMBRETE
        ? concluirLembreteNaApi(lembrete.servidorId) : Promise.resolve()
      const pendente = chamada.then(() => despachar({
        tipo: acao.CONCLUIR_LEMBRETE, lembreteId: lembrete.id, titulo: lembrete.titulo, conversaId: null,
      })).finally(() => conclusoes.delete(lembrete.id))
      conclusoes.set(lembrete.id, pendente)
      return pendente
    },
    marcarLembretesVistos: async (chaves) => {
      if (chaves.some((chave) => chave.startsWith('api-lembrete-'))) await marcarLembretesVistosNaApi()
      despachar({ tipo: acao.MARCAR_LEMBRETES_VISTOS, chaves })
    },
  }
}