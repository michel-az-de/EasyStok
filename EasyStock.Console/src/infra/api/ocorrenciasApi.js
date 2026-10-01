import { ErroApi, chamarApi } from './cliente'
import { instante } from './traducaoConversas'
import { ESTADOS_OCORRENCIA, ORIGENS_OCORRENCIA } from '../../dominio/ocorrencia'

// Ocorrência de pedido na API real (F09, S27): `api/ocorrencias`. A abertura automática
// (avaliação negativa, agente) e a resolução com reembolso são do EasyStok; aqui só se traduz.
const BASE = '/api/ocorrencias'

// Pedido pago fora do gateway: a API resolve e pede a devolução na mão.
export const CODIGO_REEMBOLSO_MANUAL = 'reembolso_manual_necessario'

const ms = (valor) => (valor ? new Date(instante(valor)).getTime() : null)

// A API não tem o passo "em apuração" do protótipo: aberta já é a que espera a dona decidir,
// então aparece com as ações de reembolso, sem estorno e bloqueio.
function estadoDaApi(o) {
  if (o.status !== 'resolvida') return ESTADOS_OCORRENCIA.EM_APURACAO
  return o.reembolsoValor != null
    ? ESTADOS_OCORRENCIA.ENCERRADA_COM_ESTORNO
    : ESTADOS_OCORRENCIA.ENCERRADA_SEM_ESTORNO
}

// Quem abriu: a dona, o automático (agente) ou o cliente pela avaliação negativa.
const AUTOR_DA_ORIGEM = { dona: 'dona', agente: 'sistema', avaliacao: 'cliente' }

export function ocorrenciaDaApi(o) {
  const resolvida = o.status === 'resolvida'
  const historico = [{ em: ms(o.criadaEm), autor: AUTOR_DA_ORIGEM[o.origem] ?? 'sistema', texto: o.relato }]
  if (resolvida && o.resolucao) historico.push({ em: ms(o.resolvidaEm), autor: 'dona', texto: o.resolucao })
  return {
    id: o.id,
    apiId: o.id,
    pedidoId: o.pedidoId,
    clienteId: o.clienteId,
    conversaId: o.conversaId ?? null,
    origem: ORIGENS_OCORRENCIA.RECLAMACAO,
    categoria: o.categoria,
    estado: estadoDaApi(o),
    abertaEm: ms(o.criadaEm),
    encerradaEm: ms(o.resolvidaEm),
    estorno: resolvida && o.reembolsoValor != null ? { valor: o.reembolsoValor, motivo: o.resolucao ?? '' } : null,
    notaInterna: resolvida && o.reembolsoValor == null && o.resolucao ? { texto: o.resolucao } : null,
    historico,
  }
}

export const listarOcorrencias = async () => ((await chamarApi(BASE)) ?? []).map(ocorrenciaDaApi)

export const abrirOcorrencia = async ({ pedidoId, categoria = 'outro', relato, conversaId = null }) =>
  ocorrenciaDaApi(await chamarApi(BASE, { metodo: 'POST', corpo: { pedidoId, categoria, relato, conversaId } }))

// Estorno recusado pelo gateway: 502 com o código do gateway. Nada foi gravado e a ocorrência
// continua aberta; a dona precisa saber disso, não de "o canal não respondeu".
export async function resolverOcorrencia(id, { resolucao, reembolsar, valor = null }) {
  try {
    return await chamarApi(`${BASE}/${id}/resolver`, { metodo: 'POST', corpo: { resolucao, reembolsar, valor } })
  } catch (erro) {
    if (erro.status === 502) {
      throw new ErroApi(502, erro.codigo,
        `O estorno não saiu: o meio de pagamento recusou a devolução (${erro.codigo}). `
        + 'A ocorrência continua aberta; tente de novo ou devolva na mão.',
        erro.dados)
    }
    throw erro
  }
}
