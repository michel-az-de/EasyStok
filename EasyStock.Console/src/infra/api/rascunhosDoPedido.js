// #2921: rascunho do "Novo pedido" na janela, um por cliente. Mesmo padrão de `rascunhosDaSessao.js`: sessionStorage,
// chave por empresa e usuário (rascunho de uma conta nunca aparece na outra), some com a aba, no "Sair"
// (`esquecerRascunhos` apaga tudo que começa com `easystok.rascunhos`) e ao Registrar. A conversa por baixo é detalhe
// interno: a chave é o `clienteId`.
const PREFIXO = 'easystok.rascunhos.pedido'

const chaveDe = (sessao) => `${PREFIXO}:${sessao?.empresa?.id ?? '-'}:${sessao?.usuario?.id ?? '-'}`

const lerTodos = (sessao) => {
  try {
    const salvo = JSON.parse(sessionStorage.getItem(chaveDe(sessao)) ?? '{}')
    return salvo && typeof salvo === 'object' && !Array.isArray(salvo) ? salvo : {}
  } catch { return {} }
}

const gravarTodos = (sessao, todos) => {
  try {
    if (Object.keys(todos).length === 0) sessionStorage.removeItem(chaveDe(sessao))
    else sessionStorage.setItem(chaveDe(sessao), JSON.stringify(todos))
    return true
  } catch { return false }
}

export const lerRascunhoDoPedido = (sessao, clienteId) => {
  const rascunho = clienteId ? lerTodos(sessao)[clienteId] : null
  return rascunho && Array.isArray(rascunho.itens) && rascunho.itens.length > 0 ? rascunho : null
}

export const temRascunhoDoPedido = (sessao, clienteId) => lerRascunhoDoPedido(sessao, clienteId) !== null

// Sem itens não há o que guardar: grava vazio apaga.
export function gravarRascunhoDoPedido(sessao, clienteId, rascunho) {
  if (!clienteId) return false
  const todos = lerTodos(sessao)
  if (!rascunho || !Array.isArray(rascunho.itens) || rascunho.itens.length === 0) delete todos[clienteId]
  else todos[clienteId] = rascunho
  return gravarTodos(sessao, todos)
}

export const apagarRascunhoDoPedido = (sessao, clienteId) => gravarRascunhoDoPedido(sessao, clienteId, null)
