import { useEffect, useRef, useState } from 'react'
import * as api from '../infra/api/cadastrosApi'
import { contextoVisualEsquecidoAoSair } from '../dominio/cadastros'
export { adicionarEnderecoCliente, adicionarNotaCadastro, adicionarTagCadastro, adicionarTelefoneCliente, alternarAtividadeCadastro, obterRelacionamentoCliente, removerTagCadastro, removerEnderecoCliente, removerTelefoneCliente, atualizarCategoriaCadastro, buscarClientesParecidos, contextoCadastro, criarCategoriaCadastro, excluirCategoriaCadastro, obterCadastro, listarCategoriasCadastro, salvarCadastro, ativarMetodoCadastro, previaModeloCadastro, variaveisDoModeloCadastro, aprovarModeloCadastro, definirEtiquetaPadrao, duplicarEtiquetaCadastro, editarEtiquetaCadastro } from '../infra/api/cadastrosApi'

// Preferências visuais da visita, sem entidades nem dados para persistir. Não contam como formulário sujo.
const contextosVisuais = new Map()
export function useContextoVisualCadastro(chave, identidade, inicial) {
  const escopo = `${identidade}:${chave}`
  const [valor, definir] = useState(() => contextosVisuais.get(escopo)?.valor ?? inicial)
  return [valor, proximo => {
    contextosVisuais.set(escopo, { chave, valor: proximo })
    if (contextosVisuais.size > 100) contextosVisuais.delete(contextosVisuais.keys().next().value)
    definir(proximo)
  }]
}

// #2870: quem sai do módulo Cadastros volta para listas limpas. Chamado na troca de rota, depois que a tela saiu.
export function useEsquecerCadastrosAoSair(moduloId) {
  const anterior = useRef(moduloId)
  useEffect(() => {
    if (anterior.current === 'cadastros' && moduloId !== 'cadastros') {
      for (const [escopo, { chave }] of contextosVisuais) if (contextoVisualEsquecidoAoSair(chave)) contextosVisuais.delete(escopo)
    }
    anterior.current = moduloId
  }, [moduloId])
}

// #2495 (S4): a árvore de categorias de produto. Ao recarregar, a lista atual fica na tela até a nova chegar.
export function useCategoriasCadastro(contexto) {
  const [resultado, definir] = useState({ itens: [], carregando: true, erro: null, revisao: 0 })
  const { empresaId, identidade } = contexto
  const { revisao } = resultado
  useEffect(() => {
    const abortar = new AbortController()
    api.listarCategoriasCadastro({ empresaId, identidade }, abortar.signal).then(
      itens => { if (!abortar.signal.aborted) definir(r => ({ ...r, itens: itens ?? [], carregando: false, erro: null })) },
      e => { if (!abortar.signal.aborted) definir(r => ({ ...r, carregando: false, erro: e.message })) },
    )
    return () => abortar.abort()
  }, [empresaId, identidade, revisao])
  return { ...resultado, recarregar: () => definir(r => ({ ...r, carregando: true, erro: null, revisao: r.revisao + 1 })) }
}

// #2734: relacionamento do cliente no editor (notas, tags, bloqueio e conversas do dossiê). Sem cliente, não lê.
// Reler mantém o que está na tela; a releitura que falha depois de uma gravação não apaga a lista local nem vira alerta.
export function useRelacionamentoCliente(clienteId, contexto) {
  const { empresaId, identidade } = contexto
  const chave = `${clienteId}:${empresaId}:${identidade}`
  const [estado, definir] = useState({ chave: null, dados: null, erro: null, lida: -1 })
  const [revisao, reler] = useState(0)
  useEffect(() => {
    if (!clienteId) return undefined
    const abortar = new AbortController()
    api.obterRelacionamentoCliente(clienteId, { empresaId, identidade }, abortar.signal).then(
      dados => { if (!abortar.signal.aborted) definir({ chave, dados, erro: null, lida: revisao }) },
      e => { if (!abortar.signal.aborted) definir(atual => ({ chave, dados: atual.chave === chave ? atual.dados : null, erro: e, lida: revisao })) },
    )
    return () => abortar.abort()
  }, [chave, clienteId, empresaId, identidade, revisao])
  const atual = estado.chave === chave
  const dados = atual ? estado.dados : null
  const lida = atual && estado.lida === revisao
  return {
    dados, carregando: Boolean(clienteId) && !lida, erro: lida && !dados ? estado.erro : null,
    recarregar: () => reler(r => r + 1),
    aplicar: mudanca => definir(a => (a.chave === chave && a.dados ? { ...a, dados: mudanca(a.dados) } : a)),
  }
}

export function useListaCadastros(tela, filtros, contexto, habilitada = true) {
  const [resultado, definir] = useState({ itens: [], total: 0, carregando: true, erro: null })
  const [revisao, recarregar] = useState(0)
  const { busca, pagina, filtro, relacionamento, ordem = '' } = filtros
  useEffect(() => {
    if (!habilitada) { definir({ itens: [], total: 0, carregando: false, erro: null }); return undefined }
    const abortar = new AbortController()
    // #2734 (A12): a lista que está na tela fica até a nova chegar; zerar a cada busca fazia a tabela piscar.
    definir(r => ({ ...r, carregando: true, erro: null }))
    const timer = setTimeout(() => api.listarCadastro(tela, { busca, pagina, filtro, relacionamento, ordem }, contexto, abortar.signal).then(
      r => { if (!abortar.signal.aborted) definir({ ...r, carregando: false, erro: null }) },
      e => { if (!abortar.signal.aborted) definir({ itens: [], total: 0, carregando: false, erro: e.message }) },
    ), busca ? 250 : 0)
    return () => { clearTimeout(timer); abortar.abort() }
  }, [tela, busca, pagina, filtro, relacionamento, ordem, contexto.empresaId, contexto.identidade, revisao, habilitada])
  return { ...resultado, recarregar: () => recarregar(r => r + 1) }
}
