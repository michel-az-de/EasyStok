import { useAtendimento } from '../aplicacao/contextos'
import { avisosDoDispositivo } from '../dominio/avisoDoDispositivo'
import css from './faixaDeFalha.module.css'

export function AvisosDoDispositivo() {
  const { som, audioBloqueado, permissaoNotificacao } = useAtendimento()
  const avisos = avisosDoDispositivo({ som, audioBloqueado, permissaoNotificacao })
  if (!avisos.length) return null
  return (
    <div className={`${css.faixa} ${css.empilhada}`}>
      {avisos.map((aviso) => <span key={aviso.id} role="status">{aviso.texto}</span>)}
    </div>
  )
}
