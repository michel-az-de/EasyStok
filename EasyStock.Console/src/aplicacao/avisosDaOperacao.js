import { normalizarAvisos } from '../dominio/avisosDaOperacao'
import { obterAvisos, salvarAvisos } from '../infra/api/avisosApi'

// Avisos da operação (#2096) para a aba Avisos da Gestão: lê e grava no EasyStok, já no formato
// da tela. Não passa pelo reducer: só a aba lê e grava, como a configuração do atendimento.
export const carregarAvisos = () => obterAvisos().then(normalizarAvisos)

export async function gravarAvisos(avisos) {
  const salvo = await salvarAvisos(avisos)
  if (!salvo || typeof salvo !== 'object') throw new Error('O EasyStok não confirmou os avisos. Tente novamente; sua edição continua nesta tela.')
  return normalizarAvisos(salvo)
}
