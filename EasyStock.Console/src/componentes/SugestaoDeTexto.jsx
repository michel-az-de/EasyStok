import { BotaoIcone } from './BotaoIcone'
import css from './SugestaoDeTexto.module.css'

export function SugestaoDeTexto({ sugestao, rotuloAcao = 'Usar sugestão', atalho = 'Tab', compacta = false }) {
  if (!sugestao.complemento) return null
  return <div className={`${css.sugestao} ${compacta ? css.compacta : ''}`}>
    <span className={css.texto} role="status"><small>Sugestão de IA</small>{sugestao.complemento}</span>
    <button type="button" className={css.usar} onClick={sugestao.aceitar}>{rotuloAcao}{atalho && <kbd>{atalho}</kbd>}</button>
    <BotaoIcone rotulo="Dispensar sugestão" icone="x" onClick={sugestao.dispensar} />
  </div>
}
