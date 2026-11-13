import { chamarApi } from './cliente'
import { requisicoesDaBusca, resultadoDaBusca } from '../../dominio/buscaNaCasa'

export async function buscarNaCasa(termo, acesso, contexto, sinal) {
  const consultas = requisicoesDaBusca(termo, acesso, contexto.empresaId)
  const respostas = await Promise.allSettled(consultas.map(async consulta => {
    const itens = await chamarApi(consulta.url, { sinal, identidadeEsperada: contexto.identidade, prazoComCancelamento: true })
    if (!Array.isArray(itens)) throw new Error('A busca não recebeu uma lista válida.')
    return itens.map(item => resultadoDaBusca(consulta.tipo, item))
  }))
  if (sinal?.aborted) throw new DOMException('Busca cancelada.', 'AbortError')
  return {
    resultados: respostas.flatMap(r => r.status === 'fulfilled' ? r.value : []),
    indisponiveis: respostas.flatMap((r, i) => r.status === 'rejected' ? [{ tipo: consultas[i].tipo, mensagem: r.reason?.message || 'Não foi possível consultar.' }] : []),
  }
}

export const obterPedidoDaBusca = (id, contexto, sinal) => chamarApi(`/api/pedidos/${encodeURIComponent(id)}?empresaId=${contexto.empresaId}`, {
  sinal, identidadeEsperada: contexto.identidade, prazoComCancelamento: true,
})
