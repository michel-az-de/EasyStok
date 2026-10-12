import { chamarApi } from './cliente'

// Esteira do pedido (S46): o lote de papel lançado depois de uma queda de conexão.
export const lancarLotePapel = (linhas) =>
  chamarApi('/api/atendimento/esteira/lote', { metodo: 'POST', corpo: { linhas } })
