import * as acao from '../acoes'
import {
  FORMA_NA_ENTREGA, FORMA_ONLINE, corpoDoPedido, formaDoMeio, gerarPedido, janelaDoId, listarJanelas, obterPedido, pedidoDaApi,
  reemitirCobranca, conferirPagamentoOnline, trocarFormaPagamento, registrarPagamentoManual, desfazerPagamentoManual, cancelarPedido, trocarJanelaPedido, STATUS_DO_PASSO,
} from '../../infra/api/comandaApi'
import { mudarStatusKds } from '../../infra/api/kdsApi'
import { aprovarPedido as aprovarPedidoApi } from '../../infra/api/entregasApi'
import { MOTIVO_BAIXA_SEM_APROVACAO, aguardaAprovacao, baixaCancelaCobrancaOnline, baixaEsperaAprovacao, situacaoDaCobranca } from '../../dominio/cobranca'
import { pedeConfirmacaoDoSaldo, ultimoPagamentoApi } from '../../dominio/pagamento'
import { motivoDoPedidoNaoCriado } from '../../dominio/pedido'
import { criarChaveDeEnvio } from '../chaveDeEnvio'
import { SO_NO_EASYSTOK } from './naoLigadas'

// Comanda e cobrança no modo API (F03, S10/S11). Montar a comanda (itens, observação,
// janela, meio) continua local até o envio; "Gerar cobrança e enviar" cria o pedido no
// EasyStok, que cobra pelo Mercado Pago e manda o resumo com o link pela conversa. Depois
// disso o pedido é do EasyStok: a polling traz pago, expirado e o resto da esteira.
//
// A esteira (#1474) anda pelo mesmo PATCH da Cozinha; o aviso ao cliente sai do EasyStok.
// O que ainda não está ligado (estorno, voltar etapa, comprovante) não mexe na
// memória do navegador com pedido já criado: avisa e deixa como está, e a Ficha nem mostra
// o botão (`acaoDisponivel`).
//
// Sem pedido criado (#1271) vale o mesmo: só a comanda é rascunho local. Gerar ou reenviar a
// cobrança cria o pedido (o EasyStok cobra e manda o resumo); pagamento e esteira avisam,
// porque um rascunho "Pago" ou "Em preparo" nunca chega à cozinha.
const NAO_LIGADO = 'esta ação ainda não está disponível nesta tela.'
const SEM_PEDIDO = 'o pedido ainda não foi criado. Gere a cobrança ou envie ao cliente primeiro.'
// #1924: textos do link novo pela Ficha.
export const LINK_NOVO_SEM_ENVIO = 'Link novo gerado, mas não saiu na conversa (fora da janela de 24 h ou canal fora do ar). '
  + 'Copie o link em Pagamento e mande ao cliente.'
export const LINK_AINDA_VALE = 'O link desta cobrança ainda vale. Para mandar de novo, copie o link em Pagamento.'
const LINK_CANCELADO_ESPERA_APROVACAO = 'O link foi cancelado e o pedido agora espera aprovação. '
  + 'Use "Aprovar pedido" na Ficha e registre o pagamento de novo.'

const MESMA_FORMA = 'A cobrança já está nessa forma de pagamento. Nada mudou.'

// #1942 (#1923): 409 que o EasyStok recusou antes de criar o pedido. Mostra o motivo, sem reler. O resto dos
// 409 (PEDIDO_EM_ANDAMENTO, a corrida do clique duplo, e código desconhecido) pode ter criado: relê.
const RECUSA_DEFINITIVA = new Set(['LOJA_FECHADA', 'JANELA_ESGOTADA', 'CEP_SEM_COBERTURA', 'CLIENTE_BLOQUEADO'])

const EDITA_COMANDA = ['adicionarItem', 'removerItem', 'ajustarQuantidade', 'ajustarObservacao', 'forcarEncaixe']

export function criarAcoesComandaApi(acoes, { despachar, estadoRef }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })
  // #1474: o aviso de uma tentativa que falhou some quando a ação seguinte dá certo.
  const limparAviso = () => despachar({ tipo: acao.FECHAR_AVISO_API })
  const pedidoDe = (id) => estadoRef.current.conversas.find((c) => c.id === id)?.pedido ?? null
  const pedidoCriado = (id) => pedidoDe(id)?.pedidoId ?? null
  // Pagamento parcial repetido depois de "sem conexão" não pode entrar duas vezes.
  const envioDoPagamento = criarChaveDeEnvio()

  // O meio escolhido na tela vai junto: a API devolve a forma, não o meio (F07, item 5).
  // #1683: o POST diz se o resumo saiu ao cliente; a leitura do pedido não diz. A marca fica
  // aqui e volta ao pedido a cada recarga desta tela. A troca de forma
  // tira a marca: a recarga leva `false` para o reducer não herdar a antiga.
  const semResumo = new Set()
  async function recarregar(id) {
    const tinhaMarca = Boolean(pedidoDe(id)?.resumoNaoEnviado)
    const lido = pedidoDaApi(await obterPedido(id), pedidoDe(id))
    const marcado = Boolean(lido) && semResumo.has(lido.pedidoId)
    const pedido = lido && (marcado || tinhaMarca) ? { ...lido, resumoNaoEnviado: marcado } : lido
    if (pedido) despachar({ tipo: acao.SINCRONIZAR_PEDIDO, id, pedido })
    return pedido
  }

  // Uma ida ao EasyStok por conversa (#1287): o segundo clique com a primeira em voo
  // recebe a mesma promessa, sem outro POST. O botão espera essa promessa para destravar.
  // #1510: só a MESMA ação herda a promessa; outra ação com a primeira em voo avisa e não
  // roda (antes recebia a promessa da primeira e sumia sem aviso).
  const AGUARDE = 'Aguarde a ação anterior terminar e tente de novo.'
  const emVoo = new Map()
  const umaPorConversa = (id, nome, fazer) => {
    const atual = emVoo.get(id)
    if (atual?.nome === nome) return atual.promessa
    if (atual) { avisar(AGUARDE); return Promise.resolve({ erro: AGUARDE }) }
    const promessa = fazer()
    // Recusa decidida na hora (`{ erro }`) não tem o que esperar: não segura a conversa.
    if (!promessa?.finally) return promessa
    const final = promessa.finally(() => emVoo.delete(id))
    emVoo.set(id, { nome, promessa: final })
    return final
  }

  const soSemPedidoCriado = (nome) => (id, ...resto) => {
    if (!pedidoCriado(id)) {
      return acoes[nome](id, ...resto)
    }
    avisar('Comanda: o pedido já está no EasyStok e não muda por aqui.')
    return undefined
  }

  const soNoEasyStok = (rotulo) => (id) => {
    avisar(`${rotulo}: ${pedidoCriado(id) ? NAO_LIGADO : SEM_PEDIDO}`)
    return undefined
  }

  const trocarForma = (id, meio) => umaPorConversa(id, 'trocarForma', () => {
    const pedidoId = pedidoCriado(id)
    // Link novo ou forma na entrega: o que valia para o resumo do pedido não vale mais.
    semResumo.delete(pedidoId)
    despachar({ tipo: acao.ESCOLHER_MEIO_PAGAMENTO, id, meio })
    // Falhou: o meio local já mudou, então volta ao que o EasyStok tem (F07, item 5).
    return trocarFormaPagamento(pedidoId, formaDoMeio(meio))
      .then(() => { limparAviso(); return recarregar(id) })
      .catch((erro) => {
        avisar(`Forma de pagamento: ${erro.message}`)
        return recarregar(id).catch(() => {})
      })
  })

  // "Enviar ao cliente", ou gerar a cobrança antes dele: o EasyStok cria o pedido, cobra e
  // manda o resumo pela conversa. Só o POST decide "não criado" (#1287): a recarga que
  // falha depois dele é só a tela atrasada, a polling traz o pedido.
  const criarPedido = (id, meio = null, enviarAoCliente = true) => umaPorConversa(id, 'criarPedido', () => {
    const pedido = pedidoDe(id)
    if (!pedido || pedidoCriado(id)) return undefined
    // #1683: o erro volta como `{ erro }` para a barra mostrar ao lado do botão; o aviso fica.
    if (!janelaDoId(pedido.janela)) {
      const erro = 'Escolha a janela de entrega antes de enviar ao cliente.'
      avisar(erro)
      return { erro }
    }
    const corpo = { ...corpoDoPedido({ ...pedido, meio: meio ?? pedido.meio }), enviarAoCliente }
    return gerarPedido(id, corpo).then(
      (gerado) => {
        // Mercado Pago fora: o pedido nasce sem link e nada o emite sozinho (#1287).
        const faltas = []
        if (!gerado?.cobranca && corpo.forma === FORMA_ONLINE) {
          faltas.push('a cobrança não saiu (Mercado Pago fora do ar). Use "Gerar cobrança" para emitir o link')
        }
        if (!gerado?.enviadoAoCliente) {
          if (gerado?.pedidoId) semResumo.add(gerado.pedidoId)
          if (enviarAoCliente) faltas.push('o resumo não saiu ao cliente (conversa fora da janela de 24 h ou canal fora do ar)')
        }
        const aviso = faltas.length > 0 ? `Pedido criado no EasyStok, mas ${faltas.join('; e ')}.` : null
        limparAviso()
        if (aviso) avisar(aviso)
        else if (!enviarAoCliente) avisar(gerado?.cobranca?.linkPagamento
          ? 'Pedido e cobrança criados sem envio. Abra Pagamento para copiar o link ou usar um modelo aprovado.'
          : 'Pedido e cobrança criados sem envio. Confira a forma de recebimento em Pagamento.')
        return recarregar(id).catch(() => avisar(aviso ? `${aviso} Atualizando a tela…` : 'Pedido criado no EasyStok, atualizando a tela…'))
      },
      (erro) => {
        if (!erro.status || erro.status >= 500 || (erro.status === 409 && !RECUSA_DEFINITIVA.has(erro.codigo))) {
          const mensagem = 'Não foi possível confirmar se o pedido foi criado. Aguarde a atualização da conversa antes de reenviar.'
          const incerto = () => { avisar(mensagem); return { erro: mensagem } }
          return recarregar(id).then((atual) => {
            if (!atual?.pedidoId) return incerto()
            limparAviso()
            avisar('Pedido encontrado. Confira a cobrança e o envio do resumo.')
            return atual
          }).catch(incerto)
        }
        const mensagem = motivoDoPedidoNaoCriado(erro)
        avisar(mensagem)
        return { erro: mensagem }
      },
    )
  })

  // Pedido criado sem link (Mercado Pago fora na hora): a operadora pede a emissão (S11).
  const reemitir = (id) => umaPorConversa(id, 'reemitir', () => reemitirCobranca(pedidoCriado(id)).then(
    () => { limparAviso(); return recarregar(id).catch(() => {}) },
    (erro) => avisar(`Cobrança: ${erro.message}`),
  ))

  // #1924: link vencido que o job não reemite mais (segundo vencimento, pedido esperando a atendente).
  // A troca para o link do Mercado Pago gera o link novo e o EasyStok manda na conversa
  // (`TextoLinkTrocado`); o `POST /cobranca` só gerava, sem mandar.
  const mandarLinkNovo = (id) => umaPorConversa(id, 'mandarLinkNovo', () => {
    const pedidoId = pedidoCriado(id)
    semResumo.delete(pedidoId)
    return trocarFormaPagamento(pedidoId, FORMA_ONLINE).then(
      (r) => {
        limparAviso()
        if (!r?.enviadaNaConversa) avisar(LINK_NOVO_SEM_ENVIO)
        return recarregar(id).catch(() => {})
      },
      (erro) => {
        avisar(`Link novo: ${erro.message}`)
        return recarregar(id).catch(() => {})
      },
    )
  })

  // Com pedido criado, gerar ou reenviar a cobrança troca a forma (S11), sem cobrança online emite
  // o link e com o link vencido manda um novo (#1924). Link ainda valendo não sai de novo sozinho.
  const cobrar = (id, meio, enviarAoCliente = true) => {
    if (!pedidoCriado(id)) return criarPedido(id, meio, enviarAoCliente)
    const atual = pedidoDe(id)
    if (meio && formaDoMeio(meio) !== formaDoMeio(atual?.meio)) return trocarForma(id, meio)
    if (!atual?.cobranca && formaDoMeio(atual?.meio) === FORMA_ONLINE) return reemitir(id)
    if (formaDoMeio(atual?.meio) === FORMA_ONLINE && situacaoDaCobranca(atual.cobranca, Date.now()).chave === 'expirada') {
      return mandarLinkNovo(id)
    }
    avisar(LINK_AINDA_VALE)
    return undefined
  }

  // #1474: com o link do Mercado Pago pendente o EasyStok recusa a baixa à mão (pedido ainda
  // pré-operacional). Primeiro a forma vira "na entrega" (cancela o link, o pedido entra na
  // fila), depois o recebido é registrado. A tela já avisou a dona antes de confirmar.
  // #1586: a recusa volta como `{ erro }`; o formulário só fecha no sucesso, com valor e método.
  const receber = (id, valor, metodo) => {
    const pedidoId = pedidoCriado(id)
    if (!pedidoId) return soNoEasyStok('Receber pagamento')(id)
    if (!metodo) { avisar('Selecione como recebeu o pagamento.'); return undefined }
    // #1474 (R8): pedido que exige aprovação (fora da área liberado) é pré-operacional até a
    // dona aprovar; o EasyStok recusaria a baixa. Nada sai antes disso.
    if (baixaEsperaAprovacao(pedidoDe(id))) {
      avisar(MOTIVO_BAIXA_SEM_APROVACAO)
      return Promise.resolve({ erro: MOTIVO_BAIXA_SEM_APROVACAO })
    }
    const cancelarLink = baixaCancelaCobrancaOnline(pedidoDe(id))
    const chave = envioDoPagamento.para({ pedidoId, valor, metodo })
    return umaPorConversa(id, 'receber', () => (cancelarLink ? trocarFormaPagamento(pedidoId, FORMA_NA_ENTREGA) : Promise.resolve())
      .then(() => registrarPagamentoManual(pedidoId, valor, metodo, chave))
      .then(
        () => {
          envioDoPagamento.confirmar(chave)
          limparAviso()
          return recarregar(id).catch(() => avisar('Pagamento registrado. Atualizando a tela…'))
        },
        (erro) => {
          const naoRegistrado = erro.codigo === 'pedido_ja_pago' ? erro.message
            : !erro.status || erro.status >= 500
              ? 'Não foi possível confirmar o registro do pagamento. Confira o pedido antes de tentar novamente.'
              : `Pagamento não registrado: ${erro.message}`
          const recusar = (mensagem) => { avisar(mensagem); return { erro: mensagem } }
          if (!cancelarLink) return recusar(naoRegistrado)
          // O link pode já ter sido cancelado: a tela mostra o pedido como o EasyStok ficou. Sem
          // o `requerAprovacao` no pedido da conversa, só depois da troca se sabe que ele passou
          // a esperar aprovação; o aviso diz o próximo passo em vez do erro cru.
          return recarregar(id)
            .then((atual) => recusar(aguardaAprovacao(atual) ? LINK_CANCELADO_ESPERA_APROVACAO : naoRegistrado))
            .catch(() => recusar(naoRegistrado))
        },
      ))
  }

  // #1474 (R1): a esteira da Ficha anda pelo mesmo PATCH da Cozinha (máquina de estados da
  // API). O aviso ao cliente em preparo, saída e entrega sai do EasyStok. O erro volta para a
  // barra mostrar ao lado do botão; nada muda na tela até a API responder.
  // #1776 (item 19): "Marcar entregue" com saldo a receber volta `saldoAReceber` para a barra perguntar;
  // a confirmação reenvia com `confirmarSaldoAReceber`.
  const avancar = (id, passo, _entregador, { confirmarSaldoAReceber = false } = {}) => {
    const pedidoId = pedidoCriado(id)
    if (!pedidoId) return soNoEasyStok('Avançar a esteira')(id)
    const status = STATUS_DO_PASSO[passo]
    if (!status) return Promise.resolve({ erro: 'Esta etapa não se marca pela Ficha.' })
    return umaPorConversa(id, 'avancar', () => mudarStatusKds(pedidoId, status, { confirmarSaldoAReceber }).then(
      () => { limparAviso(); return recarregar(id).then(() => undefined, () => undefined) },
      (erro) => ({ erro: erro.message, saldoAReceber: pedeConfirmacaoDoSaldo(erro) }),
    ))
  }

  // #1474 (R8): pedido fora da área liberado espera a aprovação da dona antes da fila e da
  // baixa à mão. Mesmo endpoint da gaveta Entregas.
  const aprovar = (id) => {
    const pedidoId = pedidoCriado(id)
    if (!pedidoId) return soNoEasyStok('Aprovar pedido')(id)
    return umaPorConversa(id, 'aprovar', () => aprovarPedidoApi(pedidoId).then(
      () => { limparAviso(); return recarregar(id).then(() => undefined, () => undefined) },
      (erro) => {
        avisar(`Pedido não aprovado: ${erro.message}`)
        return { erro: erro.message }
      },
    ))
  }
  const desfazer = (id, motivo) => {
    if (!pedidoCriado(id)) return soNoEasyStok('Desfazer pagamento')(id)
    const pagamentoId = ultimoPagamentoApi(pedidoDe(id))?.pagamentoId ?? null
    // #1586: como na baixa, a recusa volta como `{ erro }` e o motivo digitado fica na tela.
    return umaPorConversa(id, 'desfazer', () => desfazerPagamentoManual(pedidoCriado(id), motivo, pagamentoId).then(
      () => { limparAviso(); return recarregar(id).catch(() => avisar('Pagamento desfeito. Atualizando a tela…')) },
      (erro) => {
        const mensagem = `Pagamento não desfeito: ${erro.message}`
        avisar(mensagem)
        return { erro: mensagem }
      },
    ))
  }

  return {
    escolherJanela: (id, janela, avisarCliente = false) => {
      if (!pedidoCriado(id)) { limparAviso(); return acoes.escolherJanela(id, janela) }
      return umaPorConversa(id, 'reagendar', async () => {
        const resultado = await trocarJanelaPedido(pedidoCriado(id), janela, avisarCliente)
        limparAviso()
        await recarregar(id).catch(() => avisar('Agendamento alterado. Não foi possível atualizar a Ficha; consulte novamente.'))
        return resultado
      })
    },
    cancelarPedido: (id, motivo) => {
      if (!pedidoCriado(id)) return soNoEasyStok('Cancelar pedido')(id)
      if (!motivo?.trim() || motivo.trim().length < 3 || motivo.trim().length > 500)
        return Promise.resolve({ erro: 'Informe um motivo entre 3 e 500 caracteres.' })
      return umaPorConversa(id, 'cancelar', async () => {
        try {
          await cancelarPedido(pedidoCriado(id), motivo.trim())
        } catch (erro) {
          avisar(`Não foi possível confirmar o cancelamento: ${erro.message}`)
          return { erro: erro.message }
        }
        limparAviso()
        await recarregar(id).catch(() => avisar('Pedido cancelado. Não foi possível atualizar a tela; consulte novamente.'))
        return { ok: true }
      })
    },
    confirmarPagamento: receber,
    conferirPagamento: (id) => {
      const pedidoId = pedidoCriado(id)
      if (!pedidoId) return Promise.resolve({ erro: SEM_PEDIDO })
      return umaPorConversa(id, 'conferirPagamento', async () => {
        try {
          const resultado = await conferirPagamentoOnline(pedidoId)
          limparAviso()
          if (resultado.confirmado) {
            await recarregar(id).catch(() => avisar('Pagamento conferido. Não foi possível atualizar a tela; consulte novamente.'))
          }
          return resultado
        } catch (erro) {
          const mensagem = erro.status && erro.status < 500 ? erro.message
            : 'Não foi possível conferir o pagamento agora. Tente novamente.'
          avisar(mensagem)
          return { erro: mensagem }
        }
      })
    },
    desfazerPagamento: desfazer,
    avancarEsteira: avancar,
    aprovarPedido: aprovar,
    ...Object.fromEntries(EDITA_COMANDA.map((nome) => [nome, soSemPedidoCriado(nome)])),
    ...Object.fromEntries(Object.entries(SO_NO_EASYSTOK).map(([nome, rotulo]) => [nome, soNoEasyStok(rotulo)])),

    // Janelas com vaga no prazo dos itens da comanda (S16). Promessa direta para a tela.
    carregarJanelasComanda: (itens, data = '') => listarJanelas({ itens, dataInicio: data || null, dataFim: data || null }),

    gerarPedido: (id, meio = null, enviarAoCliente = true) => criarPedido(id, meio, enviarAoCliente),

    // Com pedido criado, trocar o meio é a troca de forma da S11: online ↔ na entrega.
    // #1683: entre Pix e cartão por link o EasyStok não faz nada (é o mesmo link); a tela não
    // muda o meio nem diz que reenviou.
    alterarMeioPagamento: (id, meio) => {
      if (!pedidoCriado(id)) return acoes.alterarMeioPagamento(id, meio)
      if (formaDoMeio(meio) !== formaDoMeio(pedidoDe(id)?.meio)) return trocarForma(id, meio)
      avisar(MESMA_FORMA)
      return Promise.resolve({ erro: MESMA_FORMA })
    },

    gerarCobranca: (id, pedido, meio = null, enviarAoCliente = true) => cobrar(id, meio, enviarAoCliente),
    reenviarCobranca: (id, pedido, valorForcado = null, meio = null) => cobrar(id, meio),
  }
}
