// Atendimentos antigos (rodada 10, achado P1.1, US-007: "ver a interação
// mais profundamente", "falta dados de histórico"). O fio de conversa e a
// aba Atendimentos do Histórico já sabem desenhar vários atendimentos
// encerrados dentro da mesma conversa, com divisor entre eles
// (`fronteirasDoFio`, `indiceInicioDoAtendimento`, decisão 46 "Encerrar v2");
// só faltava o dado. Este módulo sintetiza, de forma determinística (mesmo
// pedido gera sempre a mesma troca de mensagens), o atendimento fechado que
// haveria por trás de cada pedido antigo de `historicoPedidos.js`, no molde
// exato do resumo que `dominio/resumoAtendimento.js` e
// `aplicacao/casos/encerramento.js` já produzem para um encerramento ao
// vivo (mesmos campos, para a folha de "Ver resumo" renderizar igual).
//
// O pedido MAIS RECENTE de um cliente com pedido em aberto hoje é o "ao
// vivo" (espelhado em `conversa.pedido`, README de dados/): não vira
// atendimento antigo, e nenhum estado ainda em esteira (pago, aguardando,
// preparo, entrega) vira um também, mesmo fora do índice 0, por segurança.
//
// Texto novo (o dono corrigiu em 26/09: o nome dela é Thatiane, "Thati" no
// dia a dia; "Thatiane" só existe no código antigo e não é tocado aqui).
import { hashPara100 } from './avaliacaoSemente'
import { duracao, horaCurta } from '../dominio/formato'

const EM_ANDAMENTO = new Set(['pago', 'aguardando', 'preparo', 'entrega'])

const primeiroNome = (nome) => nome.split(' ')[0]

// "1× Lasanha clássica" -> "Lasanha clássica" (a casa fala em quantidade só
// no recibo automático; no bate-papo o cliente pede pelo nome do prato).
const semQuantidade = (itemTexto) => itemTexto.replace(/^\d+×\s*/, '')
const itensNatural = (itens) => itens.map(semQuantidade).join(' e ')

const escolher = (pool, semente) => pool[hashPara100(semente) % pool.length]

const SAUDACAO_PEDIDO = [
  (nome, itens) => `Oi Thati! Pode separar ${itens} pra mim?`,
  (nome, itens) => `Fala Thati, tudo bem? Quero ${itens} hoje.`,
  (nome, itens) => `Boa tarde! Dá pra mandar ${itens}?`,
  (nome, itens) => `Oi, aqui é o ${nome} de novo. Vou querer ${itens}.`,
  (nome, itens) => `Oi Thati, bateu a vontade de novo. Separa ${itens} pra mim?`,
]

const CONFIRMACAO = [
  (nome, itens, total) => `Fechado, ${nome}! ${itens}, dá ${total}. Já anoto.`,
  (nome, itens, total) => `Anotado! ${itens} por ${total}. Início do preparo já já.`,
  (nome, itens, total) => `Combinado, ${nome}. ${itens}, ${total} no total. Entro na produção.`,
]

const CANCELAMENTO_CLIENTE = [
  'Ai, vou ter que cancelar esse. Surgiu um perrengue aqui.',
  'Thati, desculpa, não vou conseguir esse pedido hoje. Pode cancelar?',
  'Preciso cancelar, mudou meu horário. Fica pra próxima.',
]
const CANCELAMENTO_CASA = [
  'Sem problema, cancelei aqui. Qualquer coisa me chama de novo.',
  'Tranquilo, já cancelei. Conto contigo na próxima.',
]

const AGENDAMENTO_CLIENTE = [
  'Pode deixar anotado pra entregar só daqui uns dias? Hoje não tem ninguém em casa.',
  'Fecha o pedido, mas só manda lá pra semana que vem, pode ser?',
]
const AGENDAMENTO_CASA = [
  'Pode ficar tranquilo, deixo anotado e entrego assim que combinarmos o dia certo.',
  'Fechado, guardo esse pedido e a gente combina a entrega mais pra frente.',
]

// Mensagem de cliente, nunca com pronome de gênero: a lista de clientes
// mistura homem, mulher e empresa, e o mesmo texto tem que servir para
// qualquer um.
const ELOGIO = [
  'Chegou tudo certinho, adorei! Já quero pedir de novo.',
  'Show, ficou uma delícia. Valeu, Thati!',
  'Perfeito como sempre. Vocês são demais.',
]
const RECLAMACAO = [
  'Olha, dessa vez não veio tão bem quanto de outras vezes.',
  'Demorou mais que o combinado, fiquei sem graça.',
  'Não ficou do jeito que eu esperava dessa vez.',
]
const DESCULPA = [
  'Sinto muito, vou ficar de olho nisso pro seu próximo pedido.',
  'Anotei aqui pra corrigir na próxima. Valeu por avisar.',
]
const AGRADECIMENTO_NEUTRO = [
  'Chegou certinho, valeu!',
  'Recebi, valeu!',
  'Beleza, chegou tudo. Até a próxima.',
]

const SITUACAO_VENDA = {
  entregue: { rotulo: 'Entregue', tom: 'ok' },
  cancelado: { rotulo: 'Cancelado', tom: 'perigo' },
  agendado: { rotulo: 'Agendado', tom: 'neutro' },
}

// Horário plausível de uma cozinha (almoço ou janta, as duas janelas que o
// catálogo já usa): 12h10 ou 19h15, escolhido pelo hash do próprio número do
// pedido, para o mesmo pedido cair sempre no mesmo horário.
function instanteBase(pedido) {
  const almoco = hashPara100(pedido.numero) % 2 === 0
  const hora = almoco ? '12:10' : '19:15'
  return new Date(`${pedido.em}T${hora}:00-03:00`).getTime()
}

// Monta a troca de mensagens de UM pedido fechado e o resumo equivalente ao
// que `casos/encerramento.js` gravaria se a Thatiane tivesse encerrado esse
// atendimento na hora. `numero` do resumo fica em branco aqui (`null`): a
// numeração AT é sequencial em toda a loja (rodada 5), então quem atribui o
// número final é `massaConversas.js`, depois de juntar TODOS os atendimentos
// de TODAS as conversas e ordenar por data.
function atendimentoDoPedido(conversaId, nomeCliente, pedido, indiceMensagemInicial) {
  const nome = primeiroNome(nomeCliente)
  const itens = itensNatural(pedido.itens)
  const total = pedido.total.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })
  const inicio = instanteBase(pedido)
  const passo = 3 * 60000

  const mensagens = [
    { dir: 'in', texto: escolher(SAUDACAO_PEDIDO, pedido.numero + 'a')(nome, itens), t: inicio },
    { dir: 'out', texto: escolher(CONFIRMACAO, pedido.numero + 'b')(nome, itens, total), t: inicio + passo, status: 'lida' },
  ]

  if (pedido.estado === 'cancelado') {
    mensagens.push({ dir: 'in', texto: escolher(CANCELAMENTO_CLIENTE, pedido.numero + 'c'), t: inicio + 2 * passo })
    mensagens.push({ dir: 'out', texto: escolher(CANCELAMENTO_CASA, pedido.numero + 'd'), t: inicio + 3 * passo, status: 'lida' })
  } else if (pedido.estado === 'agendado') {
    mensagens.push({ dir: 'in', texto: escolher(AGENDAMENTO_CLIENTE, pedido.numero + 'c'), t: inicio + 2 * passo })
    mensagens.push({ dir: 'out', texto: escolher(AGENDAMENTO_CASA, pedido.numero + 'd'), t: inicio + 3 * passo, status: 'lida' })
  } else {
    // Folga variável (10 a 59 passos, ~30 min a 3 h) para o agradecimento ou
    // reclamação não cair sempre no mesmo minuto redondo: sem isto, todo
    // atendimento sem cancelamento nem agendamento fechava com a mesma
    // duração exata, o que parece gerado, não uma cozinha de verdade.
    const folga = (10 + (hashPara100(pedido.numero + 'e') % 50)) * passo
    if (pedido.avaliacaoCliente === 'positiva') {
      mensagens.push({ dir: 'in', texto: escolher(ELOGIO, pedido.numero + 'c'), t: inicio + folga })
    } else if (pedido.avaliacaoCliente === 'negativa') {
      mensagens.push({ dir: 'in', texto: escolher(RECLAMACAO, pedido.numero + 'c'), t: inicio + folga })
      mensagens.push({ dir: 'out', texto: escolher(DESCULPA, pedido.numero + 'd'), t: inicio + folga + passo, status: 'lida' })
    } else {
      mensagens.push({ dir: 'in', texto: escolher(AGRADECIMENTO_NEUTRO, pedido.numero + 'c'), t: inicio + folga })
    }
  }

  const comId = mensagens.map((m, i) => ({
    id: `${conversaId}-ant-${pedido.numero}-${i}`,
    dir: m.dir,
    texto: m.texto,
    em: new Date(m.t).toISOString(),
    ...(m.status ? { status: m.status } : {}),
  }))

  const desde = comId[0].em
  const ultima = comId.at(-1)
  const encerradoEm = new Date(ultima.em).getTime() + 2 * 60000

  const cliente = comId.filter((m) => m.dir === 'in').length
  const voce = comId.filter((m) => m.dir === 'out').length
  const bruto = pedido.estado === 'cancelado' ? 0 : pedido.total
  const situacao = SITUACAO_VENDA[pedido.estado] ?? { rotulo: pedido.estado, tom: 'neutro' }

  const resumo = {
    numero: null, // preenchido por massaConversas.js na numeração global
    desde,
    ate: new Date(encerradoEm).toISOString(),
    duracaoTexto: duracao(encerradoEm - new Date(desde).getTime()),
    faixaHorario: `${horaCurta(desde)} a ${horaCurta(encerradoEm)}`,
    mensagens: { cliente, voce, automatico: 0, total: cliente + voce },
    receita: { bruto, estorno: 0, liquido: bruto, faltaReceber: 0 },
    vendas: [{
      numero: pedido.numero,
      itensTexto: pedido.itens.join(', '),
      meio: '—',
      valor: pedido.total,
      situacao: situacao.rotulo,
      situacaoTom: situacao.tom,
    }],
    vendasTotal: pedido.total,
    marcos: [],
    converteu: 'ja-cliente',
    avaliacaoCliente: pedido.avaliacaoCliente ?? null,
    autoavaliacao: null,
    anotacao: pedido.nota ?? '',
    guardarNota: true,
    encerradoPor: 'Thatiane',
    encerradoEm,
    ateIndice: indiceMensagemInicial + comId.length - 1,
    desfecho: 'manual',
    mensagemEnviada: false,
    avisoEmailEnviado: false,
    avisoSmsEnviado: false,
  }

  return { mensagens: comId, resumo }
}

// Gera, para UMA conversa, as mensagens antigas (a prepor às mensagens de
// hoje) e os resumos de atendimento correspondentes. `historico` é o array
// de `carregarHistorico(id)` (mais recente primeiro, já com
// `avaliacaoCliente`); `pedidoVivoNumero` é `conversa.pedido?.numero` (o
// pedido de hoje, quando existe, nunca vira atendimento antigo).
export function gerarAtendimentosAntigos({
  id, nome, historico, pedidoVivoNumero,
}) {
  const antigos = [...historico]
    .reverse() // mais antigo primeiro, ordem em que a conversa aconteceu
    .filter((p) => p.numero !== pedidoVivoNumero && !EM_ANDAMENTO.has(p.estado))

  const mensagens = []
  const atendimentos = []
  for (const pedido of antigos) {
    const { mensagens: doAtendimento, resumo } = atendimentoDoPedido(id, nome, pedido, mensagens.length)
    mensagens.push(...doAtendimento)
    atendimentos.push(resumo)
  }
  return { mensagens, atendimentos }
}
