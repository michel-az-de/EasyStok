import { useState } from 'react'
import { useAtendimento } from '../aplicacao/contextos'
import { avisosDoDispositivo } from '../dominio/avisoDoDispositivo'
import { Botao } from '../componentes/Botao'
import css from './faixaDeFalha.module.css'

export function AvisosDoDispositivo({ children, visitaSoNaMemoria = false }) {
  const { som, audioBloqueado, permissaoNotificacao, rascunhosSoNaMemoria, tentarGuardarRascunhos } = useAtendimento()
  const [dispensados, dispensar] = useState('')
  const avisos = avisosDoDispositivo({ som, audioBloqueado, permissaoNotificacao })
  const chave = avisos.map(aviso => aviso.id).join('|')
  const mostrarDispositivo = avisos.length > 0 && dispensados !== chave
  return (
    <div className={css.quadro}>
      <div className={css.conteudo}>{children}</div>
      {(mostrarDispositivo || visitaSoNaMemoria || rascunhosSoNaMemoria) && <div className={`${css.faixa} ${css.empilhada}`}>
        {rascunhosSoNaMemoria && <><span role="alert">Não foi possível guardar o texto das conversas neste navegador. Ele continua nesta sessão e pode ser perdido ao recarregar ou fechar a aba.</span><Botao variante="texto" onClick={tentarGuardarRascunhos}>Tentar guardar o texto</Botao></>}
        {visitaSoNaMemoria && <span role="alert">Não foi possível guardar as comandas neste navegador. Elas continuam nesta página e podem ser perdidas ao recarregar ou fechar a aba.</span>}
        {mostrarDispositivo && <>{avisos.map((aviso) => <span key={aviso.id} role="status">{aviso.texto}</span>)}
          <Botao variante="texto" aria-label="Dispensar avisos do aparelho" onClick={() => dispensar(chave)}>Dispensar</Botao></>}
      </div>}
    </div>
  )
}
