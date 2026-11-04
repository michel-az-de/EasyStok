// Avisos da operação (#2096): o que vai para a dona e o gerente por aviso no aparelho, e-mail e
// sino. Liga e desliga por tipo; os de prazo guardam o número (0 = desligado). Faixas e padrões
// são os mesmos que a API confere em `PUT api/atendimento/avisos`: aqui só para a tela avisar antes.

const ligado = (campo, rotulo, dica = null) => ({ campo, tipo: 'ligado', rotulo, dica })
const prazo = (campo, rotulo, { unidade, min, max, padrao, antes = false }, dica = null) =>
  ({ campo, tipo: 'prazo', rotulo, unidade, min, max, padrao, antes, dica })

const MIN = { singular: 'minuto', plural: 'minutos', curta: 'min' }
const HORA = { singular: 'hora', plural: 'horas', curta: 'h' }
const DIA = { singular: 'dia', plural: 'dias', curta: 'dias' }

export const GRUPOS_DE_AVISOS = [
  {
    id: 'atendimento', titulo: 'Atendimento',
    avisos: [ligado('clienteSemResposta', 'Cliente esperando resposta')],
  },
  {
    id: 'pedidos', titulo: 'Pedidos e cozinha',
    avisos: [
      ligado('pedidoAtrasado', 'Pedido sem início de preparo no horário previsto'),
      prazo('prontoSemEntregadorMinutos', 'Pedido pronto sem entregador', { unidade: MIN, min: 5, max: 240, padrao: 15 },
        'Avisa quando o pedido fica pronto esse tempo sem ninguém para levar.'),
      ligado('impressaoTravada', 'Impressão travada'),
      ligado('caixaEsquecido', 'Caixa aberto de ontem'),
    ],
  },
  {
    id: 'encomendas', titulo: 'Encomendas',
    avisos: [
      prazo('encomendaProximaHoras', 'Encomenda chegando', { unidade: HORA, min: 1, max: 48, padrao: 2, antes: true },
        'Avisa esse tempo antes do horário da encomenda.'),
      prazo('encomendaSemConfirmacaoHoras', 'Encomenda sem confirmação do cliente', { unidade: HORA, min: 1, max: 72, padrao: 24, antes: true },
        'Avisa esse tempo antes do horário se o cliente ainda não confirmou.'),
    ],
  },
  {
    id: 'estoque', titulo: 'Estoque',
    avisos: [
      prazo('estoqueVencendoDias', 'Estoque perto de vencer', { unidade: DIA, min: 1, max: 30, padrao: 2, antes: true },
        'Sai num resumo por dia com o que vence nesse prazo.'),
      prazo('estoqueParadoDias', 'Estoque parado para conferir', { unidade: DIA, min: 3, max: 120, padrao: 10 },
        'Pode já ter saído do freezer sem baixa. Entra no mesmo resumo do dia, depois desse tempo sem movimento.'),
    ],
  },
  {
    id: 'ia', titulo: 'IA',
    avisos: [
      ligado('reclamacaoCliente', 'A IA percebeu reclamação do cliente'),
      ligado('suspeitaAtendimento', 'A IA percebeu algo suspeito na conversa'),
    ],
  },
]

export const AVISOS = GRUPOS_DE_AVISOS.flatMap((grupo) => grupo.avisos)
const PORCAMPO = new Map(AVISOS.map((aviso) => [aviso.campo, aviso]))
export const avisoPorCampo = (campo) => PORCAMPO.get(campo) ?? null

const inteiro = (valor) => {
  const numero = typeof valor === 'string' && valor.trim() === '' ? Number.NaN : Number(valor)
  return Number.isInteger(numero) ? numero : null
}

// O que a API devolveu, no formato da tela: liga/desliga em booleano, prazo em número (0 = desligado).
export function normalizarAvisos(dados) {
  const avisos = {}
  for (const aviso of AVISOS) {
    const valor = dados?.[aviso.campo]
    avisos[aviso.campo] = aviso.tipo === 'ligado' ? valor === true : Math.max(0, inteiro(valor) ?? 0)
  }
  const sla = inteiro(dados?.slaRespostaMinutos)
  avisos.slaRespostaMinutos = sla && sla > 0 ? sla : null
  return avisos
}

// Prazo em edição pode estar vazio ou fora da faixa e continua ligado: só o 0 (número) desliga.
export const avisoLigado = (avisos, campo) => {
  const aviso = avisoPorCampo(campo)
  if (!aviso) return false
  return aviso.tipo === 'ligado' ? avisos?.[campo] === true : avisos?.[campo] !== 0 && avisos?.[campo] != null
}

// Desligar grava 0; religar sugere o padrão do aviso.
export function ligarAviso(avisos, campo, ligar) {
  const aviso = avisoPorCampo(campo)
  if (!aviso) return avisos
  const valor = aviso.tipo === 'ligado' ? ligar === true : (ligar ? aviso.padrao : 0)
  return { ...avisos, [campo]: valor }
}

export const mudarPrazo = (avisos, campo, texto) => (avisoPorCampo(campo)?.tipo === 'prazo' ? { ...avisos, [campo]: texto } : avisos)

const unidadeNoNumero = (unidade, n) => (n === 1 ? unidade.singular : unidade.plural)
export const textoDaFaixa = (aviso) => `De ${aviso.min} a ${aviso.max} ${unidadeNoNumero(aviso.unidade, aviso.max)}.`

// Mensagem por campo ligado fora da faixa; objeto vazio quando dá para salvar.
export function validarAvisos(avisos) {
  const erros = {}
  for (const aviso of AVISOS) {
    if (aviso.tipo !== 'prazo' || !avisoLigado(avisos, aviso.campo)) continue
    const numero = inteiro(avisos[aviso.campo])
    if (numero === null || numero < aviso.min || numero > aviso.max) {
      erros[aviso.campo] = `${aviso.rotulo}: use um número inteiro. ${textoDaFaixa(aviso)}`
    }
  }
  return erros
}

// Corpo do PUT: tudo menos o SLA, que é da aba Atendimento.
export function corpoDosAvisos(avisos) {
  const corpo = {}
  for (const aviso of AVISOS) {
    corpo[aviso.campo] = aviso.tipo === 'ligado'
      ? avisos?.[aviso.campo] === true
      : (avisoLigado(avisos, aviso.campo) ? inteiro(avisos[aviso.campo]) ?? 0 : 0)
  }
  return corpo
}

export const avisosMudaram = (atual, salvo) => AVISOS.some((aviso) =>
  String(atual?.[aviso.campo] ?? '') !== String(salvo?.[aviso.campo] ?? ''))

export const textoDoSla = (sla) => (sla
  ? `Avisa depois de ${sla} ${sla === 1 ? 'minuto' : 'minutos'} sem resposta; ajuste na aba Atendimento.`
  : 'Avisa quando passa o prazo de primeira resposta; ajuste na aba Atendimento.')
