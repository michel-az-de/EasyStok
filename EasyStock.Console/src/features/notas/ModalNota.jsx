import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import css from './ModalNota.module.css'

// #1839: a nota abre como um post-it pequeno logo acima do composer, sem cobrir a conversa. Esc fecha,
// Ctrl+Enter salva. Salva, ela fica fixa no topo do Resumo, junto das tags e do cadastro.
export function ModalNota({ nomeCliente, aoSalvar, aoFechar, visivel = true, outraConversa = false }) {
  const [texto, setTexto] = useState('')
  const [ocupado, setOcupado] = useState(false)
  const [erro, setErro] = useState(null)
  const salvando = useRef(false)
  const campo = useRef(null)
  const postit = useRef(null)
  const animacao = useRef(null)
  const [confirmada, setConfirmada] = useState(false)
  const contextoAtual = useRef({ visivel, outraConversa })
  contextoAtual.current = { visivel, outraConversa }
  const vazio = texto.trim().length === 0

  async function salvar() {
    if (vazio || salvando.current) return
    salvando.current = true
    campo.current?.focus()
    setOcupado(true)
    setErro(null)
    try {
      if (await aoSalvar(texto.trim()) === false) {
        setErro('Sem confirmação da nota. Seu texto continua aqui. Confira a ficha antes de tentar de novo.')
      } else {
        setConfirmada(true)
        // A persistência já foi confirmada. O movimento apenas indica onde a nota fica.
        if (!contextoAtual.current.outraConversa && contextoAtual.current.visivel && !globalThis.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches && postit.current?.animate) {
          try {
            await new Promise((resolver) => requestAnimationFrame(resolver))
            if (contextoAtual.current.outraConversa || !contextoAtual.current.visivel || !postit.current) {
              aoFechar()
              return
            }
            const origem = postit.current.getBoundingClientRect()
            const destino = document.querySelector('[data-destino-nota]')?.getBoundingClientRect()
            const estilo = getComputedStyle(postit.current)
            const x = destino?.width ? destino.left + destino.width / 2 : Math.min(innerWidth - 24, origem.right)
            const y = destino?.height ? destino.top + Math.min(destino.height / 2, 32) : Math.max(24, origem.top - 120)
            const inicio = estilo.transform === 'none' ? '' : estilo.transform
            animacao.current = postit.current.animate([
              { transform: inicio, opacity: 1 },
              { transform: `${inicio} translate(${x - origem.left - origem.width / 2}px, ${y - origem.top - origem.height / 2}px) scale(.12)`, opacity: 0 },
            ], { duration: parseFloat(estilo.getPropertyValue('--dur-media')), easing: estilo.getPropertyValue('--ease-saida').trim(), fill: 'forwards' })
            await animacao.current.finished.catch(() => {})
          } catch { /* O retorno confirmado não depende da animação. */ }
        }
        aoFechar()
      }
    } catch (falha) {
      setErro(falha.message || 'Não foi possível salvar a nota. Seu texto continua aqui.')
    } finally {
      salvando.current = false
      setOcupado(false)
    }
  }

  // Foco só ao abrir: `aoFechar` muda de identidade a cada render do App e não pode refazer o foco.
  useEffect(() => { if (visivel) campo.current?.focus() }, [visivel])
  useEffect(() => () => animacao.current?.cancel(), [])
  // Esc só fecha quando o foco está no post-it: o Esc de um modal ou da barra "/" não apaga a nota.
  const aoTeclar = (e) => { if (e.key === 'Escape' && !e.defaultPrevented && !salvando.current) { e.stopPropagation(); aoFechar() } }

  return (
    <dialog ref={postit} open={visivel} className={css.postit} aria-label="Nota interna" onKeyDown={aoTeclar}>
      <p className={css.titulo}>Nota interna <span>· só para a equipe</span></p>
      {outraConversa && <p role="status">Esta nota continua na ficha de {nomeCliente}.</p>}
      <CampoArea
        rotulo="Texto da nota"
        rotuloOculto
        rows={3}
        value={texto}
        ref={campo}
        readOnly={ocupado}
        aria-busy={ocupado}
        onChange={(e) => setTexto(e.target.value)}
        onKeyDown={(e) => { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); salvar() } }}
        placeholder="Ex.: prefere a massa mais al dente."
      />
      {erro && <p className={css.erro} role="alert">{erro}</p>}
      <div className={css.acoes}>
        <Botao variante="texto" disabled={ocupado} onClick={aoFechar}>Cancelar</Botao>
        <Botao variante="primario" disabled={vazio || ocupado} onClick={salvar}>
          {confirmada ? 'Nota salva' : ocupado ? 'Salvando…' : 'Fixar nota'}
        </Botao>
      </div>
    </dialog>
  )
}
