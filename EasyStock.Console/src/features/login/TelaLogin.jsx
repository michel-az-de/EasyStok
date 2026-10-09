import { useCallback, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoTexto } from '../../componentes/Campo'
import { Marca } from '../../componentes/Marca'
import { InterruptorTema } from '../../componentes/InterruptorTema'
import { BotaoGoogle } from './BotaoGoogle'
import css from './login.module.css'

// Um passo, com a empresa resolvida pela API (M0.2).
export function TelaLogin({ entrarNaEmpresa, google, avisoSaida }) {
  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState(null)
  const [ocupado, setOcupado] = useState(false)

  const tentar = async (acao) => {
    setErro(null)
    setOcupado(true)
    try {
      await acao()
    } catch (e) {
      setErro(e.message)
    } finally {
      setOcupado(false)
    }
  }

  const entrarComGoogle = useCallback((idToken) => tentar(() => google.entrar(idToken)), [google])
  const falhaDoGoogle = useCallback((mensagem) => setErro(mensagem), [])

  const aoEnviar = (evento) => {
    evento.preventDefault()
    if (!ocupado) tentar(() => entrarNaEmpresa(email.trim(), senha))
  }

  return (
    <main className={css.tela}>
      <section className={css.cartao} aria-labelledby="titulo-login">
        <Marca />
        <div>
          <h1 id="titulo-login" className={css.titulo}>Entrar na Casa da Baba</h1>
          <p className={css.apoio}>Use seu e-mail e senha para acessar a operação.</p>
        </div>
        {avisoSaida && <p role="status">{avisoSaida}</p>}
        <form className={css.formulario} onSubmit={aoEnviar} aria-busy={ocupado}>
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
        </form>
        {google && (
          <div className={css.google}>
            <BotaoGoogle buscarClientId={google.buscarClientId} aoReceberToken={entrarComGoogle} aoFalhar={falhaDoGoogle} />
          </div>
        )}

        {erro && <p className={css.erro} role="alert">{erro}</p>}
        <div className={css.preferencia}><InterruptorTema /></div>
      </section>
    </main>
  )
}
