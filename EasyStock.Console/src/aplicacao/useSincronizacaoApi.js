import { useEffect } from 'react'
import * as acao from './acoes'
import { listarConversas, listarMensagens } from '../infra/api/conversasApi'
import { conversaDaApi } from '../infra/api/traducaoConversas'

// Polling da inbox (F01): não existe SSE de conversas ainda (S18). A lista vem a
// cada ciclo; as mensagens só são buscadas de novo quando a conversa mudou
// (`ultimaMensagemEm`), para 30 conversas não virarem 30 chamadas a cada 5 s.
const INTERVALO_MS = 5000

export function useSincronizacaoApi({ ativo, usuario, despachar }) {
  useEffect(() => {
    if (!ativo) return undefined
    let vivo = true
    let proximo = null
    const cache = new Map()

    async function mensagensDe(resumo) {
      const guardado = cache.get(resumo.id)
      if (guardado && guardado.ultima === resumo.ultimaMensagemEm) return guardado.mensagens
      try {
        const mensagens = await listarMensagens(resumo.id)
        cache.set(resumo.id, { ultima: resumo.ultimaMensagemEm, mensagens })
        return mensagens
      } catch {
        return guardado?.mensagens ?? []
      }
    }

    async function ciclo() {
      try {
        const lista = await listarConversas()
        const conversas = await Promise.all(
          lista.map(async (resumo) => conversaDaApi(resumo, await mensagensDe(resumo), usuario)),
        )
        if (vivo) despachar({ tipo: acao.SINCRONIZAR_CONVERSAS, conversas })
      } catch (erro) {
        if (vivo) despachar({ tipo: acao.SINCRONIZACAO_FALHOU, mensagem: erro.message })
      }
      if (vivo) proximo = setTimeout(ciclo, INTERVALO_MS)
    }

    ciclo()
    return () => {
      vivo = false
      clearTimeout(proximo)
    }
  }, [ativo, usuario, despachar])
}
