import { Suspense, lazy, useEffect, useId, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Icone } from '../../../componentes/Icone'
import { ORIENTACOES } from '../../../dominio/desenhoDoFluxo'
import { LegendaDoFluxo } from './LegendaDoFluxo'
import css from './fluxoExpandido.module.css'

// Modo Expandir do desenho do fluxo. Mesmo CanvasDoFluxo, em tela cheia, num <dialog> aberto com showModal():
// a camada do navegador (top layer) fica acima do Modal da casa, que também é showModal().
const CanvasDoFluxo = lazy(() => import('./CanvasDoFluxo'))
const CELULAR = '(max-width: 599px)'

// Fecha o Expandir: se abriu uma entrada no histórico, volta nela (o popstate termina o fechamento).
// A entrada é dada como gasta na hora, para um segundo clique (Esc e depois Recolher) não voltar duas vezes.
// Esc com a Legenda aberta fecha só a Legenda; o Expandir continua.
function fecharLegenda(dialogo) {
  const botao = dialogo.querySelector('[data-legenda][aria-expanded="true"]')
  if (!botao) return false
  botao.click()
  botao.focus()
  return true
}

function sair(entrada, pendente, aoFechar) {
  if (entrada.current) {
    entrada.current = false
    pendente.current = true
    history.back()
  } else aoFechar()
}

export function FluxoExpandido({
  aoFechar, orientacao, aoTrocarOrientacao, desenho, dadosDoNo, foco, rotuloDoDesenho, ajuste = null, aviso = '', chaveDoAjuste = null, retornoDoFoco = null,
}) {
  const caixa = useRef(null)
  const recolher = useRef(null)
  const origem = useRef(null)
  const entrada = useRef(false)
  const pendente = useRef(false)
  const montado = useRef(false)
  const aoFecharRef = useRef(aoFechar)
  const idTitulo = useId()
  const [ajusteFechado, setAjusteFechado] = useState(chaveDoAjuste)
  const [celular, setCelular] = useState(() => Boolean(window.matchMedia?.(CELULAR).matches))

  useEffect(() => {
    const lista = window.matchMedia?.(CELULAR)
    if (!lista) return undefined
    const mudou = () => setCelular(lista.matches)
    lista.addEventListener('change', mudou)
    return () => lista.removeEventListener('change', mudou)
  }, [])

  useEffect(() => { aoFecharRef.current = aoFechar })

  useEffect(() => {
    const dialogo = caixa.current
    montado.current = true
    origem.current = document.activeElement
    if (!dialogo.open) dialogo.showModal()
    if (!entrada.current) {
      history.pushState({ fluxoExpandido: true }, '')
      entrada.current = true
    }
    recolher.current?.focus()

    const aoTecla = (evento) => {
      if (evento.key === 'Escape') {
        // #3011: Esc de um ModalConfirmar aberto por cima (irmão deste diálogo) é dele, não fecha o Expandir.
        if (!dialogo.contains(evento.target)) return
        evento.preventDefault()
        evento.stopPropagation()
        if (fecharLegenda(dialogo)) return
        sair(entrada, pendente, () => aoFecharRef.current())
      }
      // Tab: o showModal() já prende o foco no diálogo e pula o que está oculto.
    }
    // Voltar do navegador fecha o Expandir; o fechamento pelo botão chega aqui pelo history.back().
    const aoVoltar = () => {
      if (!entrada.current && !pendente.current) return
      entrada.current = false
      pendente.current = false
      aoFecharRef.current()
    }
    const aoCancelar = (evento) => {
      evento.preventDefault()
      if (fecharLegenda(dialogo)) return
      sair(entrada, pendente, () => aoFecharRef.current())
    }

    window.addEventListener('keydown', aoTecla, true)
    window.addEventListener('popstate', aoVoltar)
    dialogo.addEventListener('cancel', aoCancelar)
    return () => {
      window.removeEventListener('keydown', aoTecla, true)
      window.removeEventListener('popstate', aoVoltar)
      dialogo.removeEventListener('cancel', aoCancelar)
      if (dialogo.open) dialogo.close()
      // Se o modal fechou com o Expandir aberto, a entrada do histórico empilhada ficaria órfã: desempilha. O adiamento poupa o remonte do StrictMode.
      montado.current = false
      setTimeout(() => {
        if (montado.current || !entrada.current) return
        entrada.current = false
        history.back()
      }, 0)
      const volta = retornoDoFoco?.current ?? origem.current
      if (volta?.isConnected) volta.focus()
    }
  }, [])

  // Fechar o ajuste devolve o foco ao nó que estava escolhido (ou ao Recolher), não ao body.
  function fecharAjuste() {
    setAjusteFechado(chaveDoAjuste)
    const id = String(chaveDoAjuste ?? '').split('#')[0]
    requestAnimationFrame(() => {
      const no = id ? caixa.current?.querySelector(`[data-no="${id}"] button`) : null
      ;(no ?? recolher.current)?.focus()
    })
  }

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
        <LegendaDoFluxo />
        <button ref={recolher} type="button" className={css.recolher} onClick={() => sair(entrada, pendente, () => aoFecharRef.current())}>
          <Icone nome="reduzir" tamanho={20} /> Recolher
        </button>
        <p role="status" className="sr">{aviso}</p>
      </header>
      <div className={`${css.corpo} ${ajuste && !ajusteOculto ? '' : css.semAjuste}`}>
        <div className={css.canvas}>
          <Suspense fallback={<output className={css.nota}>Carregando o desenho…</output>}>
            <CanvasDoFluxo cheio interativo desenho={desenho} dadosDoNo={dadosDoNo} foco={foco} rotuloDoDesenho={rotuloDoDesenho}
              centrarNaSelecao={celular && ajuste && !ajusteOculto ? chaveDoAjuste : null} />
          </Suspense>
        </div>
        {ajuste && (
          <aside className={`${css.ajuste} ${ajusteOculto ? css.ajusteOculto : ''}`} aria-label="Ajuste do item escolhido">
            <span className={css.alca} aria-hidden="true" />
            <div className={css.ajusteRolagem}>{ajuste}</div>
            <button type="button" className={css.fecharAjuste} onClick={fecharAjuste}>Fechar ajuste</button>
          </aside>
        )}
      </div>
    </dialog>,
    document.body,
  )
}
