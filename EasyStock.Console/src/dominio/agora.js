// #1608: as três filas do painel "Agora" da entrada do console, na ordem em
// que a dona trabalha: responder, cozinhar, despachar. Nenhuma conta nova: quem
// decide o que precisa dela é `motivoDePrecisar` (o mesmo do Balcão) e quem
// decide atraso e passo é `dominio/cozinha.js` (o mesmo da fila da Cozinha).
// Com extensão nos imports: a prova carrega este arquivo direto pelo Node.
import { motivoDePrecisar } from './automatico.js'
import { conversaEncerrada, estaBloqueada } from './conversa.js'
import { pedidosNaCozinha, situacaoDoCartao } from './cozinha.js'
import { faixaDaJanela, inicioDaFaixa } from './entrega.js'

const PESO_DO_TOM = { perigo: 0 }
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

export function filasDoAgora(conversas, agora, {
  automaticoPausado = {}, aberta = true, janelas = [], expediente = null,
} = {}) {
  const vivas = (conversas ?? []).filter((c) => !conversaEncerrada(c) && !estaBloqueada(c))
  const responder = vivas
    .map((c) => ({ c, motivo: motivoDePrecisar(c, agora, automaticoPausado[c.id] ?? false, aberta, janelas, expediente) }))
    .filter(({ motivo }) => motivo)
    .map(({ c, motivo }) => ({ id: c.id, nome: c.nome, rotulo: motivo.rotulo, texto: motivo.texto, tom: motivo.tom }))
    .sort((a, b) => (PESO_DO_TOM[a.tom] ?? 1) - (PESO_DO_TOM[b.tom] ?? 1))
  const pedidos = pedidosNaCozinha(vivas).map((c) => itemDoPedido(c, janelas, agora))
  return {
    responder,
    cozinhar: doPasso(pedidos, PASSOS_DE_COZINHAR),
    despachar: doPasso(pedidos, PASSOS_DE_DESPACHAR),
  }
}
