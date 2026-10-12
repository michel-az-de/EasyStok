import { useCallback, useEffect, useRef, useState } from 'react'
import { obterStatusWhatsApp } from '../infra/api/whatsappCoexistenciaApi'
import { statusDaFalha } from '../dominio/canais'

// Estado do canal WhatsApp para a tela Canais (#1447). `status`:
//   undefined = ainda não respondeu (ou falhou: ver `erro`)
//   null      = 404, atendimento não ligado para a loja
//   { semPermissao: true } = 403, rota só de administrador
//   objeto da API nos demais casos.
export function useStatusWhatsAppApi() {
  const [estado, setEstado] = useState({ carregando: true, status: undefined, erro: null })
  const vivo = useRef(true)

  const buscar = useCallback(() => obterStatusWhatsApp()
    .then((status) => ({ carregando: false, status, erro: null }))
    .catch((e) => {
      const status = statusDaFalha(e.status)
      return { carregando: false, status, erro: status === undefined ? e.message : null }
    })
    .then((novo) => { if (vivo.current) setEstado(novo) }), [])

  useEffect(() => {
    vivo.current = true
    buscar()
    return () => { vivo.current = false }
  }, [buscar])

  const recarregar = useCallback(() => {
    setEstado((atual) => ({ ...atual, carregando: true, erro: null }))
    buscar()
  }, [buscar])

  return { ...estado, recarregar }
}
