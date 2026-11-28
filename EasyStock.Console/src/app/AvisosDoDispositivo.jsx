import { useEffect, useId, useRef, useState } from 'react'
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
  const [passoAberto, abrirPasso] = useState(false)
  const idDoPasso = useId()
  const quadro = useRef(null)
  const conteudo = useRef(null)
  const botaoDoPasso = useRef(null)
  // #3416: o que fica abaixo do conteúdo (faixas e avisos) sobe a barra de abas do celular; o que é `fixed` no pé
  // da janela (o gatilho do Assistente) soma essa altura por `--pe-do-aparelho`, medida aqui.
  useEffect(() => {
    const no = quadro.current
    const area = conteudo.current
    if (!no || !area || typeof ResizeObserver === 'undefined') return undefined
    const medir = () => no.style.setProperty('--pe-do-aparelho', `${Math.max(0, Math.round(no.getBoundingClientRect().bottom - area.getBoundingClientRect().bottom))}px`)
    const observador = new ResizeObserver(medir)
    observador.observe(no)
    observador.observe(area)
    return () => observador.disconnect()
  }, [])
  const avisos = avisosDoDispositivo({ som, audioBloqueado, permissaoNotificacao })
    .filter(aviso => !(aviso.id === 'som' && somDispensado) && !(aviso.id === 'notificacao' && notificacaoDispensada))
  const resumo = resumoDoAviso(avisos)
  useEffect(() => {
    if (permissaoNotificacao !== 'denied' && notificacaoDispensada) { gravarDispensa(''); dispensarNotificacao(false) }
  }, [permissaoNotificacao, notificacaoDispensada])
  // #3043: o passo a passo abre sob demanda (toque e teclado); Esc fecha e devolve o foco ao botão.
  const fecharPasso = () => { abrirPasso(false); botaoDoPasso.current?.focus() }
  const dispensarAviso = () => {
    abrirPasso(false)
    for (const { id } of avisos) {
      if (id === 'som') dispensarSom(true)
      else { gravarDispensa('notificacao'); dispensarNotificacao(true) }
    }
    conteudo.current?.focus()
  }
  return (
    <div className={css.quadro} ref={quadro}>
      <div className={css.conteudo} ref={conteudo} tabIndex={-1}>{children}</div>
      {(visitaSoNaMemoria || rascunhosSoNaMemoria) && <div className={`${css.faixa} ${css.empilhada}`}>
        {rascunhosSoNaMemoria && <><span role="alert">Não foi possível guardar o texto das conversas neste navegador. Ele continua nesta sessão e pode ser perdido ao recarregar ou fechar a aba.</span><Botao variante="texto" onClick={tentarGuardarRascunhos}>Tentar guardar o texto</Botao></>}
        {visitaSoNaMemoria && <span role="alert">Não foi possível guardar os pedidos neste navegador. Elas continuam nesta página e podem ser perdidas ao recarregar ou fechar a aba.</span>}
      </div>}
      {resumo && <>
        <div className={css.aparelho}>
          <Icone nome={resumo.icone} tamanho={20} />
          <span role="status" className={css.aparelhoTexto}>
            <button type="button" ref={botaoDoPasso} className={css.aparelhoPasso} aria-expanded={passoAberto} aria-controls={passoAberto ? idDoPasso : undefined}
              onClick={() => abrirPasso(aberto => !aberto)} onKeyDown={event => { if (event.key === 'Escape' && passoAberto) { event.stopPropagation(); fecharPasso() } }}>
              <span className={css.aparelhoRotulo}>{resumo.texto}</span>
              <Icone nome={passoAberto ? 'chevron-up' : 'chevron-down'} tamanho={16} />
            </button>
          </span>
          {resumo.acao && <Botao variante="secundario" className={css.aparelhoBotao} onClick={destravarAudio}>{resumo.rotuloDaAcao}</Botao>}
          <Botao variante="texto" className={css.aparelhoBotao} aria-label="Dispensar avisos do aparelho" onClick={dispensarAviso}>Dispensar</Botao>
        </div>
        {passoAberto && <p id={idDoPasso} className={css.aparelhoPassoTexto}>{resumo.detalhe}</p>}
      </>}
    </div>
  )
}
