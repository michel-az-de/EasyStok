import css from './painelAssistente.module.css'

export function Retorno({ gravacao, qual }) {
  if (gravacao.salvando === qual) return <output className={css.nota}>Salvando…</output>
  if (gravacao.salvo === qual) return <output className={css.nota}>Salvo. O próximo turno do Assistente já usa.</output>
  if (gravacao.erro?.qual === qual) return <output role="alert" className={css.erro}>Não salvou: {gravacao.erro.mensagem}</output>
  return null
}
