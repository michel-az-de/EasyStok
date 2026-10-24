import { contextoDePrevia, GATILHOS, regraDoGatilho, textoDaRegra } from '../../dominio/automacao'
import {
  aceitarDivergencia, aplicarPagamento, cancelarCobranca, criarCobranca, estornarCobranca, liberaEsteira,
  marcarComprovante, reemitirCobranca, TEXTO_DE_CONFERENCIA, textoDaCobranca,
} from '../../dominio/cobranca'
import { ehLead } from '../../dominio/conversa'
import { janelaPorId } from '../../dominio/entrega'
import { indiceDoPasso } from '../../dominio/esteira'
import { pontosGanhosNoPedido } from '../../dominio/fidelidade'
import { dataHora, mesPorExtenso, moeda } from '../../dominio/formato'
import { encerrarComEstorno } from '../../dominio/ocorrencia'
import { registroDaCobranca } from '../../dominio/pagamento'
import { pedidoEncerrado } from '../../dominio/pedido'
import { MARCA_DE_CONVERSAO } from '../../dominio/resumoAtendimento'
import { resumoParaCliente } from '../../dominio/resumoPedido'
import * as acao from '../acoes'
import { comMensagem, mapear, recusaEnvio } from '../reducer/auxiliares'
import { ATENDENTE } from '../reducer/estadoInicial'

// SÓ DA DEMONSTRAÇÃO (carregado por `src/demonstracao/`, fora do build de produção): cobrança Pix simulada,
// baixa, estorno e cancelamento. No modo API quem cobra e reconhece o pagamento é o EasyStok.

// Dinheiro reconhecido em um lugar só: a baixa exata, a marcação à mão e a
// diferença aceita terminam aqui. Enquanto a cobrança não libera (valor diferente
// sem decisão dela), o pedido NÃO anda e o recibo não sai.
function comPagamentoReconhecido(estado, id, cobranca, { agora, mensagemId }) {
  const conversa = estado.conversas.find((c) => c.id === id)
  const janela = janelaPorId(estado.catalogo.janelas, conversa.pedido.janela)
  const recibo = regraDoGatilho(estado.regras, GATILHOS.PAGAMENTO_CONFIRMADO)
  const libera = liberaEsteira(cobranca)
  // Reconhecer o dinheiro ABRE a esteira, nunca anda para trás: aceitar a
  // diferença de um pedido que já está em preparo não pode devolver ele para
  // "pago" e obrigar a marcar o preparo de novo.
  const paraPago = (pedido) =>
    (libera && indiceDoPasso(pedido.estado) < indiceDoPasso('pago') ? 'pago' : pedido.estado)
  // O mesmo contexto da prévia das automáticas: {nome}, {faixa} e {pedido}.
  // Com só { faixa }, a regra que usa {nome} chegava crua ao cliente.
  // `agora` aplica o respiro (RN-06, RN-22, US-028) dentro de contextoDePrevia.
  const contexto = contextoDePrevia(conversa, janela?.faixa, agora)
  // RN-03/UC-01 passo 10 (rodada 10, item P1.4): a primeira compra paga é
  // quem promove lead a cliente, nunca a confirmação de endereço
  // (CADASTRAR_ENDERECO, mais acima). A marca de sistema é o mesmo sinal que
  // dominio/resumoAtendimento.js lê para calcular "Converteu" no resumo.
  const converte = libera && ehLead(conversa)
  return mapear(estado, id, (c) => {
    let comConversao = c
    if (converte) {
      comConversao = comMensagem({
        ...c, conta: 'cliente', cliente: { ...c.cliente, desde: mesPorExtenso(agora) },
      }, { dir: 'sistema', texto: MARCA_DE_CONVERSAO }, { id: `${mensagemId}-conversao`, agora })
    }
    const comCobranca = {
      ...comConversao,
      pedido: { ...comConversao.pedido, cobranca, estado: paraPago(comConversao.pedido) },
    }
    if (!libera || !recibo) return comCobranca
    // Frente Fidelidade e cupons (rodada 13, issue #45, registro 107; RN-40
    // anti-spam): pontos ganhos entram como LINHA a mais na mensagem
    // automática de pagamento confirmado que já ia sair, nunca uma mensagem
    // própria. `cobranca.valor` é o que o cliente pagou de fato (já líquido
    // de cupom, se teve).
    const pontos = pontosGanhosNoPedido(cobranca.valor, estado.fidelidade.config)
    const textoRecibo = textoDaRegra(recibo, contexto)
    const texto = pontos > 0 ? `${textoRecibo}\n\nVocê ganhou ${pontos} pontos de fidelidade.` : textoRecibo
    return comMensagem(comCobranca, {
      dir: 'out', status: 'lida', automatica: true, regra: recibo.id, texto,
    }, { id: mensagemId, agora })
  })
}

export const casosPagamentoPix = {
  // Gerar o pedido manda o resumo e a cobrança de verdade, no mesmo despacho.
  // Antes saía a regra "cobranca", com o texto "Segue o link de pagamento" e
  // link nenhum: o cliente lia a promessa de um link que não existia. Agora a
  // emissão do Pix chega pronta na ação (o provider emite, RN-24) e o texto sai
  // de textoDaCobranca, que é o mesmo do botão Gerar cobrança. Uma mensagem só.
  [acao.GERAR_PEDIDO]: (estado, { id, agora, mensagemId, cobrancaId, emissao }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido) return estado
    if (recusaEnvio(estado, id, agora)) return estado
    const janela = janelaPorId(estado.catalogo.janelas, conversa.pedido.janela)
    const resumo = resumoParaCliente(conversa.pedido, estado.catalogo.cardapio, janela)
    // Cupom aplicado (frente Fidelidade, issue #45) desconta sozinho aqui:
    // `criarCobranca` já subtrai `pedido.valorDesconto` do total bruto (ver
    // dominio/cobranca.js). Sem `valorForcado`: esse parâmetro é de OUTRO
    // conceito (cobrança de diferença/complemento) e marcaria esta cobrança
    // normal como `cobranca.diferenca`, trocando a mensagem ao cliente.
    const cobranca = criarCobranca(conversa.pedido, estado.catalogo.cardapio, agora, emissao)
    const texto = textoDaCobranca(cobranca, conversa.pedido, estado.catalogo.cardapio)

    return mapear(estado, id, (c) => {
      const comResumo = comMensagem(
        { ...c, pedido: { ...c.pedido, cobranca } },
        { dir: 'out', texto: resumo, status: 'lida', automatica: true, regra: 'resumo' },
        { id: mensagemId, agora },
      )
      return comMensagem(
        comResumo,
        { dir: 'out', texto, status: 'lida', automatica: true, regra: 'cobranca-pix' },
        { id: cobrancaId, agora },
      )
    })
  },

  // A cobrança nasce junto com a mensagem que o cliente lê, no mesmo despacho.
  // Separar as duas coisas criaria cobrança que ninguém recebeu, e é justamente
  // esse silêncio que faz a dona ligar para o cliente perguntando se chegou.
  [acao.GERAR_COBRANCA]: (estado, { id, agora, mensagemId, emissao }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido || conversa.pedido.itens.length === 0) return estado
    // Ver nota de GERAR_PEDIDO acima: cupom desconta sozinho dentro de
    // `criarCobranca`.
    const cobranca = criarCobranca(conversa.pedido, estado.catalogo.cardapio, agora, emissao)
    const texto = textoDaCobranca(cobranca, conversa.pedido, estado.catalogo.cardapio)
    return mapear(estado, id, (c) => comMensagem(
      { ...c, pedido: { ...c.pedido, cobranca } },
      { dir: 'out', texto, status: 'lida', automatica: true, regra: 'cobranca-pix' },
      { id: mensagemId, agora },
    ))
  },

  // Reenvio depois de vencer. Conta tentativa e manda código novo: código velho
  // que o cliente guardou não paga mais, e ele precisa saber disso.
  [acao.REENVIAR_COBRANCA]: (estado, { id, agora, mensagemId, emissao, valorForcado = null }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca) return estado
    const cobranca = reemitirCobranca(
      conversa.pedido.cobranca, conversa.pedido, estado.catalogo.cardapio,
      agora, emissao, valorForcado,
    )
    const texto = textoDaCobranca(cobranca, conversa.pedido, estado.catalogo.cardapio)
    // Cobrança paga que sai de cena (complemento, diferença) vai para o
    // arquivo `pedido.pagamentos`: é o que mantém "quanto já foi pago" certo
    // depois da troca (dominio/pagamento.js, fonte única, registro 38).
    const anterior = conversa.pedido.cobranca
    const pagamentos = anterior.pagaEm
      ? [...(conversa.pedido.pagamentos ?? []), registroDaCobranca(anterior)]
      : (conversa.pedido.pagamentos ?? [])
    return mapear(estado, id, (c) => comMensagem(
      { ...c, pedido: { ...c.pedido, cobranca, pagamentos } },
      { dir: 'out', texto, status: 'lida', automatica: true, regra: 'cobranca-pix' },
      { id: mensagemId, agora },
    ))
  },

  // Simula o webhook do provedor (RN-24: a conciliação não é trabalho dela). O
  // botão na tela existe porque no protótipo não há provedor do outro lado, e
  // porque cliente que paga por fora acontece. O recibo ao cliente reusa a regra
  // automática de pagamento confirmado, que já existia e nunca tinha disparado.
  [acao.CONFIRMAR_PAGAMENTO]: (estado, { id, agora, mensagemId, valorPago = null }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca) return estado
    const cobranca = aplicarPagamento(conversa.pedido.cobranca, agora, valorPago)
    return comPagamentoReconhecido(estado, id, cobranca, { agora, mensagemId })
  },

  // O cliente mandou o print. Registra a espera e responde a ele, e só. Liberar
  // pedido por comprovante é o vetor do comprovante falso: a esteira continua
  // fechada até a baixa chegar.
  [acao.MARCAR_COMPROVANTE]: (estado, { id, agora, mensagemId }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca) return estado
    const cobranca = marcarComprovante(conversa.pedido.cobranca, agora)
    return mapear(estado, id, (c) => comMensagem(
      { ...c, pedido: { ...c.pedido, cobranca } },
      { dir: 'out', texto: TEXTO_DE_CONFERENCIA, status: 'lida', automatica: true, regra: 'cobranca-pix' },
      { id: mensagemId, agora },
    ))
  },

  // Caiu menos do que o cobrado e ela decidiu deixar passar. Quem escolhe é ela,
  // o sistema só registra quem liberou e quando.
  [acao.ACEITAR_DIVERGENCIA]: (estado, { id, agora, mensagemId }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca) return estado
    const cobranca = aceitarDivergencia(conversa.pedido.cobranca, agora)
    return comPagamentoReconhecido(estado, id, cobranca, { agora, mensagemId })
  },

  // Cancelar sempre avisa o cliente, com o texto pronto que a Frente A monta
  // (mesmo padrão de ENVIAR_MIDIA/GERAR_PEDIDO: o dominio não escreve copy).
  [acao.CANCELAR_PEDIDO]: (estado, { id, agora, mensagemId, texto }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido || pedidoEncerrado(conversa.pedido)) return estado
    // Fecha a cobrança em aberto junto (bug do dono, 24/09/2026: cancelar com
    // a cobrança vencida não fazia o relógio sumir). Cobrança já paga não é
    // tocada: só some quem ainda não recebeu nada.
    const cobranca = cancelarCobranca(conversa.pedido.cobranca, agora)
    return mapear(estado, id, (c) => comMensagem(
      { ...c, pedido: { ...c.pedido, estado: 'cancelado', cobranca } },
      { dir: 'out', texto, status: 'lida', automatica: true, regra: 'cancelamento' },
      { id: mensagemId, agora },
    ))
  },

  // Antes de "Em entrega" o estorno cancela o pedido junto, porque a massa
  // ainda não saiu da cozinha; depois ele só ganha a marca (cobranca.estornadaEm),
  // porque a Frente A lê esse campo para mostrar "Estornado" ao lado do passo.
  //
  // UC-06 passo 5-6: motivo obrigatório (barrado dentro de `estornarCobranca`
  // quando falta), valor pode ser parcial. O motivo grava em dois lugares: na
  // ocorrência, se o pedido tiver uma aberta, e no cadastro (nota interna).
  [acao.MARCAR_ESTORNO]: (estado, { id, agora, motivo, valor, notaId }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca) return estado
    const cobranca = estornarCobranca(conversa.pedido.cobranca, agora, { motivo, valor })
    if (cobranca === conversa.pedido.cobranca) return estado
    const antesDaEntrega = indiceDoPasso(conversa.pedido.estado) < indiceDoPasso('entrega')
    const ocorrencia = conversa.pedido.ocorrencia
      ? encerrarComEstorno(conversa.pedido.ocorrencia, { motivo, valor: cobranca.valorEstornado }, agora)
      : conversa.pedido.ocorrencia
    const nota = {
      id: notaId,
      autor: ATENDENTE,
      em: dataHora(agora),
      texto: `Estorno de ${moeda(cobranca.valorEstornado)} no pedido ${conversa.pedido.numero}. Motivo: ${motivo}`,
    }
    return mapear(estado, id, (c) => ({
      ...c,
      cliente: { ...c.cliente, notas: [nota, ...c.cliente.notas] },
      pedido: {
        ...c.pedido,
        cobranca,
        ocorrencia,
        estado: antesDaEntrega ? 'cancelado' : c.pedido.estado,
      },
    }))
  },
}
