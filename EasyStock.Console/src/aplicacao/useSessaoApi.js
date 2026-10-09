import { useCallback, useEffect, useMemo, useState } from 'react'
import { configGoogle, entrar, entrarComGoogle, sair } from '../infra/api/autenticacao'
import { EVENTO_SESSAO_EXPIRADA } from '../infra/api/cliente'
import { lerSessao } from '../infra/api/sessao'

// Sessão do modo API (F01). Um 401 em qualquer chamada limpa a sessão e dispara
// EVENTO_SESSAO_EXPIRADA; aqui isso vira "volta para o login".
export function useSessaoApi() {
  const [sessao, setSessao] = useState(() => lerSessao())

  useEffect(() => {
    const expirou = () => setSessao(null)
    window.addEventListener(EVENTO_SESSAO_EXPIRADA, expirou)
    return () => window.removeEventListener(EVENTO_SESSAO_EXPIRADA, expirou)
  }, [])

  const entrarNaEmpresa = useCallback(async (email, senha) => {
    setSessao(await entrar(email, senha))
  }, [])

  const entrarGoogle = useCallback(async (idToken) => {
    setSessao(await entrarComGoogle(idToken))
  }, [])

  const encerrarSessao = useCallback(() => {
    sair()
    setSessao(null)
  }, [])

  // #1354: identidade estável; um objeto novo a cada render redesenhava o botão do Google.
  const google = useMemo(() => ({ buscarClientId: configGoogle, entrar: entrarGoogle }), [entrarGoogle])
  return { sessao, entrarNaEmpresa, google, encerrarSessao }
}
