import { Suspense, lazy, useEffect, useId, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { ORIENTACOES } from '../../../dominio/desenhoDoFluxo'
import css from './fluxoExpandido.module.css'

// Modo Expandir do desenho do fluxo. Mesmo CanvasDoFluxo, em tela cheia, num <dialog> aberto com showModal():
// a camada do navegador (top layer) fica acima do Modal da casa, que também é showModal().
const CanvasDoFluxo = lazy(() => import('./CanvasDoFluxo'))
const FOCAVEIS = 'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

// Fecha o Expandir: se abriu uma entrada no histórico, volta nela (o popstate termina o fechamento).
function sair(entrada, aoFechar) {
  if (entrada.current) history.back()
  else aoFechar()
}

export function FluxoExpandido({
  aoFechar, orientacao, aoTrocarOrientacao, desenho, dadosDoNo, foco, rotuloDoDesenho, ajuste = null, chaveDoAjuste = null,
}) {
  const caixa = useRef(null)
  const recolher = useRef(null)
  const origem = useRef(null)
  const entrada = useRef(false)
  const aoFecharRef = useRef(aoFechar)
  const idTitulo = useId()
  const [ajusteFechado, setAjusteFechado] = useState(chaveDoAjuste)

  useEffect(() => { aoFecharRef.current = aoFechar })

  useEffect(() => {
    const dialogo = caixa.current
    origem.current = document.activeElement
    if (!dialogo.open) dialogo.showModal()
    if (!entrada.current) {
      history.pushState({ fluxoExpandido: true }, '')
      entrada.current = true
    }
    recolher.current?.focus()

    const aoTecla = (evento) => {
      if (evento.key === 'Escape') {
        evento.preventDefault()
        evento.stopPropagation()
        sair(entrada, () => aoFecharRef.current())
        return
      }
      if (evento.key !== 'Tab') return
      const focaveis = [...dialogo.querySelectorAll(FOCAVEIS)]
      if (!focaveis.length) return
      const primeiro = focaveis[0]
      const ultimo = focaveis[focaveis.length - 1]
      const ativo = document.activeElement
      if (evento.shiftKey && (ativo === primeiro || !dialogo.contains(ativo))) {
        evento.preventDefault()
        ultimo.focus()
      } else if (!evento.shiftKey && (ativo === ultimo || !dialogo.contains(ativo))) {
        evento.preventDefault()
        primeiro.focus()
      }
    }
    // Voltar do navegador fecha o Expandir; o fechamento pelo botão chega aqui pelo history.back().
    const aoVoltar = () => {
      if (!entrada.current) return
      entrada.current = false
      aoFecharRef.current()
    }
    const aoCancelar = (evento) => {
      evento.preventDefault()
      sair(entrada, () => aoFecharRef.current())
    }

    window.addEventListener('keydown', aoTecla, true)
    window.addEventListener('popstate', aoVoltar)
    dialogo.addEventListener('cancel', aoCancelar)
    return () => {
      window.removeEventListener('keydown', aoTecla, true)
      window.removeEventListener('popstate', aoVoltar)
      dialogo.removeEventListener('cancel', aoCancelar)
      if (dialogo.open) dialogo.close()
      if (origem.current?.isConnected) origem.current.focus()
    }
  }, [])

  // Abre com o desenho inteiro: o ajuste só aparece quando alguém escolhe outro nó.
  const ajusteOculto = ajusteFechado === chaveDoAjuste

  return createPortal(
    <dialog ref={caixa} aria-modal="true" aria-labelledby={idTitulo} className={css.caixa}>
      <header className={css.barra}>
        <h2 id={idTitulo} className={css.titulo}>Fluxo do atendimento</h2>
        {aoTrocarOrientacao && (
          <div role="group" aria-label="Direção do desenho" className={css.seletor}>
            {[[ORIENTACOES.horizontal, 'Horizontal'], [ORIENTACOES.vertical, 'Vertical']].map(([valor, rotulo]) => (
              <button key={valor} type="button" className={orientacao === valor ? css.opcaoAtiva : css.opcao}
                aria-pressed={orientacao === valor} onClick={() => aoTrocarOrientacao(valor)}>{rotulo}</button>
            ))}
          </div>
        )}
        <button ref={recolher} type="button" className={css.recolher} onClick={() => sair(entrada, () => aoFecharRef.current())}>
          Recolher
        </button>
      </header>
      <div className={`${css.corpo} ${ajuste && !ajusteOculto ? '' : css.semAjuste}`}>
        <div className={css.canvas}>
          <Suspense fallback={<output className={css.nota}>Carregando o desenho…</output>}>
            <CanvasDoFluxo cheio interativo desenho={desenho} dadosDoNo={dadosDoNo} foco={foco} rotuloDoDesenho={rotuloDoDesenho} />
          </Suspense>
        </div>
        {ajuste && (
          <aside className={`${css.ajuste} ${ajusteOculto ? css.ajusteOculto : ''}`} aria-label="Ajuste do item escolhido">
            <div className={css.ajusteRolagem}>{ajuste}</div>
            <button type="button" className={css.fecharAjuste} onClick={() => setAjusteFechado(chaveDoAjuste)}>Fechar ajuste</button>
          </aside>
        )}
      </div>
    </dialog>,
    document.body,
  )
}
