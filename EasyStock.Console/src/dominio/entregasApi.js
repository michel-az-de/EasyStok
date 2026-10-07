// Entregas no modo API (F04). Regras puras sobre o que a API devolve: pedidos
// do KDS (S19, com endereço e aprovação da S12/S14) e viagens da S44. Nada de
// rede aqui; quem busca é `infra/api/entregasApi.js`.

// Status que a gaveta pede ao KDS: aprovação manual, pronto e em rota.
export const STATUS_KDS_ENTREGAS = 'aguardando_aprovacao_baba,pronto,saiu_para_entrega'

const ROTULO_SITUACAO = { Montando: 'Montando', EmRota: 'Em rota', Concluida: 'Concluída', Desfeita: 'Desfeita' }
export const rotuloSituacaoViagem = (situacao) => ROTULO_SITUACAO[situacao] ?? situacao

export const TIPOS_ENTREGADOR = [
  { valor: 'Motoboy', rotulo: 'Motoboy' },
  { valor: 'Plataforma', rotulo: 'Plataforma' },
  { valor: 'Proprio', rotulo: 'Próprio' },
]

export const EMPRESAS_ENTREGADOR = [
  { valor: 'Propria', rotulo: 'Própria' },
  { valor: 'NoveNove', rotulo: '99' },
  { valor: 'Lalamove', rotulo: 'Lalamove' },
  { valor: 'Ifood', rotulo: 'iFood Entregas' },
  { valor: 'Outra', rotulo: 'Outra' },
]

export const DIAS_DA_SEMANA = ['Domingo', 'Segunda', 'Terça', 'Quarta', 'Quinta', 'Sexta', 'Sábado']

const MOTIVOS_APROVACAO = { fora_de_area: 'Entrega fora da área' }
export const motivoDaAprovacao = (motivo) => (motivo ? MOTIVOS_APROVACAO[motivo] ?? motivo : 'Aprovação manual')

// Recarrega no `ready` (a API não tem replay) e a cada evento de pedido do SSE (S18).
export const recarregaEntregasCom = (evento) => evento === 'ready' || evento.startsWith('pedido.')

// #1434: sem pedidos e com erro, a primeira carga falhou; não fica "Carregando" para sempre.
export const situacaoDaLista = (pedidos, erro) => {
  if (pedidos !== null) return 'pronta'
  return erro ? 'falhou' : 'carregando'
}

// Divide em painéis. Pedido pronto que já está numa viagem montando não aparece
// de novo solto; viagens concluídas e desfeitas saem da tela do dia.
export function paineisDeEntregas(pedidos, viagens) {
  const lista = pedidos ?? []
  const vs = viagens ?? []
  const montando = vs.filter((v) => v.situacao === 'Montando')
  const emViagem = new Set(montando.flatMap((v) => v.paradas.map((p) => p.pedidoId)))
  return {
    aprovacao: lista.filter((p) => p.status === 'aguardando_aprovacao_baba'),
    prontos: lista.filter((p) => p.status === 'pronto' && !emViagem.has(p.id)),
    montando,
    emRota: vs.filter((v) => v.situacao === 'EmRota'),
  }
}

// RN-32: sem entregador resolvido, nenhum aviso sai. A API recusa; a tela avisa antes.
export function motivoDeSaida(viagem) {
  if (!viagem.entregadorId) return 'Escolha o entregador antes de sair.'
  if (viagem.paradas.length === 0) return 'Ponha ao menos um pedido na viagem.'
  return null
}

const hora = (h) => (h.length === 5 ? `${h}:00` : h)
const numero = (v) => Number(String(v).replace(',', '.'))
const digitos = (v) => String(v ?? '').replace(/\D/g, '')

// S45: corpos no formato dos inputs da API (`JanelaEntregaInput`, `FreteZonaInput`, `BloqueioEntregaInput`).
export const corpoJanela = (f) => ({
  diaDaSemana: Number(f.diaDaSemana),
  horaInicio: hora(f.horaInicio),
  horaFim: hora(f.horaFim),
  capacidadeMaxima: Number(f.capacidadeMaxima),
  label: f.label.trim(),
})

export function corpoZona(f) {
  const porCep = f.cobertura === 'cep'
  return {
    label: f.label.trim(),
    valor: numero(f.valor),
    tempoEstimadoMinutos: Number(f.tempoEstimadoMinutos),
    ordem: Number(f.ordem),
    cepInicio: porCep ? digitos(f.cepInicio) : null,
    cepFim: porCep ? digitos(f.cepFim) : null,
    bairros: porCep ? null : f.bairros.split(',').map((b) => b.trim()).filter(Boolean),
  }
}

export const corpoBloqueio = (f) => ({
  data: f.data,
  motivo: f.motivo.trim(),
  janelaEspecificaId: f.janelaEspecificaId || null,
})
