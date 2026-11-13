import { identidadeDaMontagem, montarFichaIa } from '../infra/api/montagemFichaApi'
import { validarMontagemFicha } from '../dominio/montagemFicha'

export { identidadeDaMontagem }

export async function organizarFicha(conversaId, orientacao, { sinal, identidadeEsperada = identidadeDaMontagem() } = {}) {
  const resposta = await montarFichaIa(conversaId, orientacao, { sinal, identidadeEsperada })
  if (sinal?.aborted) throw new DOMException('A organização foi cancelada.', 'AbortError')
  if (identidadeDaMontagem() !== identidadeEsperada) throw new Error('A sessão mudou. Organize a ficha de novo.')
  return { ...validarMontagemFicha(resposta, conversaId), identidadeSessao: identidadeEsperada }
}
