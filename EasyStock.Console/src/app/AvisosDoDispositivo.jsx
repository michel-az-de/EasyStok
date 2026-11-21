import { useEffect, useRef, useState } from 'react'
import { useAtendimento } from '../aplicacao/contextos'
import { gravarDispensa, lerDispensa } from '../aplicacao/dispensaDoAviso'
import { avisosDoDispositivo, resumoDoAviso } from '../dominio/avisoDoDispositivo'
import { destravarAudio } from '../infra/som'
import { Botao } from '../componentes/Botao'
import { Icone } from '../componentes/Icone'
import css from './faixaDeFalha.module.css'

export function AvisosDoDispositivo({ children, visitaSoNaMemoria = false }) {
  const { som, audioBloqueado, permissaoNotificacao, rascunhosSoNaMemoria, tentarGuardarRascunhos } = useAtendimento()
  // #3005: só a dispensa da notificação negada fica no aparelho (o som se resolve no primeiro toque, então a dele
  // vale só nesta visita, em memória). Ela some quando a permissão deixa de ser 'denied'.
  const [notificacaoDispensada, dispensarNotificacao] = useState(() => lerDispensa() === 'notificacao')
  const [somDispensado, dispensarSom] = useState(false)
  const conteudo = useRef(null)
  const avisos = avisosDoDispositivo({ som, audioBloqueado, permissaoNotificacao })
    .filter(aviso => !(aviso.id === 'som' && somDispensado) && !(aviso.id === 'notificacao' && notificacaoDispensada))
  const resumo = resumoDoAviso(avisos)
  useEffect(() => {
    if (permissaoNotificacao !== 'denied' && notificacaoDispensada) { gravarDispensa(''); dispensarNotificacao(false) }
  }, [permissaoNotificacao, notificacaoDispensada])
  const dispensarAviso = () => {
    for (const { id } of avisos) {
      if (id === 'som') dispensarSom(true)
      else { gravarDispensa('notificacao'); dispensarNotificacao(true) }
    }
    conteudo.current?.focus()
  }
  return (
    <div className={css.quadro}>
      <div className={css.conteudo} ref={conteudo} tabIndex={-1}>{children}</div>
      {(visitaSoNaMemoria || rascunhosSoNaMemoria) && <div className={`${css.faixa} ${css.empilhada}`}>
        {rascunhosSoNaMemoria && <><span role="alert">Não foi possível guardar o texto das conversas neste navegador. Ele continua nesta sessão e pode ser perdido ao recarregar ou fechar a aba.</span><Botao variante="texto" onClick={tentarGuardarRascunhos}>Tentar guardar o texto</Botao></>}
        {visitaSoNaMemoria && <span role="alert">Não foi possível guardar os pedidos neste navegador. Elas continuam nesta página e podem ser perdidas ao recarregar ou fechar a aba.</span>}
      </div>}
      {resumo && <div className={css.aparelho}>
        <Icone nome={resumo.icone} tamanho={20} />
        <span role="status" className={css.aparelhoTexto} title={resumo.detalhe} aria-description={resumo.detalhe}>{resumo.texto}</span>
        {resumo.acao && <Botao variante="secundario" className={css.aparelhoBotao} onClick={destravarAudio}>{resumo.rotuloDaAcao}</Botao>}
        <Botao variante="texto" className={css.aparelhoBotao} aria-label="Dispensar avisos do aparelho" onClick={dispensarAviso}>Dispensar</Botao>
      </div>}
    </div>
  )
}
