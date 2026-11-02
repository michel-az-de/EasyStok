import { useCallback, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoTexto } from '../../componentes/Campo'
import { Marca } from '../../componentes/Marca'
import { InterruptorTema } from '../../componentes/InterruptorTema'
import { BotaoGoogle } from './BotaoGoogle'
import css from './login.module.css'

// Um passo, com a empresa resolvida pela API (M0.2).
export function TelaLogin({ entrarNaEmpresa, google, avisoSaida, retomando = false, erroRetomada = null }) {
  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState(null)
  const [ocupado, setOcupado] = useState(false)

  const tentar = async (acao, mensagem401) => {
    setErro(null)
    setOcupado(true)
    try {
      await acao()
    } catch (e) {
      const semMensagemDeAcesso = e.status === 401 && e.codigo === 'HTTP_401' && e.message === 'Sessão expirada. Entre de novo.'
      setErro(semMensagemDeAcesso && mensagem401 ? mensagem401 : e.message)
    } finally {
      setOcupado(false)
    }
  }

  const entrarComGoogle = useCallback((idToken) => tentar(() => google.entrar(idToken)), [google])
  const falhaDoGoogle = useCallback((mensagem) => setErro(mensagem), [])

  const aoEnviar = (evento) => {
    evento.preventDefault()
    if (!ocupado) tentar(() => entrarNaEmpresa(email.trim(), senha), 'Não foi possível entrar. Confira seu e-mail e senha.')
  }

  return (
    <main className={css.tela}>
      <section className={css.cartao} aria-labelledby="titulo-login">
        <Marca />
        <div>
          <h1 id="titulo-login" className={css.titulo}>{retomando ? 'Abrindo sua conta…' : erroRetomada ? 'Retomar Atendimento' : 'Entrar na Casa da Baba'}</h1>
        </div>
        {avisoSaida && <p className={css.aviso} role="status">{avisoSaida}</p>}
        {erro && <p className={css.erro} role="alert">{erro}</p>}
        {retomando && <p role="status">Retomando a conta do aplicativo.</p>}
        {erroRetomada && <p className={css.aviso} role="status">{erroRetomada}</p>}
        {google?.nativo && !retomando && <Botao variante="primario" largo disabled={ocupado}
          onClick={() => tentar(erroRetomada ? google.retomarNativa : google.entrarNativo)}>
          {ocupado ? 'Aguarde…' : erroRetomada ? 'Tentar novamente' : 'Entrar com Google'}
        </Botao>}
        {!google?.nativo && <form className={css.formulario} onSubmit={aoEnviar} aria-busy={ocupado}>
          <CampoTexto
            rotulo="E-mail" tipo="email" autoComplete="username" required
            value={email} onChange={(e) => setEmail(e.target.value)} readOnly={ocupado}
          />
          <CampoTexto
            rotulo="Senha" tipo="password" autoComplete="current-password" required
            value={senha} onChange={(e) => setSenha(e.target.value)} readOnly={ocupado}
          />
          <Botao variante="primario" tipo="submit" largo disabled={ocupado}>
            {ocupado ? 'Entrando…' : 'Entrar'}
          </Botao>
        </form>}
        {google && !google.nativo && (
          <div className={css.google}>
            <BotaoGoogle buscarClientId={google.buscarClientId} aoReceberToken={entrarComGoogle} aoFalhar={falhaDoGoogle} opcoes={google.opcoes} />
          </div>
        )}

        <div className={css.preferencia}><InterruptorTema /></div>
      </section>
    </main>
  )
}
