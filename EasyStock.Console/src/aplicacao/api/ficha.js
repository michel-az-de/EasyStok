import * as acao from '../acoes'
import {
  adicionarNota, adicionarTag, bloquear, desbloquear, fichaDoDossie, obterDossieDaConversa, removerTag,
} from '../../infra/api/fichaClienteApi'
import {
  CODIGO_REEMBOLSO_MANUAL, abrirOcorrencia, listarOcorrencias, resolverOcorrencia,
} from '../../infra/api/ocorrenciasApi'
import { listarAtendentes, transferirConversa } from '../../infra/api/atendentesApi'
import { ocorrenciaAberta, ocorrenciaDoPedido } from '../../dominio/ocorrencia'
import { moeda } from '../../dominio/formato'

// Ficha do cliente no modo API (F09): tag, nota e bloqueio gravam no cadastro (S24), a Ficha e
// o Histórico vêm do dossiê (S25), a ocorrência é a do EasyStok (S27) e a conversa pode passar
// para outro atendente (S41). Tag e nota aparecem na hora (despacho local) e o dossiê relido
// depois de cada gravação traz o que a API guardou. Lead sem cadastro não grava nada: avisa.
export const SEM_CADASTRO = 'Cadastre o cliente para registrar tags, notas e bloqueio.'

export function criarAcoesFichaApi(acoes, { despachar, estadoRef }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })
  const conversaPorId = (id) => estadoRef.current.conversas.find((c) => c.id === id) ?? null
  // Bloqueio chega pelo nome (assinatura do protótipo); a selecionada desempata homônimos.
  const conversaPorNome = (nome) => {
    const selecionada = conversaPorId(estadoRef.current.selecionadaId)
    return selecionada?.nome === nome ? selecionada : estadoRef.current.conversas.find((c) => c.nome === nome) ?? null
  }

  async function recarregarFicha(id) {
    try {
      const [dossie, ocorrencias] = await Promise.all([
        obterDossieDaConversa(id),
        listarOcorrencias().catch(() => null),
      ])
      const ficha = fichaDoDossie(dossie)
      if (ocorrencias) {
        ficha.cliente.ocorrencias = ficha.clienteId ? ocorrencias.filter((o) => o.clienteId === ficha.clienteId) : []
      }
      despachar({ tipo: acao.APLICAR_FICHA_API, id, ficha })
    } catch (erro) {
      avisar(`Ficha do cliente: ${erro.message}`)
    }
  }

  // Grava, avisa se não deu e relê o dossiê: o que a API guardou é o que fica na tela.
  const gravar = (id, chamada, prefixo) => chamada
    .catch((erro) => avisar(`${prefixo}: ${erro.message}`))
    .finally(() => recarregarFicha(id))

  function comCadastro(conversa, executar) {
    if (!conversa?.clienteId) {
      avisar(SEM_CADASTRO)
      return
    }
    executar(conversa.clienteId)
  }

  async function reembolsar(conversa, motivo, valor) {
    const pedidoId = conversa.pedido.pedidoId
    const existente = ocorrenciaDoPedido(conversa.cliente.ocorrencias, pedidoId)
    try {
      // Na API reembolso só existe dentro de ocorrência: sem uma aberta no pedido, abre.
      const ocorrenciaId = ocorrenciaAberta(existente)
        ? existente.apiId
        : (await abrirOcorrencia({ pedidoId, categoria: 'outro', relato: motivo, conversaId: conversa.id })).apiId
      const resultado = await resolverOcorrencia(ocorrenciaId, { resolucao: motivo, reembolsar: true, valor })
      if (resultado?.reembolso?.codigo === CODIGO_REEMBOLSO_MANUAL) {
        avisar(`Pago fora do Mercado Pago: devolva ${moeda(valor)} na mão. A ocorrência foi resolvida.`)
      }
    } catch (erro) {
      avisar(erro.status === 502 ? erro.message : `Reembolso não feito: ${erro.message}`)
    } finally {
      recarregarFicha(conversa.id)
    }
  }

  return {
    recarregarFicha,

    adicionarTag: (id, tag) => comCadastro(conversaPorId(id), (clienteId) => {
      acoes.adicionarTag(id, tag)
      gravar(id, adicionarTag(clienteId, tag), 'Tag não gravada')
    }),
    removerTag: (id, tag) => comCadastro(conversaPorId(id), (clienteId) => {
      acoes.removerTag(id, tag)
      gravar(id, removerTag(clienteId, tag), 'Tag não tirada')
    }),
    editarTag: (id, tagAntiga, tagNova) => comCadastro(conversaPorId(id), (clienteId) => {
      acoes.editarTag(id, tagAntiga, tagNova)
      gravar(id, removerTag(clienteId, tagAntiga).then(() => adicionarTag(clienteId, tagNova)), 'Tag não alterada')
    }),
    salvarNota: (id, texto) => comCadastro(conversaPorId(id), (clienteId) => {
      acoes.salvarNota(id, texto)
      gravar(id, adicionarNota(clienteId, texto), 'Nota não gravada')
    }),

    // Policy Gerente: o 403 aparece com o motivo (fichaClienteApi.js) e nada muda na tela.
    bloquearCliente: (nome, motivo) => {
      const conversa = conversaPorNome(nome)
      comCadastro(conversa, (clienteId) => gravar(conversa.id, bloquear(clienteId, motivo), 'Bloqueio não gravado'))
    },
    desbloquearCliente: (nome) => {
      const conversa = conversaPorNome(nome)
      comCadastro(conversa, (clienteId) => gravar(conversa.id, desbloquear(clienteId), 'Desbloqueio não gravado'))
    },

    marcarEstorno: (id, motivo, valor) => {
      const conversa = conversaPorId(id)
      if (!conversa?.pedido?.pedidoId || !motivo || !(valor > 0)) {
        avisar('Reembolso: marque pelo pedido da conversa, com motivo e valor.')
        return
      }
      reembolsar(conversa, motivo, valor)
    },
    encerrarOcorrenciaSemEstorno: (id, preferencia) => {
      const conversa = conversaPorId(id)
      const ocorrencia = ocorrenciaDoPedido(conversa?.cliente.ocorrencias, conversa?.pedido?.pedidoId)
      if (!ocorrenciaAberta(ocorrencia)) {
        avisar('Nenhuma ocorrência aberta neste pedido.')
        return
      }
      gravar(id, resolverOcorrencia(ocorrencia.apiId, { resolucao: preferencia, reembolsar: false }), 'Ocorrência não encerrada')
    },
    registrarOcorrencia: (id, { categoria, relato }) => {
      const conversa = conversaPorId(id)
      if (!conversa?.pedido?.pedidoId) {
        avisar('Ocorrência só existe com pedido na conversa.')
        return
      }
      gravar(id, abrirOcorrencia({ pedidoId: conversa.pedido.pedidoId, categoria, relato, conversaId: id }), 'Ocorrência não aberta')
    },

    listarAtendentes,
    transferirConversa: (id, atendente) => transferirConversa(id, atendente.usuarioId)
      .then(() => avisar(`Conversa passada para ${atendente.nome}.`))
      .catch((erro) => avisar(`Não transferiu: ${erro.message}`)),
  }
}
