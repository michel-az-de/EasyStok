import { useCallback, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoTexto } from '../../componentes/Campo'
import { BotaoGoogle } from './BotaoGoogle'
import css from './login.module.css'

// Login do console no modo API (F01), em dois passos como o EasyStok (ADR-0047):
// credenciais → empresa. Com uma empresa só, entra direto; superadmin é recusado.
export function TelaLogin({ listarEmpresas, entrarNaEmpresa, google }) {
  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [empresas, setEmpresas] = useState(null)
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
    tentar(async () => {
      const resposta = await listarEmpresas(email.trim(), senha)
      const lista = resposta?.empresas ?? []
      // SuperAdmin não tem empresa no token: a inbox viria vazia (EmpresaId vazio na API).
      if (resposta?.isSuperAdmin) throw new Error('Superadmin não atende conversas. Entre com um usuário da empresa.')
      if (lista.length === 1) {
        await entrarNaEmpresa(email.trim(), senha, lista[0] ?? null)
        return
      }
      if (lista.length === 0) throw new Error('Este usuário não tem empresa ativa.')
      setEmpresas(lista)
    })
  }

  return (
    <main className={css.tela}>
      <section className={css.cartao} aria-labelledby="titulo-login">
        <h1 id="titulo-login" className={css.titulo}>EasyStok</h1>
        <p className={css.subtitulo}>Console de atendimento</p>

        {empresas ? (
          <div className={css.empresas}>
            <p className={css.pergunta}>Qual empresa?</p>
            {empresas.map((empresa) => (
              <Botao
                key={empresa.id}
                variante="secundario"
                largo
                disabled={ocupado}
                onClick={() => tentar(() => entrarNaEmpresa(email.trim(), senha, empresa))}
              >
                {empresa.nome}
              </Botao>
            ))}
            <Botao variante="texto" onClick={() => setEmpresas(null)} disabled={ocupado}>Voltar</Botao>
          </div>
        ) : (
          <form className={css.formulario} onSubmit={aoEnviar}>
            <CampoTexto
              rotulo="E-mail" tipo="email" autoComplete="username" required
              value={email} onChange={(e) => setEmail(e.target.value)}
            />
            <CampoTexto
              rotulo="Senha" tipo="password" autoComplete="current-password" required
              value={senha} onChange={(e) => setSenha(e.target.value)}
            />
            <Botao variante="primario" tipo="submit" largo disabled={ocupado}>
              {ocupado ? 'Entrando…' : 'Entrar'}
            </Botao>
          </form>
        )}

        {!empresas && google && (
          <div className={css.google}>
            <BotaoGoogle buscarClientId={google.buscarClientId} aoReceberToken={entrarComGoogle} aoFalhar={falhaDoGoogle} />
          </div>
        )}

        {erro && <p className={css.erro} role="alert">{erro}</p>}
      </section>
    </main>
  )
}
