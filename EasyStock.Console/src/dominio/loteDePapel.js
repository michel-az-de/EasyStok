// US-042, D6, UC-04 E1: o que ela marca no papel durante a queda de conexão e
// o lote que a tela lança quando a conexão volta. Puro (sem React, sem
// reducer): só a régua de validação e o texto de cada lançamento,
// reaproveitando PASSOS/avisoDoPasso de esteira.js.
import { indiceDoPasso, avisoDoPasso, passoPorId } from './esteira'
import { pedidoEncerrado } from './pedido'
import { faixaDaJanela, faixaParaCliente } from './entrega'
import { entregadorResolvido } from './viagem'

// Os quatro passos que ela consegue marcar a caneta no canhoto (RN-33): a
// esteira também tem 'aguardando' e 'pago', mas os dois já aconteceram antes
// do canhoto sair impresso (US-042, cenário formal: "pedidos pagos e já
// impressos").
export const PASSOS_PAPEL = ['preparo', 'embalado', 'entrega', 'entregue']

// Foto de quais pedidos estavam abertos no instante em que a conexão caiu
// (UC-04 E1, "em qualquer passo depois do 2"): pago ou além, ainda não
// encerrado. Vira `conexao.pedidosAbertos` e é ESSA lista, não o estado
// atual, que decide quem entra no lote — um pedido só pago DEPOIS da queda
// nunca teve canhoto impresso pela fila automática (RN-27 exige o app rodando
// pra detectar o pagamento), então não é candidato a papel.
export function pedidosAbertosParaPapel(conversas) {
  return conversas
    .filter((c) => c.pedido && !pedidoEncerrado(c.pedido) && indiceDoPasso(c.pedido.estado) >= indiceDoPasso('pago'))
    .map((c) => c.pedido.numero)
}

// Conversas candidatas ao lote: só as que estavam na foto do instante da
// queda. Pedido cancelado durante a queda continua na lista (ela precisa VER
// que ele saiu, não pode sumir sozinho da tela) — quem recusa marcar nele é
// `validarSelecao`.
export function conversasDoLote(conversas, pedidosAbertos) {
  return conversas.filter((c) => c.pedido && pedidosAbertos.includes(c.pedido.numero))
}

// Sem retroceder e sem passo que não existe. Ir direto de 'pago' para
// 'entregue' é válido de propósito: ela só teve tempo de escrever o passo
// final no canhoto, não cada etapa (US-042, "lançados na tela em lote").
export function validarSelecao(pedido, alvoId) {
  if (!pedido || pedidoEncerrado(pedido)) return { ok: false, motivo: 'encerrado' }
  if (!PASSOS_PAPEL.includes(alvoId)) return { ok: false, motivo: 'passo-invalido' }
  if (indiceDoPasso(alvoId) < indiceDoPasso(pedido.estado)) return { ok: false, motivo: 'retrocesso' }
  return { ok: true, motivo: null }
}

// Monta os lançamentos prontos para aplicar: um por pedido com avanço real
// (alvo igual ao estado atual não lança nada — ela só confirmou que aquele
// não andou), na ordem da esteira, quem estava mais atrás primeiro. Cada um
// leva a MESMA frase que um avanço comum manda para aquele passo final —
// resumindo o passo em que parou, nunca uma mensagem por etapa (US-042: "as
// mensagens... uma vez só por pedido").
export function construirLancamentos(conversas, pedidosAbertos, selecoes, janelas, agora) {
  return conversasDoLote(conversas, pedidosAbertos)
    .map((c) => {
      const alvo = selecoes[c.id]
      if (!alvo || alvo === c.pedido.estado) return null
      if (!validarSelecao(c.pedido, alvo).ok) return null
      const faixaBruta = faixaDaJanela(janelas, c.pedido.janela)
      const faixa = faixaBruta ? faixaParaCliente(faixaBruta, agora) : faixaBruta
      // Integração 72 (US-040 x US-042): mesma régua de "quem leva" do
      // despacho (`entregadorResolvido`: escolhido antes, viagem "eu levo" ou
      // chamado achado). Sem ninguém definido, o lote não pergunta nem trava:
      // lança, e a mensagem única não cita entregador.
      const entregador = entregadorResolvido(c.pedido)
      return {
        conversaId: c.id,
        numero: c.pedido.numero,
        de: c.pedido.estado,
        para: alvo,
        mensagem: avisoDoPasso(alvo, { faixa, entregador }) ?? passoPorId(alvo)?.mensagemSemNome ?? null,
        ordem: indiceDoPasso(c.pedido.estado),
      }
    })
    .filter(Boolean)
    .sort((a, b) => (a.ordem - b.ordem) || (a.numero < b.numero ? -1 : 1))
}

// #1241 (F11): o mesmo lançamento no vocabulário da API. A máquina de estados do pedido no
// EasyStok não pula etapa (pago → preparando → pronto → entregue), então o salto que ela
// anotou no papel vira um passo por etapa. Sem a entrega marcada, vai de pronto direto para
// entregue (retirada ou entrega que ela não anotou), transição que o EasyStok aceita.
const STATUS_DA_API = { preparo: 'preparando', embalado: 'pronto', entrega: 'saiu_para_entrega', entregue: 'entregue' }

export function passosNaApi(de, para) {
  const inicio = PASSOS_PAPEL.includes(de) ? PASSOS_PAPEL.indexOf(de) + 1 : 0
  const fim = PASSOS_PAPEL.indexOf(para)
  if (fim < inicio) return []
  return PASSOS_PAPEL.slice(inicio, fim + 1)
    .filter((passo) => passo !== 'entrega' || para === 'entrega')
    .map((passo) => STATUS_DA_API[passo])
}
