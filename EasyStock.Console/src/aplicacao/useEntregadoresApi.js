import { useCallback, useEffect, useRef, useState } from 'react'
import { criarEntregador, desativarEntregador, listarEntregadores } from '../infra/api/entregasApi'

// Só o cadastro de entregadores (#2070), para quem mostra o cadastro fora das Entregas: sem pedidos,
// viagens nem tempo real. As ações têm o contrato das de `useEntregasApi` (resolvem true ou false) e
// relêem a lista depois de aceitas.
export function useEntregadoresApi() {
  const [entregadores, setEntregadores] = useState(null)
  const [erro, setErro] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  const vivo = useRef(true)

  const ler = useCallback(() => listarEntregadores()
    .then((lista) => { if (vivo.current) setEntregadores(lista ?? []) })
    .catch((e) => { if (vivo.current) setErro(`Os entregadores não carregaram: ${e.message}`) }), [])

  useEffect(() => {
    vivo.current = true
    ler()
    return () => { vivo.current = false }
  }, [ler])

  const executar = useCallback((chamada, falha) => {
    setOcupado(true)
    setErro(null)
    return chamada()
      .then(async () => { await ler(); return true })
      .catch((e) => { if (vivo.current) setErro(`${falha}: ${e.message}`); return false })
      .finally(() => { if (vivo.current) setOcupado(false) })
  }, [ler])

  const acoes = {
    criarEntregador: (corpo) => executar(() => criarEntregador(corpo), 'O entregador não foi cadastrado'),
    desativarEntregador: (id) => executar(() => desativarEntregador(id), 'O entregador não foi desativado'),
  }
  return { entregadores, erro, ocupado, acoes, tentarDeNovo: () => { setErro(null); return ler() } }
}
