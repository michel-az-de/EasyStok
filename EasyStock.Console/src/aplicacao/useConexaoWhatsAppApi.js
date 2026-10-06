import { useCallback, useEffect, useRef, useState } from 'react'
import * as api from '../infra/api/whatsappCoexistenciaApi'
import {
  abrirEmbeddedSignup, carregarSdkFacebook, escutarMensagensDaJanela, iniciarSdkFacebook,
} from '../infra/facebookSdk'
import { PRAZO_CODE_MS, lerEventoEmbeddedSignup, textoDoCancelamento } from '../dominio/embeddedSignup'

// Conectar o WhatsApp Business da loja por coexistência (#1417, Embedded Signup v4).
// O `code` vem do callback do FB.login e os ids (WABA e número) do postMessage da
// Meta; quando os dois chegam, a API recebe tudo em até 30 s do `code`.
//
// estado: carregando | indisponivel | pronto | aguardando | conectando | conectado | erro
export function useConexaoWhatsAppApi() {
  const [config, setConfig] = useState(null)
  const [estado, setEstado] = useState('carregando')
  const [erro, setErro] = useState(null)
  const [resultado, setResultado] = useState(null)
  const sdk = useRef(null)
  const fluxo = useRef(null) // { code, ids, prazo, pararDeEscutar, encerrado }

  const encerrarFluxo = useCallback(() => {
    const atual = fluxo.current
    if (!atual) return
    atual.encerrado = true
    clearTimeout(atual.prazo)
    atual.pararDeEscutar?.()
    fluxo.current = null
  }, [])

  const falhar = useCallback((mensagem) => {
    encerrarFluxo()
    setErro(mensagem)
    setEstado('erro')
  }, [encerrarFluxo])

  // Config e SDK ao abrir a seção: o clique precisa chamar o FB.login direto.
  useEffect(() => {
    let vivo = true
    api.obterConfigCoexistencia()
      .then((c) => {
        if (!vivo) return
        setConfig(c)
        if (!c?.habilitado) { setEstado('indisponivel'); return }
        return carregarSdkFacebook().then((FB) => {
          if (!vivo) return
          iniciarSdkFacebook(FB, { appId: c.appId, versao: c.graphVersion })
          sdk.current = FB
          setEstado('pronto')
        })
      })
      .catch((e) => { if (vivo) { setErro(`A conexão do WhatsApp não carregou: ${e.message}`); setEstado('erro') } })
    return () => { vivo = false; encerrarFluxo() }
  }, [encerrarFluxo])

  const tentarConcluir = useCallback(() => {
    const atual = fluxo.current
    if (!atual || atual.encerrado || !atual.code || !atual.ids) return
    const { code, ids } = atual
    encerrarFluxo()
    setEstado('conectando')
    api.conectarCoexistencia({ code, ...ids })
      .then((r) => { setResultado(r); setEstado('conectado') })
      .catch((e) => { setErro(`A conexão não foi concluída: ${e.message}`); setEstado('erro') })
  }, [encerrarFluxo])

  const conectar = useCallback(() => {
    const FB = sdk.current
    if (!FB || !config?.habilitado) return
    encerrarFluxo()
    setErro(null)
    setResultado(null)
    setEstado('aguardando')

    const atual = { code: null, ids: null, prazo: null, encerrado: false }
    fluxo.current = atual
    atual.pararDeEscutar = escutarMensagensDaJanela((origem, dados) => {
      const evento = lerEventoEmbeddedSignup(origem, dados)
      if (!evento || atual.encerrado) return
      if (evento.tipo === 'cancelado') { falhar(textoDoCancelamento(evento)); return }
      atual.ids = { wabaId: evento.wabaId, phoneNumberId: evento.phoneNumberId }
      tentarConcluir()
    })

    abrirEmbeddedSignup(FB, config.configId).then((code) => {
      if (atual.encerrado) return
      if (!code) { falhar('A janela da Meta foi fechada sem autorizar a conexão.'); return }
      atual.code = code
      atual.prazo = setTimeout(
        () => falhar('A Meta não confirmou a conta e o número a tempo. Abra o fluxo de novo.'), PRAZO_CODE_MS)
      tentarConcluir()
    })
  }, [config, encerrarFluxo, falhar, tentarConcluir])

  return { config, estado, erro, resultado, conectar }
}
