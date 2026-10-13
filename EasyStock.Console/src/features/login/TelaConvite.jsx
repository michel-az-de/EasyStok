// Primeiro acesso (#1753, N9): quem recebeu o convite escolhe a senha aqui. Abrir o link não gasta o
// convite; só o envio gasta. Depois de aceitar, a pessoa entra pelo login, como todo mundo.
import { useId, useState } from 'react'
import { aceitarConvite } from '../../aplicacao/usuarios'
import { Botao } from '../../componentes/Botao'
import { CampoTexto } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Marca } from '../../componentes/Marca'
import { regrasDaSenha, senhaValida } from '../../dominio/usuarios'
import css from './login.module.css'

export function TelaConvite({ token, aoConcluir }) {
  const id = useId()
  const [senha, setSenha] = useState('')
  const [visivel, setVisivel] = useState(false)
  const [estado, setEstado] = useState({ fase: token ? 'pedindo' : 'semToken', erro: null })
  const regras = regrasDaSenha(senha)
  const enviando = estado.fase === 'enviando'

  async function enviar(evento) {
    evento.preventDefault()
    if (enviando || !senhaValida(senha)) return
    setEstado({ fase: 'enviando', erro: null })
    try {
      await aceitarConvite(token, senha)
      setEstado({ fase: 'pronto', erro: null })
    } catch (falha) {
      // Convite usado, vencido ou trocado responde sempre a mesma coisa; a saída é pedir outro.
      const vencido = falha.status === 409 || falha.status === 404
      setEstado({ fase: vencido ? 'vencido' : 'pedindo', erro: vencido ? null : falha.message })
    }
  }

  if (estado.fase === 'pronto') {
    return (
      <main className={css.tela}>
        <section className={css.cartao} aria-labelledby={`${id}-titulo`}>
          <Marca />
          <div>
            <h1 id={`${id}-titulo`} className={css.titulo}>Senha criada</h1>
            <p className={css.apoio}>Agora é só entrar com o seu e-mail e a senha que você escolheu.</p>
          </div>
          <Botao variante="primario" largo onClick={aoConcluir}>Entrar</Botao>
        </section>
      </main>
    )
  }

  if (estado.fase === 'semToken' || estado.fase === 'vencido') {
    return (
      <main className={css.tela}>
        <section className={css.cartao} aria-labelledby={`${id}-titulo`}>
          <Marca />
          <div>
            <h1 id={`${id}-titulo`} className={css.titulo}>Este convite não vale mais</h1>
            <p className={css.apoio}>Ele já foi usado ou passou do prazo. Peça à responsável pela casa para reenviar o convite.</p>
          </div>
          <Botao largo onClick={aoConcluir}>Ir para o login</Botao>
        </section>
      </main>
    )
  }

  return (
    <main className={css.tela}>
      <section className={css.cartao} aria-labelledby={`${id}-titulo`}>
        <Marca />
        <div>
          <h1 id={`${id}-titulo`} className={css.titulo}>Bem-vinda à Casa da Baba</h1>
          <p className={css.apoio}>Escolha a senha que você vai usar para entrar.</p>
        </div>
        {estado.erro && <p className={css.erro} role="alert">{estado.erro}</p>}
        <form className={css.formulario} onSubmit={enviar} aria-busy={enviando}>
          <CampoTexto
            rotulo="Senha" tipo={visivel ? 'text' : 'password'} autoComplete="new-password" required
            value={senha} onChange={(evento) => setSenha(evento.target.value)} readOnly={enviando}
            aria-describedby={`${id}-regras`}
          />
          <label className={css.mostrar}>
            <input type="checkbox" checked={visivel} onChange={(evento) => setVisivel(evento.target.checked)} />
            Mostrar a senha
          </label>
          <ul id={`${id}-regras`} className={css.regras} aria-label="A senha precisa ter">
            {regras.map((regra) => (
              <li key={regra.id} data-ok={regra.ok || undefined}>
                {regra.ok ? <Icone nome="check" tamanho={16} /> : <span className={css.pendente} aria-hidden="true" />}
                {regra.rotulo}<span className="sr">{regra.ok ? ' (ok)' : ' (falta)'}</span>
              </li>
            ))}
          </ul>
          <Botao variante="primario" tipo="submit" largo disabled={enviando || !senhaValida(senha)}>
            {enviando ? 'Salvando…' : 'Criar senha'}
          </Botao>
        </form>
      </section>
    </main>
  )
}
