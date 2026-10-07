import { Botao } from '../../componentes/Botao'
import { telefoneDoE164 } from '../../dominio/formato'
import css from './cliente.module.css'

export const SELO_INFORMADO = 'Informado pelo visitante'

// #1430: o visitante do chat do site preencheu nome e contato antes de conversar. Não é cadastro
// (ninguém confirmou que o telefone é dele): a dona confere e confirma com um clique, e só então
// o EasyStok acha ou cria o cliente pelo telefone e liga a conversa.
export function ContatoInformado({ contato, aoConfirmar }) {
  return (
    <section className={css.contatoInformado} aria-label={SELO_INFORMADO}>
      <span className={css.seloInformado}>{SELO_INFORMADO}</span>
      <dl className={css.listaInformado}>
        {contato.nome && (
          <>
            <dt>Nome</dt>
            <dd>{contato.nome}</dd>
          </>
        )}
        <dt>Telefone</dt>
        <dd>{telefoneDoE164(contato.telefone)}</dd>
        {contato.email && (
          <>
            <dt>E-mail</dt>
            <dd>{contato.email}</dd>
          </>
        )}
      </dl>
      <p className={css.rodapeBloco}>
        Ainda não é cadastro. Se o telefone já for de um cliente, a conversa passa a ser dele.
      </p>
      <Botao largo variante="primario" icone="user-plus" onClick={aoConfirmar}>
        Confirmar cadastro
      </Botao>
    </section>
  )
}
