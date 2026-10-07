import { useCallback, useEffect, useMemo, useState } from 'react'
import { ESTADOS_AVISO, ativarAvisosNoAparelho, conferirAvisosNoAparelho } from './avisosNoAparelho'
import {
  inscreverPushNoNavegador, inscricaoPushAtual, pedirPermissaoPush, permissaoPush, pushSuportado,
} from '../infra/pushNavegador'
import { inscreverPush, obterChavePush } from '../infra/api/notificacoesApi'

const navegador = {
  suportado: pushSuportado,
  permissao: permissaoPush,
  pedirPermissao: pedirPermissaoPush,
  inscricaoAtual: inscricaoPushAtual,
  inscrever: inscreverPushNoNavegador,
}
const api = { obterChavePush, inscreverPush }

// Estado do Provider, não do reducer (mesma razão da notificação do navegador): permissão e
// assinatura são do aparelho, não do atendimento. Só existe no modo API, onde há para quem
// mandar o aviso.
export function useAvisosNoAparelho({ ativo }) {
  const [situacao, setSituacao] = useState(() => ({
    estado: ativo ? ESTADOS_AVISO.DESLIGADO : ESTADOS_AVISO.INDISPONIVEL, mensagem: null,
  }))

  useEffect(() => {
    if (!ativo) return undefined
    let vivo = true
    conferirAvisosNoAparelho({ navegador, api }).then((s) => { if (vivo) setSituacao(s) })
    return () => { vivo = false }
  }, [ativo])

  const ativar = useCallback(async () => {
    // Sem await antes do pedido de permissão: o clique ainda vale quando ele sai.
    const pendente = ativarAvisosNoAparelho({ navegador, api })
    setSituacao({ estado: ESTADOS_AVISO.ATIVANDO, mensagem: null })
    setSituacao(await pendente)
  }, [])

  return useMemo(() => ({ ...situacao, ativar }), [situacao, ativar])
}
