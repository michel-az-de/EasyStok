import { chamarApi } from './cliente'

// #2233: Pix direto pela chave da loja, sem Mercado Pago. A chave entra inteira no PUT e nunca volta:
// a leitura traz só o tipo e o final (`chaveMascarada`).
export const pixDiretoDisponivel = () => chamarApi('/api/pix-direto').then((r) => r?.disponivel === true)

const rota = (lojaId) => `/api/lojas/${encodeURIComponent(lojaId)}/pix`
export const obterPixDaLoja = (lojaId) => chamarApi(rota(lojaId))
export const gravarPixDaLoja = (lojaId, { tipo, chave, nomeRecebedor, cidade }) =>
  chamarApi(rota(lojaId), { metodo: 'PUT', corpo: { tipo, chave, nomeRecebedor, cidade } })
export const removerPixDaLoja = (lojaId) => chamarApi(rota(lojaId), { metodo: 'DELETE' })
