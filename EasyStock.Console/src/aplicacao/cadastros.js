import { useEffect, useState } from 'react'
import * as api from '../infra/api/cadastrosApi'
export { contextoCadastro, obterCadastro, listarCategoriasCadastro, salvarCadastro, ativarMetodoCadastro, previaModeloCadastro, aprovarModeloCadastro, definirEtiquetaPadrao, duplicarEtiquetaCadastro, editarEtiquetaCadastro } from '../infra/api/cadastrosApi'

// Preferências visuais da visita, sem entidades nem dados para persistir. Não contam como formulário sujo.
const contextosVisuais = new Map()
export function useContextoVisualCadastro(chave, identidade, inicial) {
  const escopo = `${identidade}:${chave}`
  const [valor, definir] = useState(() => contextosVisuais.get(escopo) ?? inicial)
  return [valor, proximo => {
    contextosVisuais.set(escopo, proximo)
    if (contextosVisuais.size > 100) contextosVisuais.delete(contextosVisuais.keys().next().value)
    definir(proximo)
  }]
}

export function useListaCadastros(tela, filtros, contexto, habilitada = true) {
  const [resultado, definir] = useState({ itens: [], total: 0, carregando: true, erro: null })
  const [revisao, recarregar] = useState(0)
  const { busca, pagina, filtro, relacionamento } = filtros
  useEffect(() => {
    if (!habilitada) { definir({ itens: [], total: 0, carregando: false, erro: null }); return undefined }
    const abortar = new AbortController()
    definir({ itens: [], total: 0, carregando: true, erro: null })
    const timer = setTimeout(() => api.listarCadastro(tela, { busca, pagina, filtro, relacionamento }, contexto, abortar.signal).then(
      r => { if (!abortar.signal.aborted) definir({ ...r, carregando: false, erro: null }) },
      e => { if (!abortar.signal.aborted) definir({ itens: [], total: 0, carregando: false, erro: e.message }) },
    ), busca ? 250 : 0)
    return () => { clearTimeout(timer); abortar.abort() }
  }, [tela, busca, pagina, filtro, relacionamento, contexto.empresaId, contexto.identidade, revisao, habilitada])
  return { ...resultado, recarregar: () => recarregar(r => r + 1) }
}
