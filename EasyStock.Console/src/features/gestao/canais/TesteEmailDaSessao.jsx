import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { useTesteEmailDaSessao } from '../../../aplicacao/useTesteEmailDaSessao'
import css from './TesteEmailDaSessao.module.css'

const TEXTOS = {
  Pendente: 'Aguardando envio.',
  EmEnvio: 'Envio em andamento.',
  Enviado: 'O provedor aceitou o envio. Confira a caixa de entrada e o spam.',
  Simulado: 'Foi uma simulação. Nenhum e-mail foi enviado.',
  Falhado: 'O envio falhou.',
  Cancelado: 'O teste foi cancelado.',
  Suprimido: 'O envio foi bloqueado.',
  Indeterminado: 'O provedor não confirmou o resultado. Confira seu e-mail antes de enviar outro teste.',
  Expirado: 'O prazo do teste venceu sem envio.',
}

export function TesteEmailDaSessao({ email }) {
  const { motivo, setMotivo, eventoId, consulta, enviando, consultando, erroEnvio, erroConsulta,
    enviar, consultarNovamente, aguardando } = useTesteEmailDaSessao()
  return (
    <details className={css.ferramenta}>
      <summary>Homologação: testar meu e-mail</summary>
      <div className={css.conteudo}>
        <p>O teste vai para o e-mail confirmado da sua própria conta{email ? `: ${email}` : '.'}</p>
        <form onSubmit={enviar} className={css.formulario}>
          <CampoTexto rotulo="Motivo do teste" minLength={10} maxLength={500} required value={motivo}
            onChange={e => setMotivo(e.target.value)} disabled={enviando || consultando} />
          <Botao tipo="submit" variante="texto" disabled={enviando || consultando || aguardando || motivo.trim().length < 10}>
            {enviando ? 'Solicitando teste…' : 'Enviar teste para meu e-mail'}
          </Botao>
        </form>
        {erroEnvio && <p className={css.erro} role="alert">Não foi possível confirmar a solicitação. {erroEnvio} Confira seu e-mail antes de solicitar outro teste.</p>}
        {eventoId && <div className={css.resultado} aria-live="polite">
          {(!consulta || (consulta.eventoStatus === 'Pendente' && consulta.mensagens.length === 0)) && <p>Teste solicitado. Aguardando processamento.</p>}
          {consulta?.mensagens.map((mensagem, indice) => <p key={`${mensagem.canal}:${indice}`}>
            <strong>{mensagem.canal}: </strong>{TEXTOS[mensagem.status] ?? `Estado: ${mensagem.status}.`}
            {mensagem.provider && ` Provedor: ${mensagem.provider}.`}{mensagem.erro && ` ${mensagem.erro}`}
          </p>)}
          {consulta && consulta.mensagens.length === 0 && consulta.eventoStatus !== 'Pendente'
            && <p>Nenhum e-mail foi enfileirado. Estado do teste: {consulta.eventoStatus}.{consulta.eventoErro && ` ${consulta.eventoErro}`}</p>}
          {erroConsulta && <p className={css.erro} role="alert">Não foi possível consultar o resultado. {erroConsulta}</p>}
          {!consultando && <Botao variante="texto" onClick={consultarNovamente}>Consultar resultado novamente</Botao>}
        </div>}
      </div>
    </details>
  )
}
