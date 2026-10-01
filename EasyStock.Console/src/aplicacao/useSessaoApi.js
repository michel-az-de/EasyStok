import { useCallback, useEffect, useState } from 'react'
import { configGoogle, entrar, entrarComGoogle, listarEmpresas, sair } from '../infra/api/autenticacao'
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

  const entrarNaEmpresa = useCallback(async (email, senha, empresa) => {
    setSessao(await entrar(email, senha, empresa))
  }, [])

  const entrarGoogle = useCallback(async (idToken) => {
    setSessao(await entrarComGoogle(idToken))
  }, [])

  const encerrarSessao = useCallback(() => {
    sair()
    setSessao(null)
  }, [])

  const google = { buscarClientId: configGoogle, entrar: entrarGoogle }
  return { sessao, listarEmpresas, entrarNaEmpresa, google, encerrarSessao }
}
