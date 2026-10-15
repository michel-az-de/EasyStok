// Massa de clientes gerada (rodada 10, achados P2 e "o que falta construir"
// 4: a base de 31 cadastros não sustenta nem de longe o próprio exemplo da
// dona na US-052, "filtrou lasanha, deu 50 clientes"). Gerador determinístico
// com semente fixa, não JSON gigante escrito à mão (o pedido do dono): a
// mesma semente sempre produz os mesmos clientes, os mesmos pedidos e as
// mesmas datas, então o build fica reprodutível e o arquivo-fonte continua
// pequeno mesmo gerando dezenas de cadastros novos.
//
// Cada cadastro novo já nasce no formato que `dados/massa-conversas.json`
// usa (mesmos campos que `massaConversas.js` converte) e no formato que
// `infra/historicoPedidos.js` usa (mesmos campos que `avaliacaoSemente.js`
// e `atendimentosAntigos.js` já sabem ler): as duas frentes de dados desta
// rodada (cadastro e histórico) nascem coerentes uma com a outra, contagem
// batendo cliente por cliente (mesma regra que `coerencia-da-massa.mjs` já
// cobra da massa antiga).
import { CARDAPIO, RESTRICOES, MOTIVOS_BLOQUEIO } from './catalogo'
import { comAvaliacoes } from './avaliacaoSemente'

// PRNG pequeno e determinístico (mulberry32): sem dependência nova, sempre a
// mesma sequência para a mesma semente.
function criarSemente(valor) {
  let estado = valor
  return function sorteio() {
    estado |= 0
    estado = (estado + 0x6D2B79F5) | 0
    let t = Math.imul(estado ^ (estado >>> 15), 1 | estado)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}
const sorteio = criarSemente(20260926)
const umDe = (lista) => lista[Math.floor(sorteio() * lista.length)]
const entre = (min, max) => min + Math.floor(sorteio() * (max - min + 1))
const chance = (p) => sorteio() < p

// Nunca dois cadastros novos com o mesmo nome completo: sem isso dois
// "Fulano de Tal" diferentes confundiam a Thatiane na lista do Balcão.
const nomesUsados = new Set()
function nomeUnico(primeiros, sobrenomes) {
  let tentativa
  do {
    tentativa = `${umDe(primeiros)} ${umDe(sobrenomes)}`
  } while (nomesUsados.has(tentativa))
  nomesUsados.add(tentativa)
  return tentativa
}

const PRIMEIROS_NOMES = [
  'Beatriz', 'Caio', 'Débora', 'Elias', 'Fernanda', 'Gustavo', 'Helena', 'Igor', 'Joana', 'Kaique',
  'Larissa', 'Marcos', 'Nicole', 'Otávio', 'Priscila', 'Rodrigo', 'Sabrina', 'Tiago', 'Úrsula', 'Vitor',
  'Wanda', 'Yasmin', 'Alan', 'Bruna', 'Cauê', 'Daniela', 'Emerson', 'Flávia', 'Gabriel', 'Hilda',
  'Ivo', 'Jéssica', 'Leonardo', 'Mônica', 'Nelson', 'Olívia', 'Pedro', 'Regina', 'Samuel', 'Tereza',
]
const SOBRENOMES = [
  'Albuquerque', 'Barreto', 'Cavalcanti', 'Dornelles', 'Esteves', 'Farah', 'Guerreiro', 'Homem',
  'Iglesias', 'Junqueira', 'Klein', 'Loureiro', 'Machado', 'Nogueira', 'Osório', 'Pontes', 'Quinteiro',
  'Ribas', 'Sabino', 'Tavares', 'Ulhôa', 'Valadares', 'Werneck', 'Xavier', 'Zambelli', 'Abreu',
  'Bezerra', 'Carrijo', 'Dutra', 'Estanislau', 'Ferraz', 'Gouveia', 'Hirata', 'Jatobá', 'Kock',
  'Lacerda', 'Mesquita', 'Nakamura', 'Oliveira Filho', 'Prestes',
]

const ENDERECOS_ATENDIDOS = [
  ['Rua Purpurina', 'Vila Madalena', '05435'],
  ['Rua Girassol', 'Vila Madalena', '05433'],
  ['Rua Inácio Pereira da Rocha', 'Pinheiros', '05408'],
  ['Rua Teodoro Sampaio', 'Pinheiros', '05416'],
  ['Av. Pompeia', 'Perdizes', '05022'],
]
const ENDERECOS_FORA = [
  ['Rua Tuiuti', 'Tatuapé', '03310'],
  ['Rua Joaquim Antunes', 'Moema', '04041'],
  ['Av. Cangaíba', 'Vila Matilde', '03810'],
  ['Rua Voluntários da Pátria', 'Santana', '02010'],
]

const PRECO_POR_NOME = Object.fromEntries(CARDAPIO.map((item) => [item.nome, item.preco]))
const NOMES_PRATOS = CARDAPIO.filter((item) => item.sku !== 'EXT-PAR').map((item) => item.nome)
const NOME_EXTRA = CARDAPIO.find((item) => item.sku === 'EXT-PAR').nome // "Parmesão ralado (extra)"
// Mesma convenção de historicoPedidos.js: o extra aparece sem o
// parêntese na linha do pedido ("2× Parmesão ralado", não "(extra)").
const NOME_EXTRA_CURTO = NOME_EXTRA.replace(' (extra)', '')
const LASANHAS = ['Lasanha clássica', 'Lasanha verde']

const CANAIS = ['WhatsApp', 'Instagram', 'Chat do site']

const NOTAS_RESTRICAO = {
  Lactose: 'Sem lactose. Ajustar receita antes de produzir.',
  Glúten: 'Sem glúten. Confirmar sempre antes de fechar o pedido.',
  Celíaco: 'Celíaco na família. Zero contaminação cruzada.',
  Vegano: 'Vegano. Nada de queijo nem manteiga na receita.',
  Alergia: 'Alergia alimentar registrada. Perguntar antes de qualquer novidade.',
}

const NOME_DO_MES = {
  '2026-01': 'janeiro de 2026', '2026-02': 'fevereiro de 2026', '2026-03': 'março de 2026',
  '2026-04': 'abril de 2026', '2026-05': 'maio de 2026', '2026-06': 'junho de 2026',
  '2026-07': 'julho de 2026', '2026-08': 'agosto de 2026', '2026-09': 'setembro de 2026',
}

let contadorPedido = 199 // continua depois do maior número da massa antiga (2026-0199)
const proximoNumero = () => {
  contadorPedido += 1
  return `2026-${String(contadorPedido).padStart(4, '0')}`
}

const DIA_MS = 86400000
// 20/09/2026: 2 dias antes do INSTANTE_INICIAL do catálogo (22/09), para um
// pedido gerado nunca cair "hoje" e se confundir com um pedido ao vivo.
const TETO_DATA_MS = Date.UTC(2026, 8, 20)

// Datas SEMPRE crescentes (nunca duas iguais, nunca fora de ordem): gera por
// passo cumulativo a partir de `mesInicio`, em vez de mês fixo com dia
// aleatório, porque dia aleatório dentro do mesmo mês podia inverter a
// ordem cronológica de dois pedidos vizinhos.
function datasCrescentes(quantidade, mesInicio) {
  const inicioMs = Date.UTC(2026, mesInicio, entre(2, 20))
  const passoBase = Math.max((TETO_DATA_MS - inicioMs) / quantidade, DIA_MS)
  const datas = []
  let cursor = inicioMs
  for (let i = 0; i < quantidade; i += 1) {
    cursor += passoBase * (0.6 + sorteio() * 0.8)
    datas.push(Math.min(cursor, TETO_DATA_MS))
  }
  for (let i = 1; i < datas.length; i += 1) {
    if (datas[i] <= datas[i - 1]) datas[i] = datas[i - 1] + DIA_MS
  }
  return datas.map((ms) => new Date(ms).toISOString().slice(0, 10))
}

// Gera os N pedidos (mais recente primeiro, igual ao resto de
// historicoPedidos.js) de um cliente, espalhados a partir de `mesInicio`.
// `forcarLasanha` garante ao menos um pedido com lasanha (achado
// P1.3/US-052: a busca por "lasanha" precisa achar gente de sobra).
function gerarPedidosDoCliente({
  quantidade, mesInicio, forcarLasanha, tagRestricao,
}) {
  const indiceLasanha = forcarLasanha ? entre(0, quantidade - 1) : -1
  const datas = datasCrescentes(quantidade, mesInicio)
  const pedidos = []
  for (let i = 0; i < quantidade; i += 1) {
    const em = datas[i]
    const prato = i === indiceLasanha ? umDe(LASANHAS) : umDe(NOMES_PRATOS)
    const comExtra = chance(0.25)
    const itens = [`1× ${prato}`]
    let total = PRECO_POR_NOME[prato]
    if (comExtra) { itens.push(`1× ${NOME_EXTRA_CURTO}`); total += PRECO_POR_NOME[NOME_EXTRA] }
    // Estado majoritariamente entregue; uma fatia cancelada e uma fatia
    // agendada (achado "mais variedade de estado", item 5) só entre os
    // pedidos que não são o mais recente (esse seria o "ao vivo").
    let estado = 'entregue'
    if (i < quantidade - 1) {
      if (chance(0.06)) estado = 'cancelado'
      else if (chance(0.03)) estado = 'agendado'
    }
    let nota = null
    if (estado === 'agendado') nota = 'Fechou antecipado, entrega combinada só pro dia certo.'
    else if (tagRestricao && chance(0.3)) nota = NOTAS_RESTRICAO[tagRestricao]
    pedidos.push({
      numero: proximoNumero(), em, total, estado, itens, nota,
    })
  }
  return pedidos.reverse() // mais recente primeiro
}

function enderecoDe(lista) {
  const [rua, bairro, prefixoCep] = umDe(lista)
  const numero = entre(20, 1200)
  const cepFinal = String(entre(0, 999)).padStart(3, '0')
  return `${rua}, ${numero}, ${bairro}, ${prefixoCep}-${cepFinal}`
}

// Um cadastro com pedido (recorrente, cliente da casa, bloqueado ou fora de
// área que já foi cliente). Todos nascem "Encerrado" e sem pedido ao vivo
// hoje: são profundidade de histórico, não fila de hoje (quem precisa da
// Thatiane continua sendo só a massa de 31 já existente). O fio de hoje fica
// vazio de propósito: `massaConversas.js` prepõe as mensagens antigas
// (`atendimentosAntigos.js`) a partir do próprio `historico`, e essas já
// contam a conversa inteira.
function gerarClienteComPedido(id, tipo, indice) {
  const nome = nomeUnico(PRIMEIROS_NOMES, SOBRENOMES)
  const cadastroId = `cad-${id}`
  const quantidade = tipo === 'cliente-da-casa' ? entre(8, 14)
    : tipo === 'bloqueado' || tipo === 'fora-de-area' ? entre(2, 4)
      : entre(3, 7)
  const mesInicio = tipo === 'cliente-da-casa' ? 0 : entre(0, 5)
  const forcarLasanha = chance(0.8)
  const restricao = chance(0.18) ? umDe(RESTRICOES) : null

  const historico = gerarPedidosDoCliente({
    quantidade, mesInicio, forcarLasanha, tagRestricao: restricao,
  })

  const tags = []
  if (tipo === 'cliente-da-casa') tags.push('Cliente da casa')
  if (tipo === 'fora-de-area') tags.push('Fora da área de entrega')
  if (tipo === 'bloqueado') tags.push('Bloqueado')
  if (restricao) tags.push(NOTAS_RESTRICAO[restricao].split('.')[0])
  if (tags.length === 0) tags.push(umDe(['Pede sempre a mesma massa', 'Gosta de experimentar novidade', 'Pede pro fim de semana']))

  const notas = []
  // Nota escrita perto do pedido mais recente (historico[0], mais recente
  // primeiro): "AAAA-MM-DD" vira "DD/MM/AAAA HH:MM", mesma gramática que o
  // resto da massa já usa em cliente.notas.
  const [anoNota, mesNota, diaNota] = historico[0].em.split('-')
  const horaNota = String(entre(9, 21)).padStart(2, '0')
  const dataNota = `${diaNota}/${mesNota}/${anoNota} ${horaNota}:${String(entre(0, 59)).padStart(2, '0')}`
  if (tipo === 'bloqueado') {
    notas.push({ autor: 'Thatiane', em: dataNota, texto: `${umDe(MOTIVOS_BLOQUEIO)}. Bloqueado em todos os canais.` })
  } else if (tipo === 'fora-de-area') {
    notas.push({ autor: 'Thatiane', em: dataNota, texto: 'Já foi cliente daqui, mas o endereço saiu da área que a casa entrega hoje.' })
  } else if (restricao) {
    notas.push({ autor: 'Thatiane', em: dataNota, texto: NOTAS_RESTRICAO[restricao] })
  } else if (tipo === 'cliente-da-casa') {
    notas.push({ autor: 'Thatiane', em: dataNota, texto: 'Cliente antigo, sempre volta. Trata como cliente da casa.' })
  }

  const enderecos = tipo === 'fora-de-area' ? ENDERECOS_FORA : ENDERECOS_ATENDIDOS
  const desde = NOME_DO_MES[historico.at(-1).em.slice(0, 7)]

  return {
    conversa: {
      id,
      cadastroId,
      conta: 'cliente',
      nome,
      canal: umDe(CANAIS),
      estado: 'Encerrado',
      responsavel: 'Thatiane',
      janelaMinutos: null,
      ultimaMinutosAtras: 20000 + indice * 37, // bem antigo, sem atividade hoje
      atrasada: false,
      cliente: {
        desde,
        endereco: enderecoDe(enderecos),
        enderecoCapturado: null,
        telefone: `(11) 9${String(7000 + indice).padStart(4, '0')}-${String(entre(1000, 9999))}`,
        pedidos: historico.length,
        tags,
        notas,
      },
      pedido: null,
      mensagens: [],
    },
    historico: comAvaliacoes(historico),
  }
}

// Lead puro (tipo "novo", 0 pedidos): mesmo molde da semente, só o primeiro
// contato de hoje, sem histórico nenhum.
const ABERTURAS_LEAD = [
  'Oi! Vocês entregam por aqui? Vi o perfil de vocês agora.',
  'Boa tarde! Queria saber o que tem no cardápio hoje.',
  'Oi, gostaria de saber como funciona pra fazer um pedido.',
]

function gerarLead(id, indice) {
  const nome = nomeUnico(PRIMEIROS_NOMES, SOBRENOMES)
  return {
    conversa: {
      id,
      cadastroId: `cad-${id}`,
      conta: 'lead',
      nome,
      canal: umDe(CANAIS),
      estado: 'Aberto',
      responsavel: null,
      janelaMinutos: null,
      ultimaMinutosAtras: entre(5, 400),
      atrasada: false,
      cliente: {
        desde: null,
        endereco: null,
        enderecoCapturado: null,
        telefone: `(11) 9${String(8000 + indice).padStart(4, '0')}-${String(entre(1000, 9999))}`,
        pedidos: 0,
        tags: ['Chegou hoje'],
        notas: [],
      },
      pedido: null,
      mensagens: [
        { id: `${id}m1`, dir: 'in', texto: umDe(ABERTURAS_LEAD), minutosAtras: entre(5, 400) },
      ],
    },
    historico: [],
  }
}

// Contagem (achado 4, US-052): a dona filtrou lasanha e achou 50 clientes,
// só nesse um filtro. 6 cliente-da-casa + 30 recorrente + 7 bloqueado + 7
// fora-de-área = 50 cadastros com pedido, girando a maioria com lasanha no
// histórico, mais 10 leads novos para o tipo "novo" também crescer.
const PLANO = [
  ...Array(6).fill('cliente-da-casa'),
  ...Array(30).fill('recorrente'),
  ...Array(7).fill('bloqueado'),
  ...Array(7).fill('fora-de-area'),
]

const CONVERSAS = []
const HISTORICO = {}
PLANO.forEach((tipo, indice) => {
  const id = `g${indice + 1}`
  const { conversa, historico } = gerarClienteComPedido(id, tipo, indice)
  CONVERSAS.push(conversa)
  HISTORICO[id] = historico
})
for (let i = 0; i < 10; i += 1) {
  const id = `g${PLANO.length + i + 1}`
  const { conversa } = gerarLead(id, i)
  CONVERSAS.push(conversa)
}

export const CONVERSAS_GERADAS = CONVERSAS
export const HISTORICO_GERADO = HISTORICO
