// #1608: as três filas do painel "Agora" da entrada do console, na ordem em
// que a dona trabalha: responder, cozinhar, despachar. Nenhuma conta nova: quem
// decide o que precisa dela é `motivoDePrecisar` (o mesmo do Balcão) e quem
// decide atraso e passo é `dominio/cozinha.js` (o mesmo da fila da Cozinha).
// Com extensão nos imports: a prova carrega este arquivo direto pelo Node.
import { motivoDePrecisar } from './automatico.js'
import { conversaEncerrada, estaBloqueada, ordenarBalcao } from './conversa.js'
import { pedidosNaCozinha, situacaoDoCartao } from './cozinha.js'
import { faixaDaJanela, inicioDaFaixa } from './entrega.js'

const PASSOS_DE_COZINHAR = ['pago', 'preparo']
const PASSOS_DE_DESPACHAR = ['embalado', 'entrega']

function itemDoPedido(conversa, janelas, agora) {
  const { pedido } = conversa
  const faixa = faixaDaJanela(janelas, pedido.janela)
  const situacao = situacaoDoCartao(pedido, janelas, agora)
  return {
    id: conversa.id,
    nome: conversa.nome,
    numero: pedido.numero,
    faixa,
    inicio: faixa ? inicioDaFaixa(faixa, agora) : null,
    passo: pedido.estado,
    situacao: situacao.rotulo,
    atrasado: situacao.chave === 'atraso',
  }
}

// Atrasado primeiro, depois a janela que sai antes; sem janela fica por último.
const porUrgencia = (a, b) => Number(b.atrasado) - Number(a.atrasado)
  || (a.inicio ?? Infinity) - (b.inicio ?? Infinity)

const doPasso = (itens, passos) => itens
  .filter((i) => passos.includes(i.passo))
  .sort((a, b) => porUrgencia(a, b) || passos.indexOf(a.passo) - passos.indexOf(b.passo))

// #1611: no modo API a lista começa vazia e pode nunca chegar. Zero conversa sem
// sincronização concluída não é "ninguém esperando": a fila diz o que falta.
export function avisoDeResponder({ fonteApi, estado, total }) {
  if (!fonteApi || estado === 'ok' || total > 0) return null
  return estado === 'erro'
    ? 'Não consegui carregar as conversas. Abra o Balcão para tentar de novo.'
    : 'Carregando as conversas…'
}

export function filasDoAgora(conversas, agora, {
  automaticoPausado = {}, aberta = true, janelas = [], expediente = null,
} = {}) {
  const vivas = (conversas ?? []).filter((c) => !conversaEncerrada(c) && !estaBloqueada(c))
  const motivos = new Map(vivas.map((c) => [
    c.id, motivoDePrecisar(c, agora, automaticoPausado[c.id] ?? false, aberta, janelas, expediente),
  ]))
  // #1611: mesma ordem de urgência do Balcão, para os primeiros itens serem os de maior espera.
  const responder = ordenarBalcao(vivas.filter((c) => motivos.get(c.id)), agora, {
    automaticoPausado, janelas, aberta, expediente, aba: 'precisa',
  }).map((c) => {
    const motivo = motivos.get(c.id)
    return { id: c.id, nome: c.nome, rotulo: motivo.rotulo, texto: motivo.texto, tom: motivo.tom }
  })
  const pedidos = pedidosNaCozinha(vivas).map((c) => itemDoPedido(c, janelas, agora))
  return {
    responder,
    cozinhar: doPasso(pedidos, PASSOS_DE_COZINHAR),
    despachar: doPasso(pedidos, PASSOS_DE_DESPACHAR),
  }
}
