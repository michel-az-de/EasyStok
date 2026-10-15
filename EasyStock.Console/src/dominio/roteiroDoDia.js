// Entregas do dia por janela (issue #1440, homologação de 07/10). Puro: recebe o que a API
// devolve (pedidos do KDS do dia, viagens e entregadores da S44, janelas e bloqueios da S45)
// e devolve uma seção por janela com os pedidos, o que falta para cada um sair e quem leva.
// A mesma estrutura alimenta a tela "Entregas de hoje" e o roteiro impresso.
import { faixaDeHorarios } from './entrega'
import { rotuloEmpresaEntregador } from './entregasApi'
import { dataIsoNoFuso, listaEmPortugues, plural } from './formato'

const SEM_JANELA = 'sem-janela'
const hhmm = (h) => String(h ?? '').slice(0, 5)
const chaveDaJanela = (label, inicio, fim) => `${hhmm(inicio)}|${hhmm(fim)}|${String(label ?? '').trim().toLowerCase()}`

// "2026-10-08" -> 4 (quinta). Meio-dia UTC: a data civil nunca escorrega de dia.
export const diaDaSemanaDaData = (data) => new Date(`${data}T12:00:00Z`).getUTCDay()

// "quinta-feira, 08/10/2026", para o cabeçalho da tela e do papel.
export const rotuloDaData = (data) => new Date(`${data}T12:00:00Z`).toLocaleDateString('pt-BR', {
  weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric', timeZone: 'UTC',
})

// Dia de produção do pedido, como o KDS calcula: a data da vaga; sem vaga, o agendamento;
// sem ele, a criação (no fuso da loja). O KDS manda aprovação de qualquer dia.
export const diaDoPedido = (p) => p.janela?.data ?? dataIsoNoFuso(Date.parse(p.agendadoParaEm ?? p.criadoEm))

// Status -> etapa da tela e o que falta. `pronto` depende da viagem e fica em `faltaDoPronto`.
const ETAPAS = {
  aguardando_pagamento: { etapa: 'pagamento', rotulo: 'Aguardando pagamento', falta: 'Falta o pagamento' },
  aguardando_aprovacao_baba: { etapa: 'aprovacao', rotulo: 'Aguardando aprovação', falta: 'Falta a sua aprovação' },
  aprovado_baba: { etapa: 'agendado', falta: 'Falta preparar' },
  aguardando: { etapa: 'agendado', falta: 'Falta preparar' },
  preparando: { etapa: 'preparo', rotulo: 'Em preparo', falta: 'Falta terminar o preparo' },
  pronto: { etapa: 'pronto', rotulo: 'Pronto' },
  saiu_para_entrega: { etapa: 'saiu', rotulo: 'Saiu para entrega', falta: null },
  entregue: { etapa: 'entregue', rotulo: 'Entregue', falta: null },
}

function faltaDoPronto(viagem) {
  if (!viagem) return 'Falta pôr numa viagem'
  if (!viagem.entregadorId) return 'Falta escolher quem leva'
  return 'Falta marcar a saída'
}

// Viagem viva de cada pedido (desfeita não conta: o pedido voltou a ficar solto).
function viagensPorPedido(viagens) {
  const mapa = new Map()
  for (const viagem of viagens ?? []) {
    if (viagem.situacao === 'Desfeita') continue
    for (const parada of viagem.paradas ?? []) mapa.set(parada.pedidoId, { viagem, parada })
  }
  return mapa
}

// Quem leva: o retrato gravado na saída manda; antes dela, o entregador escolhido na viagem.
function entregadorDe(achado, entregadores) {
  if (!achado) return null
  const { viagem, parada } = achado
  if (parada.entregadorNome) return { nome: parada.entregadorNome, empresa: rotuloEmpresaEntregador(parada.empresaEntregador) }
  const quem = viagem.entregadorId ? (entregadores ?? []).find((e) => e.id === viagem.entregadorId) : null
  return quem ? { nome: quem.nome, empresa: rotuloEmpresaEntregador(quem.empresa) } : null
}

const textoDoEntregador = (e) => (e.empresa ? `${e.nome} (${e.empresa})` : e.nome)

function linhaDoPedido(p, achado, entregadores) {
  const base = ETAPAS[p.status] ?? { etapa: 'outro', rotulo: p.statusRotulo ?? p.status, falta: null }
  const pago = Boolean(p.pagoEm)
  const rotulo = base.rotulo ?? (pago ? 'Pago, agendado' : 'Agendado')
  const viagem = achado?.viagem ?? null
  return {
    id: p.id,
    numero: p.numeroCurto,
    cliente: p.clienteNome ?? 'Cliente sem nome',
    apto: p.clienteApt ?? null,
    endereco: p.endereco ?? null,
    status: p.status,
    etapa: base.etapa,
    rotulo,
    pago,
    falta: p.status === 'pronto' ? faltaDoPronto(viagem) : base.falta,
    entregador: entregadorDe(achado, entregadores),
    viagem: viagem ? { id: viagem.id, situacao: viagem.situacao } : null,
    podePorNaViagem: p.status === 'pronto' && !viagem,
  }
}

function novoGrupo({ chave, label, inicio, fim, capacidade = null, janelaId = null }) {
  return {
    chave, label, inicio: hhmm(inicio), fim: hhmm(fim), janelaId, capacidade,
    faixa: chave === SEM_JANELA ? 'Sem janela' : faixaDeHorarios(hhmm(inicio), hhmm(fim)),
    bloqueio: null, pedidos: [], quemLeva: [],
  }
}

export function roteiroDoDia({ data, pedidos, viagens, entregadores, janelas, bloqueios }) {
  const diaDaSemana = diaDaSemanaDaData(data)
  const bloqueiosDoDia = (bloqueios ?? []).filter((b) => b.data === data)
  const bloqueioDoDia = bloqueiosDoDia.find((b) => !b.janelaEspecificaId)?.motivo ?? null
  const grupos = new Map()

  // Janelas cadastradas para este dia da semana, mesmo vazias: a dona vê a vaga que sobra.
  for (const j of janelas ?? []) {
    if (j.diaDaSemana !== diaDaSemana || !j.ativa) continue
    const grupo = novoGrupo({
      chave: chaveDaJanela(j.label, j.horaInicio, j.horaFim), label: j.label, inicio: j.horaInicio, fim: j.horaFim,
      capacidade: j.capacidadeMaxima, janelaId: j.id,
    })
    grupo.bloqueio = bloqueiosDoDia.find((b) => b.janelaEspecificaId === j.id)?.motivo ?? bloqueioDoDia
    grupos.set(grupo.chave, grupo)
  }

  const naViagem = viagensPorPedido(viagens)
  const doDia = (pedidos ?? []).filter((p) => ETAPAS[p.status] && diaDoPedido(p) === data)
  for (const p of doDia) {
    const chave = p.janela ? chaveDaJanela(p.janela.label, p.janela.inicio, p.janela.fim) : SEM_JANELA
    if (!grupos.has(chave)) {
      grupos.set(chave, p.janela
        ? novoGrupo({ chave, label: p.janela.label, inicio: p.janela.inicio, fim: p.janela.fim })
        : novoGrupo({ chave, label: 'Sem janela', inicio: '', fim: '' }))
    }
    grupos.get(chave).pedidos.push(linhaDoPedido(p, naViagem.get(p.id), entregadores))
  }

  for (const grupo of grupos.values()) {
    grupo.quemLeva = [...new Set(grupo.pedidos.filter((x) => x.entregador).map((x) => textoDoEntregador(x.entregador)))]
  }
  const ordenados = [...grupos.values()].sort((a, b) => {
    if (a.chave === SEM_JANELA) return 1
    if (b.chave === SEM_JANELA) return -1
    return a.inicio.localeCompare(b.inicio) || a.fim.localeCompare(b.fim)
  })
  return { data, diaDaSemana, bloqueioDoDia, grupos: ordenados, total: doDia.length, ids: new Set(doDia.map((p) => p.id)) }
}

// "4 entregas das 12h00 às 14h00, 2 das 17h00 às 19h00 e 1 sem janela".
export function resumoDoRoteiro(roteiro) {
  const partes = roteiro.grupos
    .filter((g) => g.pedidos.length > 0)
    .map((g, i) => {
      const quantas = i === 0 ? plural(g.pedidos.length, 'entrega', 'entregas') : String(g.pedidos.length)
      return g.chave === SEM_JANELA ? `${quantas} sem janela` : `${quantas} das ${g.faixa}`
    })
  return partes.length === 0 ? 'Nenhuma entrega neste dia' : listaEmPortugues(partes)
}

export const textoDeQuemLeva = textoDoEntregador
