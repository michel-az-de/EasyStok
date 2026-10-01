import { ErroApi, chamarApi } from './cliente'
import { estadoDoStatusApi } from './comandaApi'
import { instante } from './traducaoConversas'
import { dataHora, mascaraCep, mesPorExtenso } from '../../dominio/formato'

// Ficha do cliente na API real (F09): dossiê (S25) e CRM leve (S24) do cadastro.
// O dossiê vem pela conversa (`api/atendimento/conversas/{id}/dossie`), que também atende o lead
// sem cadastro (dossiê mínimo). Tag, nota, bloqueio e preferências gravam no cadastro
// (`api/clientes/{clienteId}/...`). Aqui só se traduz o formato; a regra é do EasyStok.
// Preferências (`PUT .../preferencias`) ficam de fora: a Ficha não tem tela para elas; os
// avisos por e-mail e SMS são consentimento (S38, F02).
const doCliente = (clienteId) => `/api/clientes/${clienteId}`

const NOME_DO_CANAL = {
  WhatsApp: 'WhatsApp', Instagram: 'Instagram', Messenger: 'Messenger',
  ChatSite: 'Chat do site', Email: 'E-mail', Sms: 'SMS',
}
const ROTULO_DA_SITUACAO = { Automatica: 'Automático', Assumida: 'Em atendimento', Encerrada: 'Encerrado' }

const ms = (valor) => (valor ? new Date(instante(valor)).getTime() : null)
const hora = (valor) => new Date(ms(valor)).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })

function enderecoEmTexto(enderecos) {
  const e = (enderecos ?? []).find((x) => x.padrao) ?? enderecos?.[0]
  if (!e) return null
  const rua = [e.logradouro, e.numero].filter(Boolean).join(', ')
  return [rua, e.complemento, e.bairro, e.cep ? mascaraCep(e.cep) : null].filter(Boolean).join(', ') || null
}

const notaDaApi = (n) => ({ id: n.id, autor: n.autor, em: dataHora(ms(n.criadoEm)), texto: n.texto })

const pedidoDoHistorico = (p) => ({
  numero: `EZ-${String(p.id).slice(0, 6).toUpperCase()}`,
  pedidoId: p.id,
  em: instante(p.criadoEm),
  total: p.total,
  estado: estadoDoStatusApi(p.status),
  itens: (p.itens ?? []).map((i) => `${i.quantidade}× ${i.nome}`),
  nota: null,
})

// Conversa recente do dossiê → linha da aba Atendimentos. Não é o resumo congelado do
// encerramento do protótipo (sem número AT, receita nem avaliação): a modal mostra só o que veio.
const atendimentoDaApi = (c) => ({
  id: c.id,
  daApi: true,
  numero: null,
  encerradoEm: ms(c.ultimaMensagemEm),
  resumoApi: [
    NOME_DO_CANAL[c.canal] ?? c.canal,
    ROTULO_DA_SITUACAO[c.situacao] ?? c.situacao,
    `${hora(c.iniciadaEm)} às ${hora(c.ultimaMensagemEm)}`,
  ].join(' · '),
})

// "Cliente desde" só quando o dossiê trouxe todos os pedidos: com mais pedidos do que a
// lista, o mais antigo listado não é o primeiro.
function desdeQuando(dossie) {
  const pedidos = dossie.ultimosPedidos ?? []
  if (pedidos.length === 0 || dossie.totalPedidos > pedidos.length) return null
  return mesPorExtenso(Math.min(...pedidos.map((p) => ms(p.criadoEm))))
}

// Dossiê → { clienteId, cliente, bloqueio } no formato que a Ficha e o Histórico já leem.
// `dossie: true` marca o cadastro carregado; a polling da inbox o preserva (reducer.js).
export function fichaDoDossie(dossie) {
  const dados = dossie.cliente ?? {}
  const pedidos = dossie.ultimosPedidos ?? []
  return {
    clienteId: dados.id ?? null,
    cliente: {
      dossie: true,
      desde: desdeQuando(dossie),
      telefone: dados.telefone ?? null,
      email: dados.email ?? null,
      endereco: enderecoEmTexto(dossie.enderecos),
      pedidos: dossie.totalPedidos ?? pedidos.length,
      tags: (dossie.tags ?? []).map((t) => t.tag),
      notas: (dossie.notas ?? []).map(notaDaApi),
      historico: pedidos.map(pedidoDoHistorico),
      atendimentos: (dossie.conversasRecentes ?? []).map(atendimentoDaApi),
    },
    bloqueio: dossie.bloqueado
      ? { motivo: dados.motivoBloqueio || 'Motivo não registrado.', em: null, por: null, alcance: 'todos os canais' }
      : null,
  }
}

export const obterDossieDaConversa = (conversaId) =>
  chamarApi(`/api/atendimento/conversas/${conversaId}/dossie`)

export const adicionarTag = (clienteId, tag) =>
  chamarApi(`${doCliente(clienteId)}/tags`, { metodo: 'POST', corpo: { tag } })

export const removerTag = (clienteId, tag) =>
  chamarApi(`${doCliente(clienteId)}/tags/${encodeURIComponent(tag)}`, { metodo: 'DELETE' })

export const adicionarNota = (clienteId, texto) =>
  chamarApi(`${doCliente(clienteId)}/notas`, { metodo: 'POST', corpo: { texto } })

// Bloqueio é da policy Gerente: o 403 diz por que não gravou, em vez do texto genérico.
async function comoGerente(chamada) {
  try {
    return await chamada()
  } catch (erro) {
    if (erro.status === 403) {
      throw new ErroApi(403, erro.codigo, 'Só quem é gerente bloqueia ou desbloqueia cliente.')
    }
    throw erro
  }
}

export const bloquear = (clienteId, motivo) => comoGerente(() =>
  chamarApi(`${doCliente(clienteId)}/bloquear`, { metodo: 'POST', corpo: { motivo } }))

export const desbloquear = (clienteId) => comoGerente(() =>
  chamarApi(`${doCliente(clienteId)}/desbloquear`, { metodo: 'POST' }))
