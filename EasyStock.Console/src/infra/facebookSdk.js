// SDK do Facebook para o Embedded Signup do WhatsApp (#1417). Carregado sob
// demanda, uma vez só: só quem abre a seção do WhatsApp baixa o script.
const URL_SDK = 'https://connect.facebook.net/en_US/sdk.js'

let carregamento = null

export function carregarSdkFacebook() {
  if (window.FB) return Promise.resolve(window.FB)
  if (carregamento) return carregamento
  carregamento = new Promise((resolver, rejeitar) => {
    const script = document.createElement('script')
    script.src = URL_SDK
    script.async = true
    script.defer = true
    script.crossOrigin = 'anonymous'
    script.onload = () => (window.FB ? resolver(window.FB) : rejeitar(new Error('O SDK do Facebook carregou sem o objeto FB.')))
    script.onerror = () => {
      carregamento = null
      script.remove()
      rejeitar(new Error('Não foi possível carregar o SDK do Facebook. Confira a conexão ou o bloqueador de anúncios.'))
    }
    document.body.appendChild(script)
  })
  return carregamento
}

export function iniciarSdkFacebook(FB, { appId, versao }) {
  FB.init({ appId, autoLogAppEvents: true, xfbml: false, version: versao })
}

// FB.login precisa sair direto do clique (senão o navegador bloqueia a janela).
// Resolve com o `code` ou `null` quando a pessoa fecha sem autorizar.
export function abrirEmbeddedSignup(FB, configId) {
  return new Promise((resolver) => {
    FB.login(
      (resposta) => resolver(resposta?.authResponse?.code ?? null),
      { config_id: configId, response_type: 'code', override_default_response_type: true, extras: {} },
    )
  })
}

// Mensagens da janela da Meta; devolve a função que para de escutar.
export function escutarMensagensDaJanela(aoReceber) {
  const ouvinte = (evento) => aoReceber(evento.origin, evento.data)
  window.addEventListener('message', ouvinte)
  return () => window.removeEventListener('message', ouvinte)
}
