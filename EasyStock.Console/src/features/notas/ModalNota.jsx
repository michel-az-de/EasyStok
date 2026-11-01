import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import css from './ModalNota.module.css'

// #1839: a nota abre como um post-it pequeno logo acima do composer, sem cobrir a conversa. Esc fecha,
// Ctrl+Enter salva. Salva, ela fica fixa no topo do Resumo, junto das tags e do cadastro.
export function ModalNota({ nomeCliente, aoSalvar, aoFechar, aoComanda, aoGuardar, rascunho, precisaCadastrar = false, aoCadastrar,
  embutida = false, visivel = true, outraConversa = false, textoInicial = '' }) {
  const [texto, setTexto] = useState(rascunho?.texto ?? textoInicial)
  const [ocupado, setOcupado] = useState(false)
  const [erro, setErro] = useState(null)
  const salvando = useRef(false)
  const campo = useRef(null)
  const postit = useRef(null)
  // Antes do commit: o efeito de abertura ainda não transferiu o foco ao campo.
  const abridor = useRef(globalThis.document?.activeElement ?? null)
  const montada = useRef(true)
  const animacao = useRef(null)
  const [confirmada, setConfirmada] = useState(false)
  const contextoAtual = useRef({ visivel, outraConversa })
  contextoAtual.current = { visivel, outraConversa }
  const vazio = texto.trim().length === 0
  const emGravacao = ocupado || Boolean(rascunho?.gravando)
  const falha = rascunho?.erro ?? erro
  const posicao = useRef(rascunho ?? { texto: textoInicial, inicio: textoInicial.length, fim: textoInicial.length, rolagem: 0 })
  function guardarCampo(no = campo.current) {
    if (!no) return
    posicao.current = { texto: no.value, inicio: no.selectionStart, fim: no.selectionEnd, rolagem: no.scrollTop }
    aoGuardar?.(posicao.current)
  }

  function fechar() {
    if (!montada.current) return
    guardarCampo()
    const interno = () => postit.current?.contains?.(globalThis.document?.activeElement) === true
    const contexto = contextoAtual.current
    const devolver = montada.current && contexto.visivel && !contexto.outraConversa && interno()
    aoFechar()
    const destino = abridor.current
    // O callback pode focar outro controle. Não disputa esse foco nem o de uma ação externa.
    if (devolver && (interno() || document.activeElement === document.body)
      && destino?.isConnected && destino !== document.body && destino !== document.documentElement
      && typeof destino.focus === 'function' && !destino.disabled
      && destino.getClientRects().length > 0) destino.focus({ preventScroll: true })
  }

  async function salvar() {
    if (precisaCadastrar && !emGravacao) { guardarCampo(); aoCadastrar?.(); return }
    if (vazio || salvando.current || rascunho?.gravando) return
    salvando.current = true
    campo.current?.focus()
    setOcupado(true)
    setErro(null)
    try {
      if (await aoSalvar(texto.trim()) !== true) {
        setErro('Sem confirmação da nota. Seu texto continua aqui. Confira a ficha antes de tentar de novo.')
      } else {
        setConfirmada(true)
        // A persistência já foi confirmada. O movimento apenas indica onde a nota fica.
        if (!embutida && !contextoAtual.current.outraConversa && contextoAtual.current.visivel && !globalThis.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches && postit.current?.animate) {
          try {
            await new Promise((resolver) => requestAnimationFrame(resolver))
            if (contextoAtual.current.outraConversa || !contextoAtual.current.visivel || !postit.current) {
              fechar()
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
        fechar()
      }
    } catch (falha) {
      setErro(falha.message || 'Não foi possível salvar a nota. Seu texto continua aqui.')
    } finally {
      salvando.current = false
      setOcupado(false)
    }
  }

  // Mostrar o mesmo nó mantém desfazer. O espelho restaura seleção/rolagem também após remonte.
  useLayoutEffect(() => {
    if (!visivel || !campo.current) return
    const salva = { ...posicao.current }
    campo.current.focus({ preventScroll: true })
    campo.current.setSelectionRange(salva.inicio, salva.fim)
    campo.current.scrollTop = salva.rolagem
  }, [visivel])
  useEffect(() => {
    montada.current = true
    return () => { montada.current = false; animacao.current?.cancel() }
  }, [])
  // Esc só fecha quando o foco está no post-it: o Esc de um modal ou da barra "/" não apaga a nota.
  const aoTeclar = (e) => { if (e.key === 'Escape' && !e.defaultPrevented) { e.stopPropagation(); fechar() } }

  return (
    <dialog ref={postit} open={visivel} className={`${css.postit} ${embutida ? css.embutida : ''}`} aria-label={`Nota interna de ${nomeCliente}`} onKeyDown={aoTeclar}>
      <p className={css.titulo}>Nota interna <span>· só para a equipe</span></p>
      {outraConversa && <p role="status">Esta nota continua na ficha de {nomeCliente}.</p>}
      <CampoArea
        rotulo="Texto da nota"
        rotuloOculto
        rows={3}
        value={texto}
        ref={campo}
        readOnly={emGravacao}
        aria-busy={emGravacao}
        aria-label={`Texto da nota de ${nomeCliente}`}
        onChange={(e) => { setTexto(e.target.value); guardarCampo(e.target) }}
        onSelect={(e) => guardarCampo(e.target)}
        onScroll={(e) => guardarCampo(e.target)}
        onBlur={(e) => guardarCampo(e.target)}
        onKeyDown={(e) => { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); salvar() } }}
        placeholder="Ex.: prefere a massa mais al dente."
      />
      {precisaCadastrar ? <p className={css.requisito}>Para salvar a nota, cadastre o cliente.</p>
        : falha && <p className={css.erro} role="alert">{falha}</p>}
      <div className={css.acoes}>
        <Botao variante="texto" icone={embutida ? 'conversa' : undefined} onClick={fechar}>{embutida ? 'Conversa' : 'Recolher'}</Botao>
        {embutida && <Botao variante="texto" icone="shopping-cart" onClick={() => { guardarCampo(); aoComanda?.() }}>Comanda</Botao>}
        <Botao variante="primario" disabled={(!precisaCadastrar && vazio) || emGravacao} onClick={salvar}
          className={precisaCadastrar ? css.cadastrar : undefined}
          aria-label={precisaCadastrar ? 'Cadastrar para salvar' : undefined}>
          {precisaCadastrar ? (embutida ? <span>Cadastrar<br />para salvar</span> : 'Cadastrar para salvar')
            : confirmada ? 'Nota salva' : emGravacao ? 'Salvando…' : embutida ? 'Salvar' : 'Fixar nota'}
        </Botao>
      </div>
    </dialog>
  )
}
