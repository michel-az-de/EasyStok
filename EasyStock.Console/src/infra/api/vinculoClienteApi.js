import { chamarApi, ErroApi } from './cliente'
import { identidadeDaSessao, lerSessao } from './sessao'

const rota = (id) => `/api/atendimento/conversas/${encodeURIComponent(id)}`
const identidade = () => identidadeDaSessao(lerSessao())
async function noContexto(caminho, opcoes = {}) {
  const conta = identidade()
  const resultado = await chamarApi(caminho, { ...opcoes, identidadeEsperada: conta, prazoComCancelamento: true })
  if (identidade() !== conta) throw new ErroApi(401, 'SESSAO_ALTERADA', 'Sua sessão mudou. Abra a conversa novamente.')
  return resultado
}
export const obterVinculoCliente = (id, sinal) => noContexto(`${rota(id)}/vinculo-cliente`, { sinal })
// #2447: POST para o telefone não ficar na URL (log do proxy). A leitura não grava nada.
export const obterCandidatosCliente = (id, telefone, sinal) => noContexto(`${rota(id)}/candidatos-cliente`, { metodo: 'POST', corpo: { telefone }, sinal })
export const alterarVinculoCliente = (id, corpo) => noContexto(`${rota(id)}/vinculo-cliente`, { metodo: 'POST', corpo })
