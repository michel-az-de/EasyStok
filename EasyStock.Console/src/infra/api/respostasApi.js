import { chamarApi } from './cliente'

// Biblioteca de respostas prontas e mensagens automáticas (S42).
const RESPOSTAS = '/api/atendimento/respostas-prontas'
const AUTOMACOES = '/api/atendimento/automacoes'

export const listarRespostasProntas = () => chamarApi(`${RESPOSTAS}?arquivadas=true`)
export const criarRespostaPronta = (corpo) => chamarApi(RESPOSTAS, { metodo: 'POST', corpo })
export const editarRespostaPronta = (id, corpo) => chamarApi(`${RESPOSTAS}/${id}`, { metodo: 'PUT', corpo })
export const arquivarRespostaPronta = (id, arquivada) =>
  chamarApi(`${RESPOSTAS}/${id}/arquivar?arquivada=${arquivada}`, { metodo: 'POST' })

export const listarAutomacoes = () => chamarApi(AUTOMACOES)
export const salvarAutomacao = (gatilho, { texto, ligada }) =>
  chamarApi(`${AUTOMACOES}/${gatilho}`, { metodo: 'PUT', corpo: { texto, ligada } })
