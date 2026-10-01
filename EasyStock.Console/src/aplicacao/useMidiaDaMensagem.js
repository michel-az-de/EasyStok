import { useEffect, useState } from 'react'
import { baixarMidia } from '../infra/api/conversasApi'

// Mídia recebida na conversa (modo API, #1287): o arquivo mora no storage privado do
// EasyStok e só sai pelo endpoint autenticado, então vem como Blob e vira um endereço
// local do navegador, liberado quando o balão sai da tela.
export function useMidiaDaMensagem(midia) {
  const conversaId = midia?.conversaId ?? null
  const mensagemId = midia?.mensagemId ?? null
  const [estado, setEstado] = useState({ url: null, erro: null })

  useEffect(() => {
    if (!conversaId || !mensagemId) return undefined
    let vivo = true
    let url = null
    baixarMidia(conversaId, mensagemId)
      .then((blob) => {
        if (!vivo || !blob) return
        url = URL.createObjectURL(blob)
        setEstado({ url, erro: null })
      })
      .catch((erro) => { if (vivo) setEstado({ url: null, erro: erro.message }) })
    return () => {
      vivo = false
      if (url) URL.revokeObjectURL(url)
    }
  }, [conversaId, mensagemId])

  return estado
}
