import { useCallback, useEffect, useRef, useState } from 'react'
import { desativar, listar, salvarChave, testar } from '../infra/api/integracoesApi'

// Integrações da loja no modo API (F16, #1246). A aba usa sem intervalo (carrega ao abrir);
// a faixa do modo API usa com intervalo, para a dona ver a integração parada sem abrir a Gestão.
// Cada ação relê a lista depois, e o erro da ação volta para quem chamou mostrar no cartão.
// A rota é só de Admin: no primeiro 403 a consulta periódica para (quem não é Admin não vê a faixa).
export function useIntegracoesApi({ ativo = true, intervaloMs = null } = {}) {
  const [lista, setLista] = useState(null)
  const [erro, setErro] = useState(null)
  const [ocupado, setOcupado] = useState(null)
  const proibido = useRef(false)

  const recarregar = useCallback(() => {
    if (proibido.current) return Promise.resolve()
    return listar()
      .then((l) => { setLista(l ?? []); setErro(null) })
      .catch((e) => {
        if (e.status === 403) proibido.current = true
        setErro(e.message)
      })
  }, [])

  useEffect(() => {
    if (!ativo) return undefined
    recarregar()
    if (!intervaloMs) return undefined
    const relogio = setInterval(recarregar, intervaloMs)
    return () => clearInterval(relogio)
  }, [ativo, intervaloMs, recarregar])

  const executar = useCallback(async (provider, chamada) => {
    setOcupado(provider)
    try {
      return await chamada()
    } finally {
      setOcupado(null)
      await recarregar()
    }
  }, [recarregar])

  return {
    lista,
    erro,
    ocupado,
    salvar: (provider, campos, ambiente) => executar(provider, () => salvarChave(provider, campos, ambiente)),
    testar: (provider) => executar(provider, () => testar(provider)),
    desativar: (provider) => executar(provider, () => desativar(provider)),
  }
}
