import { chamarApi } from '../infra/api/cliente'
import { identidadeDaSessao, lerSessao, EVENTO_SESSAO_ALTERADA } from '../infra/api/sessao'
import { instante } from '../infra/api/traducaoConversas'

export { EVENTO_SESSAO_ALTERADA }
export const identidadeAtualDaMesa = () => identidadeDaSessao(lerSessao())
export const identidadeDaMesa = (sessao) => sessao?.empresa?.id && sessao?.usuario?.id ? identidadeDaSessao(sessao) : null
const chave = (escopo) => `cdb:mesa:v1:${encodeURIComponent(escopo)}`
export const idNotaNaMesa = (clienteId, notaId) => `${clienteId}:${notaId}`
const id = valor => typeof valor === 'string' && /^[\w-]{1,100}$/.test(valor)
const referencia = (r) => id(r?.clienteId) && id(r?.conversaId) && id(r?.notaId)
  ? { clienteId: r.clienteId, conversaId: r.conversaId, notaId: r.notaId } : null

export function lerReferenciasDaMesa(escopo, armazenamento) {
  if (!escopo) return []
  try {
    const dados = JSON.parse((armazenamento ?? globalThis.localStorage)?.getItem(chave(escopo)) ?? '[]')
    if (!Array.isArray(dados)) return []
    return [...new Map(dados.map(referencia).filter(Boolean).map(r => [idNotaNaMesa(r.clienteId, r.notaId), r])).values()]
  } catch { return [] }
}

export function guardarReferenciasDaMesa(escopo, referencias, armazenamento) {
  if (!escopo) return false
  try {
    const destino = armazenamento ?? globalThis.localStorage
    if (!destino) return false
    destino.setItem(chave(escopo), JSON.stringify(referencias.map(referencia).filter(Boolean)))
    return true
  } catch { return false }
}

export function notaDaMesa(nota) {
  const criadoEm = instante(nota.criadoEm)
  return { id: nota.id, texto: nota.texto, autor: nota.autor, criadoEm,
    em: new Date(criadoEm).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) }
}
const clienteValido = (clienteId) => {
  if (!/^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/i.test(clienteId)) throw new Error('Cliente inválido.')
  return `/api/clientes/${clienteId}/notas`
}
export async function obterNotaDaMesa(clienteId, notaId, opcoes = {}) {
  if (!/^[a-f\d-]{36}$/i.test(notaId)) throw new Error('Nota inválida.')
  const resultado = await chamarApi(`${clienteValido(clienteId)}/${notaId}`, { ...opcoes, prazoComCancelamento: true })
  if (resultado?.clienteId !== clienteId || resultado?.nota?.id !== notaId) throw new Error('A nota recebida não corresponde ao cliente.')
  return { clienteId, clienteNome: resultado.clienteNome, nota: notaDaMesa(resultado.nota) }
}
export async function paginaNotasDaMesa(clienteId, pagina, opcoes = {}) {
  const resultado = await chamarApi(`${clienteValido(clienteId)}/pagina?pagina=${pagina}&tamanho=50`, { ...opcoes, prazoComCancelamento: true })
  return { itens: (resultado.itens ?? []).map(notaDaMesa), temMais: resultado.temMais === true, proximaPagina: resultado.proximaPagina }
}
