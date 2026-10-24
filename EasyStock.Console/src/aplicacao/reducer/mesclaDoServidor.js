import { mensagemSoLocal } from '../../dominio/mensagem'
import { pedidoEncerrado } from '../../dominio/pedido'
import { mesmoConteudo } from '../igualdade'

// Modo API (F01). A conversa do servidor manda; do lado de cá só sobrevivem os
// balões da dona ainda sem resposta da API (enviando) ou recusados (falhou), para
// ela ver o que não saiu. O resto do estado local (rascunhos, filtros) fica.
// #1287: a confirmada pela API (`aguardandoSync`) também fica até um sync que a contenha,
// porque a leitura em voo pode ter saído antes de o servidor gravá-la (`mensagemSoLocal`).

// #1565: o pedido relido igual fica o mesmo objeto na tela, com a `leitura` de quando entrou. A ordem da
// releitura mais nova que o confirmou fica aqui, para a regra da #1548 continuar valendo.
const confirmadoNaLeitura = new WeakMap()
const leituraDe = (pedido) => (pedido ? Math.max(pedido.leitura ?? -Infinity, confirmadoNaLeitura.get(pedido) ?? -Infinity) : undefined)

// F03: sem pedido no servidor, a comanda que a dona monta (rascunho) fica. Com pedido, o do
// servidor manda, inclusive a vaga de entrega; o meio escolhido na tela vem junto.
// Pedido anterior já entregue ou cancelado não apaga a comanda nova que ela está montando.
export function pedidoMesclado(local, doServidor) {
  if (!doServidor) return local ?? null
  // #1548: leitura que saiu antes da que já está na tela chegou atrasada e não desfaz a mais nova.
  if (leituraDe(local) > doServidor.leitura) return local
  const rascunho = Boolean(local) && !local.pedidoId
  if (rascunho && pedidoEncerrado(doServidor)) return local
  if (!local || (!rascunho && local.pedidoId !== doServidor.pedidoId)) return doServidor
  return {
    ...doServidor,
    meio: doServidor.cobranca?.meio ?? local.meio ?? doServidor.meio,
    // #1683: só a tela sabe que o resumo não saiu; a releitura do mesmo pedido não apaga isso.
    // A recarga da ação que mandou link novo traz `false` e a marca cai.
    ...(local.resumoNaoEnviado && doServidor.resumoNaoEnviado === undefined && local.pedidoId === doServidor.pedidoId
      ? { resumoNaoEnviado: true } : null),
  }
}

export const mesmaLista = (a, b) => a.length === b.length && a.every((item, i) => item === b[i])

// O resumo da inbox não traz o cadastro; o cliente lido do EasyStok (#1276) fica enquanto
// a conversa seguir com o mesmo cliente.
const clienteDaApiMantido = (local, doServidor) =>
  local.cliente?.daApi && local.clienteId && local.clienteId === doServidor.clienteId

// #1565: `leitura` é só a ordem em que a consulta saiu; o mesmo pedido relido não é mudança.
const mesmoPedido = (a, b) => Boolean(a) && Boolean(b) && mesmoConteudo({ ...a, leitura: null }, { ...b, leitura: null })

// #2005 (console-08): o que a atendente acabou de mudar (assumir, devolver, encerrar, reabrir) e o servidor
// confirmou. Uma leitura que saiu antes dessa confirmação (`lidaEm` <= `confirmadaEm`) traz a conversa como era
// e não pode desfazê-la: ela não vale para estes campos. A primeira leitura que saiu depois solta a marca.
const SITUACAO_CONFIRMADA = ['estado', 'situacaoApi', 'assumidaPorUsuarioId', 'responsavel', 'pausaApi', 'passagem']
const situacaoLocal = (local) => Object.fromEntries(SITUACAO_CONFIRMADA.map((campo) => [campo, local[campo]]))
const semConfirmacao = (local) => {
  const { confirmadaEm: _confirmadaEm, ...resto } = local
  return resto
}

// #1565: a polling relê a cada 5 s. Leitura igual à que já está na tela devolve a MESMA conversa, para a
// tela parada não re-renderizar inteira.
// #1576: `semMensagens` é a conversa cujas mensagens o laço não conseguiu ler e não tinha em cache. Não é
// conversa vazia: ficam as mensagens da tela e o que se calcula delas (janela de 24 h, atraso).
export function mesclarDoServidor(local, lida, semMensagens = false, lidaEm = undefined) {
  if (!local) return lida
  const leituraAtrasada = local.confirmadaEm != null && lidaEm != null && lidaEm <= local.confirmadaEm
  const base = local.confirmadaEm != null && !leituraAtrasada ? semConfirmacao(local) : local
  const doServidor = {
    ...(semMensagens
      ? { ...lida, mensagens: local.mensagens, janelaExpiraEm: local.janelaExpiraEm, atrasada: local.atrasada, temAnteriores: local.temAnteriores }
      : lida),
    ...(leituraAtrasada ? situacaoLocal(local) : null),
  }
  const doServidorIds = new Set(doServidor.mensagens.map((m) => m.id))
  // #2005 (console-09): quem abriu as mensagens anteriores não as perde na volta de 5 s, que traz só a página mais
  // recente: o que já estava na tela e é mais velho que essa página fica (inclusive o trecho que a janela deslizou).
  // Vale para qualquer mensagem velha, também a saída que o servidor guarda como Falhou ou Pendente: ela já é do
  // servidor e fica no topo, em vez de virar "só local" no fim do fio com o Reenviar.
  const inicioDaPagina = semMensagens || !local.anterioresCarregadas ? null : Date.parse(doServidor.mensagens[0]?.em)
  const retidas = inicioDaPagina
    ? local.mensagens.filter((m) => !doServidorIds.has(m.id) && Date.parse(m.em) < inicioDaPagina)
    : []
  const idsRetidos = new Set(retidas.map((m) => m.id))
  const soLocais = local.mensagens.filter((m) => mensagemSoLocal(m) && !doServidorIds.has(m.id) && !idsRetidos.has(m.id))
  const mantido = clienteDaApiMantido(local, doServidor)
  const rascunhoDoNome = local.nomeDaDona && !doServidor.clienteId
  const lido = pedidoMesclado(local.pedido, doServidor.pedido)
  const pedido = mesmoPedido(local.pedido, lido) ? local.pedido : lido
  if (pedido !== lido && lido.leitura > leituraDe(pedido)) confirmadoNaLeitura.set(pedido, lido.leitura)
  const mesclada = {
    ...base,
    ...doServidor,
    ...(mantido ? { nome: local.nome, cliente: local.cliente } : {}),
    ...(rascunhoDoNome ? { nome: local.nome, nomeDaDona: true } : {}),
    pedido,
    mensagens: [...retidas, ...doServidor.mensagens, ...soLocais],
  }
  return mesmoConteudo(local, mesclada) ? local : mesclada
}
