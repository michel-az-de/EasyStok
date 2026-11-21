import { useEffect, useState } from 'react'
import { useAtendimento } from '../aplicacao/contextos'
import { gravarDispensa, lerDispensa } from '../aplicacao/dispensaDoAviso'
import { avisosDoDispositivo, resumoDoAviso } from '../dominio/avisoDoDispositivo'
import { destravarAudio } from '../infra/som'
import { Botao } from '../componentes/Botao'
import { Icone } from '../componentes/Icone'
import css from './faixaDeFalha.module.css'

export function AvisosDoDispositivo({ children, visitaSoNaMemoria = false }) {
  const { som, audioBloqueado, permissaoNotificacao, rascunhosSoNaMemoria, tentarGuardarRascunhos } = useAtendimento()
  const [dispensado, dispensar] = useState(() => lerDispensa())
  const resumo = resumoDoAviso(avisosDoDispositivo({ som, audioBloqueado, permissaoNotificacao }))
  // Aparelho voltou ao normal: esquece a dispensa, para um bloqueio futuro avisar de novo.
  useEffect(() => { if (!resumo && dispensado) { gravarDispensa(''); dispensar('') } }, [resumo, dispensado])
  const mostrarDispositivo = resumo !== null && dispensado !== resumo.chave
  const dispensarAviso = () => { gravarDispensa(resumo.chave); dispensar(resumo.chave) }
  return (
    <div className={css.quadro}>
      <div className={css.conteudo}>{children}</div>
      {(visitaSoNaMemoria || rascunhosSoNaMemoria) && <div className={`${css.faixa} ${css.empilhada}`}>
        {rascunhosSoNaMemoria && <><span role="alert">Não foi possível guardar o texto das conversas neste navegador. Ele continua nesta sessão e pode ser perdido ao recarregar ou fechar a aba.</span><Botao variante="texto" onClick={tentarGuardarRascunhos}>Tentar guardar o texto</Botao></>}
        {visitaSoNaMemoria && <span role="alert">Não foi possível guardar os pedidos neste navegador. Elas continuam nesta página e podem ser perdidas ao recarregar ou fechar a aba.</span>}
      </div>}
      {mostrarDispositivo && <div className={css.aparelho}>
        <Icone nome={resumo.icone} tamanho={20} />
        <span role="status" className={css.aparelhoTexto} title={resumo.detalhe}>{resumo.texto}</span>
        {resumo.acao && <Botao variante="secundario" className={css.aparelhoBotao} onClick={destravarAudio}>{resumo.rotuloDaAcao}</Botao>}
        <Botao variante="texto" className={css.aparelhoBotao} aria-label="Dispensar avisos do aparelho" onClick={dispensarAviso}>Dispensar</Botao>
      </div>}
    </div>
  )
}
