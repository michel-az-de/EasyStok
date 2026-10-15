import { chamarApi } from './cliente'

// Consentimento do cliente por canal e finalidade (S38):
// `api/atendimento/clientes/{clienteId}/consentimentos`.
//
// Os checkboxes "Avisos" da Ficha falam do aviso do pedido, que é finalidade Transacional.
// Transacional sai por padrão e só para com revogação explícita; por isso o checkbox mostra
// `podeEnviar` (o que a API vai fazer de fato), e não só o que foi registrado.
const caminho = (clienteId) => `/api/atendimento/clientes/${clienteId}/consentimentos`

const CANAL_DO_AVISO = { email: 'Email', sms: 'Sms' }
const FINALIDADE_DO_AVISO = 'Transacional'

export function avisosDaApi(linhas) {
  const pode = (canal) => Boolean((linhas ?? []).find(
    (l) => l.canal === canal && l.finalidade === FINALIDADE_DO_AVISO,
  )?.podeEnviar)
  return { email: pode(CANAL_DO_AVISO.email), sms: pode(CANAL_DO_AVISO.sms) }
}

export const listarAvisos = async (clienteId) => avisosDaApi(await chamarApi(caminho(clienteId)))

export async function definirAviso(clienteId, aviso, ligado) {
  const linhas = await chamarApi(caminho(clienteId), {
    metodo: 'PUT',
    corpo: {
      itens: [{
        canal: CANAL_DO_AVISO[aviso],
        finalidade: FINALIDADE_DO_AVISO,
        situacao: ligado ? 'Concedido' : 'Revogado',
      }],
    },
  })
  return avisosDaApi(linhas)
}
