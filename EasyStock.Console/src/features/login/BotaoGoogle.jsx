import { useEffect, useRef, useState } from 'react'

const SCRIPT_GOOGLE = 'https://accounts.google.com/gsi/client'

// Carrega o Google Identity Services uma vez por página.
function carregarScriptGoogle() {
  if (window.google?.accounts?.id) return Promise.resolve()
  return new Promise((resolver, rejeitar) => {
    const existente = document.querySelector(`script[src="${SCRIPT_GOOGLE}"]`)
    const script = existente ?? Object.assign(document.createElement('script'), { src: SCRIPT_GOOGLE, async: true })
    script.addEventListener('load', () => resolver())
    script.addEventListener('error', () => rejeitar(new Error('Não deu para carregar o login do Google.')))
    if (!existente) document.head.appendChild(script)
  })
}

// "Entrar com Google" (#1324). Some quando a API não tem ClientId configurado.
export function BotaoGoogle({ buscarClientId, aoReceberToken, aoFalhar }) {
  const alvo = useRef(null)
  const [clientId, setClientId] = useState(null)

  useEffect(() => {
    let vivo = true
    buscarClientId().then((id) => { if (vivo) setClientId(id) })
    return () => { vivo = false }
  }, [buscarClientId])

  useEffect(() => {
    if (!clientId) return
    let vivo = true
    carregarScriptGoogle()
      .then(() => {
        if (!vivo || !alvo.current) return
        window.google.accounts.id.initialize({
          client_id: clientId,
          callback: (resposta) => aoReceberToken(resposta.credential),
        })
        window.google.accounts.id.renderButton(alvo.current, {
          theme: 'outline', size: 'large', text: 'signin_with', locale: 'pt-BR', width: 280,
        })
      })
      .catch((erro) => aoFalhar(erro.message))
    return () => { vivo = false }
  }, [clientId, aoReceberToken, aoFalhar])

  if (!clientId) return null
  return <div ref={alvo} aria-label="Entrar com Google" />
}
