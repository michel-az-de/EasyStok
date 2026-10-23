import { useEffect, useRef, useState } from 'react'
import { complementoDaResposta, podeAceitarComplemento, perguntaDeComplemento } from '../dominio/complementoDeTexto'
import { perguntarAssistente } from '../infra/api/assistenteApi'
import { EVENTO_SESSAO_ALTERADA, identidadeDaSessao, lerSessao } from '../infra/api/sessao'
import { combinarSinais } from '../infra/api/sinais'

let emVoo = null
let proximaChamada = 0

export function useComplementoDeTexto({ texto = '', contexto, conversaId, ativo, aoCompletar, limite = 2000 }) {
  const [focado, setFocado] = useState(false)
  const [identidade, setIdentidade] = useState(() => identidadeDaSessao(lerSessao()))
  const [resposta, setResposta] = useState(null)
  const [dispensada, setDispensada] = useState(null)
  const cache = useRef(new Map())
  const campo = useRef(null)
  const ultimaAceita = useRef(null)
  const chave = JSON.stringify([identidade, conversaId, contexto, texto, limite])
  const atual = useRef(chave)
  atual.current = chave

  useEffect(() => { setDispensada(null) }, [chave])

  useEffect(() => {
    const atualizar = () => { cache.current.clear(); setIdentidade(identidadeDaSessao(lerSessao())); setResposta(null) }
    window.addEventListener(EVENTO_SESSAO_ALTERADA, atualizar)
    window.addEventListener('storage', atualizar)
    return () => { window.removeEventListener(EVENTO_SESSAO_ALTERADA, atualizar); window.removeEventListener('storage', atualizar) }
  }, [])

  useEffect(() => {
    if (!ativo || !focado || !lerSessao() || dispensada === chave || ultimaAceita.current === chave
      || texto.trim().length < 16 || texto.length > 1000 || texto.startsWith('/')) return
    const controle = new AbortController()
    const guardada = cache.current.get(chave)
    const aplicar = (complemento) => {
      if (!controle.signal.aborted && atual.current === chave && identidadeDaSessao(lerSessao()) === identidade)
        setResposta({ chave, complemento })
    }
    const temporizador = setTimeout(async () => {
      if (guardada && guardada.ate > Date.now()) { aplicar(guardada.complemento); return }
      if (emVoo) return
      emVoo = controle
      proximaChamada = Date.now() + 8000
      try {
        // A cozinha pede só redação; sem conversa, a API não oferece ferramentas de ações no cliente.
        const resposta = await perguntarAssistente(perguntaDeComplemento(texto, contexto), contexto === 'cozinha' ? null : conversaId, {
          sinal: combinarSinais([controle.signal, AbortSignal.timeout(15000)]), identidadeEsperada: identidade,
        })
        // Ações da IA nunca são executadas por um complemento de escrita.
        const complemento = complementoDaResposta(texto, resposta, limite)
        if (!controle.signal.aborted && atual.current === chave && identidadeDaSessao(lerSessao()) === identidade) {
          if (cache.current.size >= 10) cache.current.delete(cache.current.keys().next().value)
          cache.current.set(chave, { complemento, ate: Date.now() + 60000 })
          aplicar(complemento)
        }
      } catch { /* Sugestão opcional: a escrita e o envio continuam disponíveis. */ }
      finally { if (emVoo === controle) emVoo = null }
    }, guardada && guardada.ate > Date.now() ? 0 : Math.max(1200, proximaChamada - Date.now()))
    return () => { clearTimeout(temporizador); controle.abort() }
  }, [ativo, focado, texto, contexto, conversaId, identidade, chave, dispensada, limite])

  const complemento = ativo && resposta?.chave === chave && dispensada !== chave ? resposta.complemento : ''
  const aceitar = () => {
    if (!complemento || identidadeDaSessao(lerSessao()) !== identidade) return false
    setDispensada(chave)
    const completo = texto + complemento
    ultimaAceita.current = JSON.stringify([identidade, conversaId, contexto, completo, limite])
    aoCompletar(completo)
    requestAnimationFrame(() => {
      if (atual.current === ultimaAceita.current && identidadeDaSessao(lerSessao()) === identidade && campo.current?.isConnected) {
        campo.current.focus(); campo.current.setSelectionRange(completo.length, completo.length)
      }
    })
    return true
  }
  return {
    complemento, aceitar,
    dispensar: () => setDispensada(chave),
    aoFocar: (evento) => { campo.current = evento.currentTarget; setFocado(true) }, aoSair: () => setFocado(false),
    aoTeclar: (evento) => {
      if (evento.isComposing || evento.nativeEvent?.isComposing || evento.keyCode === 229) return false
      if (evento.key === 'Escape' && complemento) { evento.preventDefault(); setDispensada(chave); return true }
      if (!podeAceitarComplemento(evento, texto, complemento)) return false
      evento.preventDefault()
      return aceitar()
    },
  }
}
