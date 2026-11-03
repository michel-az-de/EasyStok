// Primeiro acesso (#1753, N9): quem recebeu o convite escolhe a senha aqui. Abrir o link não gasta o
// convite; só o envio gasta. Depois de aceitar, a pessoa entra pelo login, como todo mundo.
// #2070: ou entra direto com a conta Google do e-mail convidado. A tela diz quem entrou; no "Continuar", o App larga o
// convite e vai para o hall (`aoEntrarPeloGoogle`); no "Não sou eu", a conta sai e o convite continua.
import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { aceitarConvite } from '../../aplicacao/usuarios'
import { Botao } from '../../componentes/Botao'
import { CampoTexto } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Marca } from '../../componentes/Marca'
import {
  fraseDaFalhaDoGoogleNoConvite, regrasDaSenha, SAIDAS_DO_CONVITE, saidaDoGoogleNoConvite, senhaValida,
} from '../../dominio/usuarios'
import { BotaoGoogle } from './BotaoGoogle'
import css from './login.module.css'

export function TelaConvite({ token, google, aoConcluir, aoEntrarPeloGoogle }) {
  const id = useId()
  const [senha, setSenha] = useState('')
  const [visivel, setVisivel] = useState(false)
  const [estado, setEstado] = useState({ fase: token ? 'pedindo' : 'semToken', erro: null, quem: null })
  // O texto só fala do Google quando o botão existe (a API sem ClientId não tem o botão).
  const [comGoogle, setComGoogle] = useState(false)
  const [entrandoGoogle, setEntrandoGoogle] = useState(false)
  const regras = regrasDaSenha(senha)
  const enviando = estado.fase === 'enviando'
  const titulo = useRef(null)
  const entrou = estado.fase === 'entrou'
  const saida = entrou ? saidaDoGoogleNoConvite(estado.quem) : SAIDAS_DO_CONVITE[estado.fase] ?? null
  const tela = saida ? estado.fase : 'pedindo'

  const buscarClientId = useCallback(async () => {
    const clientId = await google.buscarClientId()
    setComGoogle(Boolean(clientId))
    return clientId
  }, [google])
  // Com a senha no ar, o toque no Google não abre uma segunda entrada.
  const entrarComGoogle = useCallback(async (idToken) => {
    if (enviando) return
    setEstado((atual) => ({ ...atual, erro: null }))
    setEntrandoGoogle(true)
    try {
      // A conta Google deste aparelho pode ser de outra pessoa: quem entrou aparece antes de o convite sair.
      const quem = await google.entrar(idToken)
      setEstado({ fase: 'entrou', erro: null, quem })
    } catch (falha) {
      setEstado((atual) => ({ ...atual, erro: fraseDaFalhaDoGoogleNoConvite(falha) }))
    } finally {
      setEntrandoGoogle(false)
    }
  }, [google, enviando])
  const falhaDoGoogle = useCallback((mensagem) => setEstado((atual) => ({ ...atual, erro: mensagem })), [])
  // "Não sou eu": a conta sai deste aparelho e do servidor; o convite continua, para entrar com a conta certa.
  const sairDaConta = () => {
    google.sair().catch(() => { /* já saiu deste aparelho; sem rede, o servidor só a encerra no vencimento */ })
    setEstado({ fase: 'pedindo', erro: null, quem: null })
  }

  // A tela trocou inteira: o foco vai para o título novo, senão o teclado e o leitor de tela ficam no botão que sumiu.
  const telaAnterior = useRef(tela)
  useEffect(() => {
    if (telaAnterior.current === tela) return
    telaAnterior.current = tela
    titulo.current?.focus()
  }, [tela])

  async function enviar(evento) {
    evento.preventDefault()
    if (enviando || entrandoGoogle || !senhaValida(senha)) return
    setEstado({ fase: 'enviando', erro: null })
    try {
      await aceitarConvite(token, senha)
      setEstado({ fase: 'pronto', erro: null })
    } catch (falha) {
      // Convite usado, vencido ou trocado responde sempre a mesma coisa; a saída é pedir outro.
      const vencido = falha.status === 409 || falha.status === 404
      // 429: a API travou as tentativas por uns minutos. Insistir não adianta, então o formulário sai da tela.
      const limitado = falha.status === 429
      setEstado({ fase: vencido ? 'vencido' : limitado ? 'limitado' : 'pedindo', erro: vencido || limitado ? null : falha.message })
    }
  }

  // Toda saída tem a mesma forma: o que houve, o que fazer, e um só botão. Depois do Google, um segundo: "Não sou eu".
  if (saida) {
    return (
      <main className={css.tela}>
        <section className={css.cartao} aria-labelledby={`${id}-titulo`}>
          <Marca />
          <div>
            <h1 id={`${id}-titulo`} ref={titulo} tabIndex={-1} className={css.titulo}>{saida.titulo}</h1>
            <p className={css.apoio}>{saida.texto}</p>
          </div>
          {entrou ? (
            <>
              <Botao variante="primario" largo onClick={aoEntrarPeloGoogle}>Continuar</Botao>
              <Botao variante="texto" largo onClick={sairDaConta}>Não sou eu, sair</Botao>
            </>
          ) : <Botao variante="primario" largo onClick={aoConcluir}>Entrar</Botao>}
        </section>
      </main>
    )
  }

  return (
    <main className={css.tela}>
      <section className={css.cartao} aria-labelledby={`${id}-titulo`}>
        <Marca />
        <div>
          <h1 id={`${id}-titulo`} ref={titulo} tabIndex={-1} className={css.titulo}>Boas-vindas à Casa da Baba</h1>
          <p className={css.apoio}>
            {comGoogle
              ? 'Entre com a conta Google do e-mail que recebeu o convite, ou crie uma senha.'
              : 'Escolha a senha que você vai usar para entrar.'}
          </p>
        </div>
        {estado.erro && <p className={css.erro} role="alert">{estado.erro}</p>}
        {google && (
          <div className={css.googleDoConvite} aria-busy={entrandoGoogle}>
            <BotaoGoogle buscarClientId={buscarClientId} aoReceberToken={entrarComGoogle} aoFalhar={falhaDoGoogle} opcoes={google.opcoes} />
            {comGoogle && <output className={css.ou}>{entrandoGoogle ? 'Entrando com o Google…' : 'ou crie uma senha'}</output>}
          </div>
        )}
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
          <Botao variante="primario" tipo="submit" largo disabled={enviando || entrandoGoogle || !senhaValida(senha)}>
            {enviando ? 'Salvando…' : 'Criar senha'}
          </Botao>
        </form>
      </section>
    </main>
  )
}
