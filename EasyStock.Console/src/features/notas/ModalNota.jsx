import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import css from './ModalNota.module.css'

// #1839: a nota abre como um post-it pequeno logo acima do composer, sem cobrir a conversa. Esc fecha,
// Ctrl+Enter salva. Salva, ela fica fixa no topo do Resumo, junto das tags e do cadastro.
export function ModalNota({ nomeCliente, aoSalvar, aoFechar }) {
  const [texto, setTexto] = useState('')
  const [ocupado, setOcupado] = useState(false)
  const [erro, setErro] = useState(null)
  const salvando = useRef(false)
  const campo = useRef(null)
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
      } else aoFechar()
    } catch (falha) {
      setErro(falha.message || 'Não foi possível salvar a nota. Seu texto continua aqui.')
    } finally {
      salvando.current = false
      setOcupado(false)
    }
  }

  // Foco só ao abrir: `aoFechar` muda de identidade a cada render do App e não pode refazer o foco.
  useEffect(() => { campo.current?.focus() }, [])
  // Esc só fecha quando o foco está no post-it: o Esc de um modal ou da barra "/" não apaga a nota.
  const aoTeclar = (e) => { if (e.key === 'Escape' && !e.defaultPrevented && !salvando.current) { e.stopPropagation(); aoFechar() } }

  return (
    <dialog open className={css.postit} aria-label="Nota interna" onKeyDown={aoTeclar}>
      <p className={css.titulo}>Nota interna <span>· {nomeCliente} nunca vê</span></p>
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
          {ocupado ? 'Salvando…' : 'Fixar nota'}
        </Botao>
      </div>
    </dialog>
  )
}
