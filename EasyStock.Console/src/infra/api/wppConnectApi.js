import { chamarApi } from './cliente'

const BASE = '/api/integracoes/whatsapp/wppconnect'
export const obterWppConnect = () => chamarApi(BASE)
export const conectarWppConnect = () => chamarApi(BASE, { metodo: 'POST' })
export const reconectarWppConnect = () => chamarApi(`${BASE}/reconectar`, { metodo: 'POST' })
export const desconectarWppConnect = () => chamarApi(BASE, { metodo: 'DELETE' })
