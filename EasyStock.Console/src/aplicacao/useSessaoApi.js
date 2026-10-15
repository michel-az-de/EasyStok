import { useCallback, useEffect, useMemo, useState } from 'react'
import { configGoogle, entrar, entrarComGoogle, sair } from '../infra/api/autenticacao'
import { EVENTO_SESSAO_EXPIRADA, garantirSessao } from '../infra/api/cliente'
import { EVENTO_FALHA_SAIDA_PUSH, EVENTO_SESSAO_ALTERADA, lerSessao, limparSessao } from '../infra/api/sessao'
import { AVISO_SESSAO_VENCIDA } from '../dominio/sessao'
import { liberarEntradaSozinha, opcoesDaEntradaGoogle, travarEntradaSozinha } from '../infra/entradaGoogle'

// Sessão do modo API (F01). Um 401 em qualquer chamada limpa a sessão e dispara
// EVENTO_SESSAO_EXPIRADA; aqui isso vira "volta para o login".
export function useSessaoApi() {
  const [sessao, setSessao] = useState(() => lerSessao())
  const [avisoSaida, setAvisoSaida] = useState(null)

  useEffect(() => {
    // #1582: quem cai no login por vencimento lê o motivo. A hash fica, então o novo login volta à mesma tela.
    let havia = Boolean(lerSessao())
    const expirou = () => { setAvisoSaida(AVISO_SESSAO_VENCIDA); setSessao(null) }
    const atualizar = () => {
      const atual = lerSessao()
      havia = Boolean(atual)
      setSessao(atual)
    }
    const falhaPush = () => setAvisoSaida('Você saiu. Não foi possível desligar os avisos deste aparelho. Bloqueie as notificações nas configurações do navegador.')
    const conferir = () => {
      if (!lerSessao()) {
        if (havia) setAvisoSaida(AVISO_SESSAO_VENCIDA)
        limparSessao()
      }
      else garantirSessao().catch(() => { /* a tela de trabalho mostra a falha e retenta */ })
    }
    window.addEventListener(EVENTO_SESSAO_EXPIRADA, expirou)
    window.addEventListener(EVENTO_SESSAO_ALTERADA, atualizar)
    window.addEventListener(EVENTO_FALHA_SAIDA_PUSH, falhaPush)
    window.addEventListener('storage', atualizar)
    window.addEventListener('online', conferir)
    const intervalo = setInterval(conferir, 15000)
    return () => {
      clearInterval(intervalo)
      window.removeEventListener(EVENTO_SESSAO_EXPIRADA, expirou)
      window.removeEventListener(EVENTO_SESSAO_ALTERADA, atualizar)
      window.removeEventListener(EVENTO_FALHA_SAIDA_PUSH, falhaPush)
      window.removeEventListener('storage', atualizar)
      window.removeEventListener('online', conferir)
    }
  }, [])

  const entrarNaEmpresa = useCallback(async (email, senha) => {
    setAvisoSaida(null)
    setSessao(await entrar(email, senha))
  }, [])

  const entrarGoogle = useCallback(async (idToken) => {
    setAvisoSaida(null)
    setSessao(await entrarComGoogle(idToken))
    liberarEntradaSozinha()
  }, [])

  const encerrarSessao = useCallback(() => {
    // #1810: sair não pode virar entrar de novo pelo One Tap.
    travarEntradaSozinha()
    sair().catch(() => setAvisoSaida('Você saiu deste aparelho. Não foi possível confirmar o encerramento no servidor.'))
    setSessao(null)
    window.location.hash = '#/'
  }, [])

  // #1354: identidade estável; um objeto novo a cada render redesenhava o botão do Google.
  const google = useMemo(() => ({ buscarClientId: configGoogle, entrar: entrarGoogle, opcoes: opcoesDaEntradaGoogle }), [entrarGoogle])
  return { sessao, entrarNaEmpresa, google, encerrarSessao, avisoSaida }
}
