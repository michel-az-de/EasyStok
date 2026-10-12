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
  // #1354: callbacks em ref. O pai pode recriá-los a cada render sem o botão ser desenhado de novo.
  const aoReceberTokenAtual = useRef(aoReceberToken)
  const aoFalharAtual = useRef(aoFalhar)
  useEffect(() => {
    aoReceberTokenAtual.current = aoReceberToken
    aoFalharAtual.current = aoFalhar
  })

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
          callback: (resposta) => aoReceberTokenAtual.current(resposta.credential),
        })
        // #1354: o renderButton acrescenta; limpar antes garante um botão só.
        alvo.current.replaceChildren()
        window.google.accounts.id.renderButton(alvo.current, {
          theme: 'outline', size: 'large', text: 'signin_with', locale: 'pt-BR', width: 280,
        })
      })
      .catch((erro) => { if (vivo) aoFalharAtual.current(erro.message) })
    return () => { vivo = false }
  }, [clientId])

  if (!clientId) return null
  return <div ref={alvo} aria-label="Entrar com Google" />
}
