import { chamarApi } from './cliente'
import { identidadeDaSessao, lerSessao } from './sessao'

export const listarModelos = (atualizar = false) => chamarApi(`/api/atendimento/modelos?atualizar=${atualizar}`, { sinal: AbortSignal.timeout(25000) })
export const enviarModelo = (id, corpo) => chamarApi(`/api/atendimento/conversas/${id}/mensagens/modelo`, {
  metodo: 'POST', corpo, sinal: AbortSignal.timeout(45000),
})

// #1408: abre (ou reaproveita) a conversa do cliente cadastrado e envia o modelo; devolve { conversaId, mensagem }.
export const iniciarConversaComModelo = (corpo) => chamarApi('/api/atendimento/conversas/iniciar', {
  metodo: 'POST', corpo, sinal: AbortSignal.timeout(45000),
})
export const buscarClientes = (termo) =>
  chamarApi(`/api/clientes/buscar?termo=${encodeURIComponent(termo)}&max=5`, { sinal: AbortSignal.timeout(15000) })

const prefixo = (id) => `easystok.modelo:${identidadeDaSessao(lerSessao())}:${id}`
const memoria = new Map()
export function tentativaModelo(id, dados) {
  const chave = prefixo(id)
  let tentativa = memoria.get(chave)
  try { tentativa ??= JSON.parse(sessionStorage.getItem(chave)) } catch { /* mantém a proteção durante esta visita */ }
  const assinatura = JSON.stringify(dados)
  if (tentativa?.assinatura !== assinatura) tentativa = { assinatura, id: crypto.randomUUID() }
  memoria.set(chave, tentativa)
  try { sessionStorage.setItem(chave, JSON.stringify(tentativa)) } catch { /* armazenamento indisponível */ }
  return tentativa.id
}

export function limparTentativaModelo(id) {
  const chave = prefixo(id)
  memoria.delete(chave)
  try { sessionStorage.removeItem(chave) } catch { /* armazenamento indisponível */ }
}
