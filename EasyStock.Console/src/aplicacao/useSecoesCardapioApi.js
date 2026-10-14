import { useCallback, useEffect, useMemo, useState } from 'react'
import { criarGestaoSecoes, lerPreviaMigracao, lerSecoes } from './gestaoSecoes'

// Categorias do cardápio lidas da API (M1.3, #1483). Lista da tela; nada no reducer.
// #1803: quem edita também lê a prévia da conversão; se ela falhar, a tela segue sem o resumo.
export function useSecoesCardapioApi({ comPrevia = false } = {}) {
  const [carga, setCarga] = useState({ estado: 'carregando', secoes: [], previa: null, erro: null, aviso: null })

  const ler = useCallback(() => Promise.all([
    lerSecoes(),
    comPrevia ? lerPreviaMigracao().catch(() => null) : Promise.resolve(null),
  ]), [comPrevia])

  const recarregar = useCallback(() => ler()
    .then(([secoes, previa]) => setCarga((atual) => ({ ...atual, estado: 'ok', secoes, previa, erro: null })))
    .catch((erro) => setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message }))), [ler])

  useEffect(() => {
    let vivo = true
    ler()
      .then(([secoes, previa]) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'ok', secoes, previa, erro: null })) })
      .catch((erro) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message })) })
    return () => { vivo = false }
  }, [ler])

  const { secoes } = carga
  const acoes = useMemo(() => criarGestaoSecoes({
    obterSecoes: () => secoes,
    recarregar,
    aoErro: (mensagem) => setCarga((atual) => ({ ...atual, aviso: mensagem })),
    aoAviso: (mensagem) => setCarga((atual) => ({ ...atual, aviso: mensagem })),
  }), [secoes, recarregar])

  const fecharAviso = useCallback(() => setCarga((atual) => ({ ...atual, aviso: null })), [])
  return { ...carga, recarregar, fecharAviso, ...acoes }
}
