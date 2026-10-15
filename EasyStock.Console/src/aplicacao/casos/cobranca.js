// Casos de reducer da Frente 4 · Cobrança (rodada 5, seção 4). Arquivo da F4:
// nenhuma outra frente edita este arquivo, e a F4 nunca edita `reducer.js`
// (ele já importa e espalha `casosCobranca` no objeto composto). Ações
// previstas em `aplicacao/acoes.js`: REFAZER_COBRANCA, COBRAR_COMPLEMENTO,
// MARCAR_RECEBIDO_ENTREGA, DESPACHAR_MESMO_ASSIM. Rodada 12: ALTERAR_MEIO_PAGAMENTO
// (substitui o antigo TROCAR_MEIO_PAGAMENTO) e DESFAZER_PAGAMENTO.
//
// GERAR_COBRANCA, REENVIAR_COBRANCA, CONFIRMAR_PAGAMENTO, MARCAR_COMPROVANTE
// e ACEITAR_DIVERGENCIA já existem em `reducer.js` desde a rodada 2 (Pix) e
// continuam lá: são genéricos por natureza (leem `emissao`/`cobranca` sem
// saber de meio nenhum), então a escolha de meio desta rodada entra só na
// EMISSÃO (efeito, montada em `aplicacao/AtendimentoProvider.jsx` e em
// `aplicacao/acoes/cobranca.js`), nunca no reducer.
//
// COBRAR_COMPLEMENTO é da F3 (decisão 31: "F3 Comanda, cobrança complementar
// e canhoto"), não desta frente: o acréscimo mora na comanda
// (`dominio/pagamento.js`, `pedido.pagamentos`), que é arquivo dela. Fica
// como caso vazio aqui só para a ação não cair no limbo se alguém disparar
// antes da F3 implementar; não é a versão final.
import * as acao from '../acoes'
import {
  aplicarPagamento, cobrancaTrocada, criarCobranca, desfazerPagamento, estadoDepoisDaTroca,
  estadoDepoisDeDesfazer, nomeDoMeio, podeAlterarMeio, podeDesfazerPagamento, reemitirCobranca,
  textoDaCobranca,
} from '../../dominio/cobranca'
import { indiceDoPasso } from '../../dominio/esteira'
import { dataHora, moeda } from '../../dominio/formato'

const ATENDENTE = 'Thatiane'

// Mesmo padrão de `reducer.js` (que não exporta o dele): mapear uma conversa
// por id.
const mapear = (estado, id, transformar) => ({
  ...estado,
  conversas: estado.conversas.map((c) => (c.id === id ? transformar(c) : c)),
})

// Mesmo helper de `reducer.js` (que não exporta o dele): registro de sistema
// não mexe no relógio da conversa, mensagem ao cliente mexe.
function comMensagem(conversa, mensagem, { id, agora }) {
  const paraOCliente = mensagem.dir !== 'sistema'
  return {
    ...conversa,
    atrasada: paraOCliente ? false : conversa.atrasada,
    ultimaEm: paraOCliente ? new Date(agora).toISOString() : conversa.ultimaEm,
    mensagens: [...conversa.mensagens, { id, em: new Date(agora).toISOString(), ...mensagem }],
  }
}

export const casosCobranca = {
  // "Gerar a cobrança da maquininha ou do vale" ANDA a esteira sem esperar
  // pagamento (seção 4, decisão 19: "A esteira anda sem pagamento";
  // `liberaEsteiraSemPagar` de `infra/catalogo.js`). Quem dispara esta ação é
  // a própria emissão da cobrança (ver `aplicacao/AtendimentoProvider.jsx`,
  // `gerarCobranca`/`reenviarCobranca`): ao ver que a emissão voltou sem
  // `link`, despacha GERAR_COBRANCA e, na sequência, esta.
  //
  // Nunca anda para trás e nunca pula "pago" de um pedido que já foi além
  // dele (mesma guarda de `comPagamentoReconhecido` em reducer.js).
  [acao.DESPACHAR_MESMO_ASSIM]: (estado, { id }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const pedido = conversa?.pedido
    if (!pedido || indiceDoPasso(pedido.estado) >= indiceDoPasso('pago')) return estado
    return mapear(estado, id, (c) => ({ ...c, pedido: { ...c.pedido, estado: 'pago' } }))
  },

  // Maquininha ou vale: "Recebi" registra a baixa igual a uma marcação à mão
  // (mesma função de domínio que a baixa manual do Pix usa, `aplicarPagamento`,
  // RN-25 sinal verde). `valorPago: null` de propósito: quem apertou o botão
  // viu o dinheiro entrar na maquininha, não uma conciliação de banco — é
  // exatamente a mesma honestidade da baixa à mão do Pix (RelogioPix mostra
  // "Pago à mão", nunca "Pago e conciliado", porque não passou pelo banco).
  // "Não recebi" (recebido=false) não muda nada: o bloco continua mostrando
  // "a receber", e é exatamente essa a resposta certa (decisão 19, seção 4:
  // "'Não recebi' deixa 'A receber R$ 68,00' no bloco").
  [acao.MARCAR_RECEBIDO_ENTREGA]: (estado, { id, agora, recebido }) => {
    if (!recebido) return estado
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca || conversa.pedido.cobranca.pagaEm) return estado
    const cobranca = aplicarPagamento(conversa.pedido.cobranca, agora, null)
    return mapear(estado, id, (c) => ({ ...c, pedido: { ...c.pedido, cobranca } }))
  },

  // Ponta (a) da integração: a escolha do chip mora no pedido, não no estado
  // local do bloco. Assim "Enviar comanda" e o atalho "Gerar cobrança" da
  // barra mandam o MESMO meio que ela marcou; sem meio marcado, os dois
  // perguntam antes de mandar. Só antes de existir cobrança: depois disso o
  // meio é o da própria cobrança, e mudar é "Trocar meio".
  [acao.ESCOLHER_MEIO_PAGAMENTO]: (estado, { id, meio }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido || conversa.pedido.cobranca) return estado
    return mapear(estado, id, (c) => ({ ...c, pedido: { ...c.pedido, meio } }))
  },

  // "Refazer cobrança": registro sem âncora direta em US/RN (fica anotado na
  // decisão 32 para o gerente revisar). Interpretação usada aqui: corrigir a
  // cobrança combinada de maquininha/vale sem trocar de meio nem contar como
  // reenvio ao cliente (que não existe, porque não há link para reenviar) —
  // por exemplo, o valor mudou antes de ela cair. Reusa `reemitirCobranca`
  // (mesma conta de tentativa do Pix), sem mensagem automática: não há nada
  // para reenviar a quem não recebe link.
  [acao.REFAZER_COBRANCA]: (estado, { id, agora, emissao }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca || conversa.pedido.cobranca.pagaEm) return estado
    const cobranca = reemitirCobranca(
      conversa.pedido.cobranca, conversa.pedido, estado.catalogo.cardapio, agora, emissao,
    )
    return mapear(estado, id, (c) => ({ ...c, pedido: { ...c.pedido, cobranca } }))
  },

  // Ver comentário do topo do arquivo: acréscimo pós-pagamento é da F3.
  [acao.COBRAR_COMPLEMENTO]: (estado) => estado,

  // Rodada 12 (issue #13): "Alterar forma de pagamento". Numa ação só: a
  // cobrança pendente vai para o histórico, cancelada; sai uma nova na forma
  // escolhida, com texto que conta a troca ao cliente; a conversa ganha o
  // registro de sistema. O pedido é o mesmo (número, itens, janela). A emissão
  // chega pronta do Provider, como em GERAR_COBRANCA.
  [acao.ALTERAR_MEIO_PAGAMENTO]: (estado, {
    id, agora, emissao, mensagemId, sistemaId,
  }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const pedido = conversa?.pedido
    if (!podeAlterarMeio(pedido) || emissao?.meio === pedido.cobranca.meio) return estado
    const anterior = pedido.cobranca
    const base = { ...pedido, estado: estadoDepoisDaTroca(pedido, anterior, emissao.link != null) }
    const cobranca = {
      ...criarCobranca(base, estado.catalogo.cardapio, agora, emissao),
      tentativa: (anterior.tentativa ?? 1) + 1,
      meioAnterior: anterior.meio ?? 'pix',
    }
    const novoPedido = {
      ...base,
      meio: cobranca.meio,
      cobranca,
      cobrancasAnteriores: [...(pedido.cobrancasAnteriores ?? []), cobrancaTrocada(anterior, agora)],
    }
    const registro = `Forma de pagamento alterada de ${nomeDoMeio(anterior.meio)} para `
      + `${nomeDoMeio(cobranca.meio)} por ${ATENDENTE}. A cobrança anterior foi cancelada.`
    return mapear(estado, id, (c) => comMensagem(
      comMensagem({ ...c, pedido: novoPedido }, { dir: 'sistema', texto: registro }, { id: sistemaId, agora }),
      {
        dir: 'out', texto: textoDaCobranca(cobranca, novoPedido, estado.catalogo.cardapio),
        status: 'lida', automatica: true, regra: 'cobranca-pix',
      },
      { id: mensagemId, agora },
    ))
  },

  // Rodada 12 (issue #13): "Desfazer pagamento" da baixa marcada por engano.
  // Não é estorno (não entrou dinheiro para devolver) e não cancela o pedido:
  // a cobrança volta a esperar e fica a trilha, na conversa e no cadastro.
  // Nada sai para o cliente sozinho; se precisar, ela escreve.
  [acao.DESFAZER_PAGAMENTO]: (estado, {
    id, agora, motivo, mensagemId, notaId,
  }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const pedido = conversa?.pedido
    if (!podeDesfazerPagamento(pedido)) return estado
    const cobranca = desfazerPagamento(pedido.cobranca, agora, motivo)
    if (cobranca === pedido.cobranca) return estado
    const valor = pedido.cobranca.valorPago ?? pedido.cobranca.valor
    const texto = `Pagamento desfeito no pedido ${pedido.numero} por ${ATENDENTE}: `
      + `${moeda(valor)} por ${nomeDoMeio(pedido.cobranca.meio)}. Motivo: ${cobranca.pagamentoDesfeito.motivo}`
    const nota = { id: notaId, autor: ATENDENTE, em: dataHora(agora), texto }
    return mapear(estado, id, (c) => comMensagem({
      ...c,
      cliente: { ...c.cliente, notas: [nota, ...(c.cliente?.notas ?? [])] },
      pedido: { ...c.pedido, cobranca, estado: estadoDepoisDeDesfazer(c.pedido) },
    }, { dir: 'sistema', texto }, { id: mensagemId, agora }))
  },
}
