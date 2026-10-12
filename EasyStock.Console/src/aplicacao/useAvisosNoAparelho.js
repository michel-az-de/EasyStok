import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ESTADOS_AVISO, ativarAvisosNoAparelho, conferirAvisosNoAparelho } from './avisosNoAparelho'
import {
  associarInscricaoPush, desinscreverPushNoNavegador, inscreverPushNoNavegador, inscricaoPushAtual,
  pedirPermissaoPush, permissaoPush, pushSuportado,
} from '../infra/pushNavegador'
import { inscreverPush, obterChavePush } from '../infra/api/notificacoesApi'
import { identidadeDaSessao, lerSessao } from '../infra/api/sessao'

const navegador = {
  suportado: pushSuportado, permissao: permissaoPush, pedirPermissao: pedirPermissaoPush,
  inscricaoAtual: inscricaoPushAtual, inscrever: inscreverPushNoNavegador,
  associar: associarInscricaoPush, desinscrever: desinscreverPushNoNavegador,
}
const api = { obterChavePush, inscreverPush }

export function useAvisosNoAparelho({ sessao }) {
  const [situacao, setSituacao] = useState({ estado: ESTADOS_AVISO.DESLIGADO, mensagem: null })
  const ocupado = useRef(false)
  const geracao = useRef(0)
  const vivo = useRef(false)
  const identidade = identidadeDaSessao(sessao)
  const contexto = useMemo(() => ({
    navegador, api, identidade,
    atual: () => identidadeDaSessao(lerSessao()) === identidade,
  }), [identidade])

  useEffect(() => {
    vivo.current = true
    const atual = ++geracao.current
    conferirAvisosNoAparelho(contexto).then((s) => {
      if (vivo.current && atual === geracao.current) setSituacao(s)
    })
    return () => { vivo.current = false; geracao.current++ }
  }, [contexto])

  const operar = useCallback(async (desligar) => {
    if (ocupado.current) return
    ocupado.current = true
    const atual = ++geracao.current
    // Inicia a operação antes do setState para conservar o gesto de permissão.
    const pendente = desligar
      ? desinscreverPushNoNavegador(identidade).then(() => ({ estado: ESTADOS_AVISO.DESLIGADO, mensagem: null }))
      : ativarAvisosNoAparelho(contexto)
    setSituacao({ estado: ESTADOS_AVISO.ATIVANDO, mensagem: null })
    try {
      const resultado = await pendente
      if (vivo.current && atual === geracao.current) setSituacao(resultado)
    } catch (erro) {
      if (vivo.current && atual === geracao.current) setSituacao({ estado: ESTADOS_AVISO.ERRO, mensagem: erro.message })
    } finally { ocupado.current = false }
  }, [contexto, identidade])
  return { ...situacao, ativar: () => operar(false), desligar: () => operar(true) }
}