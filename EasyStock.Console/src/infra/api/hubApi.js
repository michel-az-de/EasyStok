import { chamarApi } from './cliente'

// Só resumos, sem mensagens, clientes ou alterações. Teto da inbox é explícito.
export async function lerInboxDoHub(sinal) {
  const vistas = new Map()
  for (let pagina = 1; pagina <= 10; pagina += 1) {
    const itens = await chamarApi(`/api/atendimento/conversas?limite=100&pagina=${pagina}`, { sinal })
    if (!Array.isArray(itens) || itens.some((item) => !item?.id || !['Automatica', 'Assumida', 'Encerrada'].includes(item.situacao) || typeof item.aguardaResposta !== 'boolean')) throw new Error('Não foi possível conferir as conversas.')
    for (const item of itens) vistas.set(item.id, item)
    if (itens.length < 100) return { itens: [...vistas.values()], parcial: false }
  }
  return { itens: [...vistas.values()], parcial: true }
}

export async function lerPedidosDoHub(sinal) {
  const itens = await chamarApi('/api/kds/pedidos', { sinal })
  if (!Array.isArray(itens) || itens.some((item) => !item?.id || typeof item.status !== 'string')) throw new Error('Não foi possível conferir os pedidos.')
  return { itens, parcial: itens.length >= 200 }
}

export async function lerCaixaDoHub(sinal) {
  const dados = await chamarApi('/api/caixa/dia', { sinal })
  if (!Number.isFinite(dados?.saldoEsperado) || typeof dados.aberto !== 'boolean') throw new Error('O saldo do caixa não foi informado.')
  return dados
}
