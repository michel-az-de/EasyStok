import { useEffect, useState } from 'react'
import { obterFotoCliente } from '../infra/api/fotoClienteApi'

// #3254: cache por fotoUrl (versionada: foto nova, chave nova). Data URL não precisa de revogação,
// então a linha que remonta não baixa de novo e nada em uso é invalidado. Limite pequeno, o mais antigo sai.
const GUARDADAS = new Map()
const LIMITE = 200
function guardar(fonte, url) {
  GUARDADAS.delete(fonte)
  GUARDADAS.set(fonte, url)
  if (GUARDADAS.size > LIMITE) GUARDADAS.delete(GUARDADAS.keys().next().value)
}

// Data URL fica só em memória: <img> não envia o JWT usado pela API.
// A fonte versionada impede mostrar a foto anterior ao trocar de cliente ou substituí-la.
export function useFotoCliente(fotoUrl) {
  const [foto, setFoto] = useState({ fonte: null, url: null, erro: null })
  useEffect(() => {
    if (!fotoUrl || GUARDADAS.has(fotoUrl)) return undefined // já guardada: a linha que remonta não baixa de novo
    const controle = new AbortController()
    const leitor = new FileReader()
    let ativo = true
    leitor.onload = () => { if (ativo) { guardar(fotoUrl, leitor.result); setFoto({ fonte: fotoUrl, url: leitor.result, erro: null }) } }
    const falhar = erro => { if (ativo) setFoto({ fonte: fotoUrl, url: null, erro: erro?.message || 'Não foi possível carregar a foto.' }) }
    leitor.onerror = () => falhar(leitor.error)
    Promise.resolve().then(() => obterFotoCliente(fotoUrl, controle.signal)).then(arquivo => {
      if (ativo) leitor.readAsDataURL(arquivo)
    }).catch(falhar)
    return () => { ativo = false; controle.abort(); if (leitor.readyState === 1) leitor.abort() }
  }, [fotoUrl])
  if (fotoUrl && GUARDADAS.has(fotoUrl)) return { url: GUARDADAS.get(fotoUrl), carregando: false, erro: null }
  if (!fotoUrl || foto.fonte !== fotoUrl) return { url: null, carregando: Boolean(fotoUrl), erro: null }
  return { url: foto.url, carregando: false, erro: foto.erro }
}
