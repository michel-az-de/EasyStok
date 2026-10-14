import css from './SugestaoDeTexto.module.css'

export function SugestaoDeTexto({ sugestao }) {
  if (!sugestao.complemento) return null
  return <div className={css.sugestao}>
    <span className={css.texto} role="status"><small>Sugestão de IA</small>{sugestao.complemento}</span>
    <button type="button" className={css.usar} onClick={sugestao.aceitar}>Usar sugestão <kbd>Tab</kbd></button>
    <button type="button" className={css.fechar} aria-label="Dispensar sugestão" onClick={sugestao.dispensar}>×</button>
  </div>
}
