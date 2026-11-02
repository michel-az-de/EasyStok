import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { configGoogle, entrar, entrarComGoogle, sair } from '../infra/api/autenticacao'
import { EVENTO_SESSAO_EXPIRADA, garantirSessao } from '../infra/api/cliente'
import { EVENTO_FALHA_SAIDA_PUSH, EVENTO_SESSAO_ALTERADA, gravarSessaoNativa, lerSessao, limparSessao } from '../infra/api/sessao'
import { entrarGoogleNativo, obterSessaoNativa, temSessaoNativa } from '../infra/sessaoNativa'
import { AVISO_SESSAO_VENCIDA } from '../dominio/sessao'
import { liberarEntradaSozinha, opcoesDaEntradaGoogle, travarEntradaSozinha } from '../infra/entradaGoogle'
import { esquecerRascunhos } from '../infra/api/rascunhosDaSessao'
import { esquecerVisitas } from '../infra/api/visitaDaSessao'

// Sessão do modo API (F01). Um 401 em qualquer chamada limpa a sessão e dispara
// EVENTO_SESSAO_EXPIRADA; aqui isso vira "volta para o login".
export function useSessaoApi() {
  const [sessao, setSessao] = useState(() => temSessaoNativa() ? null : lerSessao())
  const [avisoSaida, setAvisoSaida] = useState(null)
  const [retomando, setRetomando] = useState(temSessaoNativa)
  const [erroRetomada, setErroRetomada] = useState(null)
  const versaoLocal = useRef(0)
  const versaoNativa = useRef(-1)

  const retomarNativa = useCallback(async () => {
    const versao = ++versaoLocal.current
    setRetomando(true)
    setErroRetomada(null)
    try {
      const dados = await obterSessaoNativa()
      if (versao !== versaoLocal.current || (Number.isFinite(dados?.versao) && dados.versao < versaoNativa.current)) return
      if (Number.isFinite(dados?.versao)) versaoNativa.current = dados.versao
      if (!dados?.autenticado) { esquecerRascunhos(); esquecerVisitas() }
      setSessao(gravarSessaoNativa(dados))
    }
    catch (erro) {
      if (versao !== versaoLocal.current || erro.codigo === 'SESSAO_ALTERADA') return
      if (erro.status === 401 || erro.status === 403) { limparSessao(); setSessao(null) }
      else setErroRetomada('Confira a conexão para retomar o Atendimento. Sua conta continua guardada no aplicativo.')
    } finally { if (versao === versaoLocal.current) setRetomando(false) }
  }, [])

  useEffect(() => {
    if (!temSessaoNativa()) return
    retomarNativa()
    const alterada = (evento) => {
      if (Number.isFinite(evento.detail?.versao) && evento.detail.versao < versaoNativa.current) return
      if (Number.isFinite(evento.detail?.versao)) versaoNativa.current = evento.detail.versao
      if (evento.detail?.autenticado === false) { versaoLocal.current++; limparSessao(); esquecerRascunhos(); esquecerVisitas(); setSessao(null); setErroRetomada(null); setRetomando(false) }
      else retomarNativa()
    }
    window.addEventListener('easystok:native-session-changed', alterada)
    return () => { versaoLocal.current++; window.removeEventListener('easystok:native-session-changed', alterada) }
  }, [retomarNativa])

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
      if (temSessaoNativa()) {
        garantirSessao().then(setSessao).catch(() => { /* sem rede não apaga a credencial nativa */ })
        return
      }
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

  const entrarNativo = useCallback(async () => {
    const versao = ++versaoLocal.current
    setAvisoSaida(null)
    setErroRetomada(null)
    const dados = await entrarGoogleNativo()
    if (versao !== versaoLocal.current || (Number.isFinite(dados?.versao) && dados.versao < versaoNativa.current)) return
    if (Number.isFinite(dados?.versao)) versaoNativa.current = dados.versao
    setSessao(gravarSessaoNativa(dados))
  }, [])

  const encerrarSessao = useCallback(() => {
    versaoLocal.current++
    // #1810: sair não pode virar entrar de novo pelo One Tap.
    travarEntradaSozinha()
    sair().catch(() => setAvisoSaida('Você saiu deste aparelho. Não foi possível confirmar o encerramento no servidor.'))
    setSessao(null)
    window.location.hash = '#/'
  }, [])

  // #1354: identidade estável; um objeto novo a cada render redesenhava o botão do Google.
  const google = useMemo(() => ({ buscarClientId: configGoogle, entrar: entrarGoogle, opcoes: opcoesDaEntradaGoogle,
    nativo: temSessaoNativa(), entrarNativo, retomarNativa }), [entrarGoogle, entrarNativo, retomarNativa])
  return { sessao, entrarNaEmpresa, google, encerrarSessao, avisoSaida, retomando, erroRetomada }
}
