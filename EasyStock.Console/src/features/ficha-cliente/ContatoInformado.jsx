import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { telefoneDoE164 } from '../../dominio/formato'
import css from './cliente.module.css'

export const SELO_INFORMADO = 'Informado pelo visitante'

// #1430: o visitante do chat do site preencheu nome e contato antes de conversar. Não é cadastro
// (ninguém confirmou que o telefone é dele): a dona confere e confirma com um clique. Telefone
// novo vira cadastro; telefone que já é de alguém não liga sozinho, porque quem digita o número
// de outra pessoa não é ela: o cartão diz de quem é e a dona decide.
//
// `aoConfirmar(opcoes)` devolve o que a ação devolveu: nada no sucesso, `{ erro }` na recusa e
// `{ erro, jaCadastrado: { nome } }` quando o telefone já é de um cadastro. A Ficha remonta o
// cartão a cada conversa (`key`), então a espera e a pergunta não passam de uma para a outra.
export function ContatoInformado({ contato, aoConfirmar }) {
  // #1586: espera a resposta, trava o segundo toque e mostra a recusa junto do botão.
  const [salvando, setSalvando] = useState(false)
  const [erro, setErro] = useState(null)
  const [dono, setDono] = useState(null)
  const emVoo = useRef(false)

  async function confirmar(opcoes) {
    if (emVoo.current) return
    emVoo.current = true
    setSalvando(true)
    setErro(null)
    try {
      const resultado = await aoConfirmar(opcoes)
      if (resultado?.jaCadastrado) setDono({ nome: resultado.jaCadastrado.nome, pergunta: resultado.erro })
      else if (resultado?.erro) setErro(resultado.erro)
    } catch (falha) {
      setErro(falha.message || 'Não foi possível salvar o cadastro. Tente de novo.')
    } finally {
      emVoo.current = false
      setSalvando(false)
    }
  }

  function naoLigar() {
    setDono(null)
    setErro(null)
  }

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
      {dono ? (
        <>
          <p className={css.perguntaInformado} role="alert">{dono.pergunta}</p>
          {erro && <p className={css.erroEmLinha} role="alert">{erro}</p>}
          <Botao largo variante="primario" disabled={salvando} onClick={() => confirmar({ confirmarVinculoExistente: true })}>
            {salvando ? 'Ligando…' : dono.nome ? `Ligar ao cadastro de ${dono.nome}` : 'Ligar ao cadastro'}
          </Botao>
          <Botao largo disabled={salvando} onClick={naoLigar}>Não ligar</Botao>
        </>
      ) : (
        <>
          <p className={css.rodapeBloco}>
            Ainda não é cadastro. Se o telefone já for de um cliente, você decide se liga a conversa a ele.
          </p>
          {erro && <p className={css.erroEmLinha} role="alert">{erro}</p>}
          <Botao largo variante="primario" icone="user-plus" disabled={salvando} onClick={() => confirmar()}>
            {salvando ? 'Salvando…' : 'Confirmar cadastro'}
          </Botao>
        </>
      )}
    </section>
  )
}
