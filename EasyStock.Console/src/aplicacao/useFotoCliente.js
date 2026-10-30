import { useEffect, useState } from 'react'
import { obterFotoCliente } from '../infra/api/fotoClienteApi'

// Data URL fica só em memória: <img> não envia o JWT usado pela API.
// A fonte versionada impede mostrar a foto anterior ao trocar de cliente ou substituí-la.
export function useFotoCliente(fotoUrl) {
  const [foto, setFoto] = useState({ fonte: null, url: null, erro: null })
  useEffect(() => {
    if (!fotoUrl) return undefined
    const controle = new AbortController()
    const leitor = new FileReader()
    let ativo = true
    leitor.onload = () => { if (ativo) setFoto({ fonte: fotoUrl, url: leitor.result, erro: null }) }
    const falhar = erro => { if (ativo) setFoto({ fonte: fotoUrl, url: null, erro: erro?.message || 'Não foi possível carregar a foto.' }) }
    leitor.onerror = () => falhar(leitor.error)
    Promise.resolve().then(() => obterFotoCliente(fotoUrl, controle.signal)).then(arquivo => {
      if (ativo) leitor.readAsDataURL(arquivo)
    }).catch(falhar)
    return () => { ativo = false; controle.abort(); if (leitor.readyState === 1) leitor.abort() }
  }, [fotoUrl])
  if (!fotoUrl || foto.fonte !== fotoUrl) return { url: null, carregando: Boolean(fotoUrl), erro: null }
  return { url: foto.url, carregando: false, erro: foto.erro }
}
