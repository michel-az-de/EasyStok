import { INSTANTE_INICIAL } from './catalogo'

const base = new Date(INSTANTE_INICIAL)
const min = (m) => new Date(base.getTime() - m * 60000).toISOString()
const mais = (m) => new Date(base.getTime() + m * 60000).toISOString()

// Conversas de exemplo. Nomes fictícios, situações tiradas da entrevista.
export const CONVERSAS_SEMENTE = [
  {
    id: 'c1',
    conta: 'cliente',
    nome: 'José Moretti',
    canal: 'WhatsApp',
    estado: 'Em atendimento',
    responsavel: 'Thatiane',
    janelaExpiraEm: mais(61),
    ultimaEm: min(3),
    atrasada: false,
    cliente: {
      desde: 'março de 2026',
      endereco: 'Rua Girassol, 412, apto 71, Vila Madalena, 05433-001',
      enderecoCapturado: null,
      pedidos: 7,
      tags: ['Gosta de lasanha', 'Massa al dente'],
      notas: [
        { autor: 'Thatiane', em: '14/09/2026 20:12', texto: 'Adorou a lasanha mas pediu a massa mais al dente. Reduzir 1 min no cozimento.' },
        { autor: 'Thatiane', em: '02/08/2026 19:40', texto: 'Mora no mesmo endereço da Maria Moretti. Mesma casa, cadastros separados.' },
      ],
    },
    pedido: {
      numero: '2026-0184', estado: 'pago', janela: 'j2', entregador: 'motoboy da casa',
      itens: [
        { sku: 'LAS-CLA', qtd: 1, obs: 'gratinada no forno' },
        { sku: 'EXT-PAR', qtd: 2, obs: '' },
      ],
    },
    mensagens: [
      { id: 'c1m1', dir: 'in', texto: 'Oi Thatiane, boa tarde! O que tem hoje?', em: min(22) },
      { id: 'c1m2', dir: 'out', texto: 'Oi José! Segue o cardápio de hoje. Tem lasanha clássica, pappardelle com ragu e os raviólis.', em: min(20), status: 'lida' },
      { id: 'c1m3', dir: 'in', texto: 'Quero uma lasanha clássica. Dá pra gratinar?', em: min(16) },
      { id: 'c1m4', dir: 'out', texto: 'Dá sim, sai gratinada. Confirma o endereço da Rua Girassol, 412, apto 71?', em: min(14), status: 'lida' },
      { id: 'c1m5', dir: 'in', texto: 'Isso mesmo. E manda um parmesão extra também, dois potes.', em: min(11) },
      { id: 'c1m6', dir: 'out', texto: 'Anotado. Já gerei seu pedido, segue o link de pagamento.', em: min(9), status: 'lida' },
      { id: 'c1m7', dir: 'in', texto: 'Paguei agora. Consegue entregar na hora do almoço?', em: min(3) },
    ],
  },
  {
    id: 'c2',
    conta: 'lead',
    nome: 'Patrícia Alencar',
    canal: 'Instagram',
    estado: 'Aberto',
    responsavel: null,
    janelaExpiraEm: null,
    ultimaEm: min(7),
    atrasada: true,
    cliente: {
      desde: null,
      endereco: null,
      enderecoCapturado: 'Av. Pompeia 1870, Perdizes, CEP 05022-001',
      pedidos: 0,
      tags: ['Chegou pelo Instagram'],
      notas: [],
    },
    pedido: null,
    mensagens: [
      { id: 'c2m1', dir: 'in', texto: 'Oi! Vi o post do ravióli de limão siciliano. Vocês entregam em Perdizes?', em: min(9) },
      { id: 'c2m2', dir: 'out', texto: 'Oi! Entrego sim. Me passa rua, número e CEP que eu já confirmo a área.', em: min(8), status: 'lida', automatica: true },
      { id: 'c2m3', dir: 'in', texto: 'Av. Pompeia 1870, Perdizes, CEP 05022-001', em: min(7) },
    ],
  },
  {
    id: 'c3',
    conta: 'cliente',
    nome: 'Laís Ferreira',
    canal: 'WhatsApp',
    estado: 'Em atendimento',
    responsavel: 'Thatiane',
    janelaExpiraEm: mais(14),
    ultimaEm: min(41),
    atrasada: false,
    cliente: {
      desde: 'junho de 2026',
      endereco: 'Rua Cardeal Arcoverde, 2.200, Pinheiros, 05408-003',
      enderecoCapturado: null,
      pedidos: 4,
      tags: ['Quis lasanha e não tinha', 'Prefere pedir na sexta'],
      notas: [
        { autor: 'Thatiane', em: '05/09/2026 18:02', texto: 'Pediu lasanha duas vezes quando não tinha. Avisar quando entrar na produção.' },
      ],
    },
    pedido: {
      numero: '2026-0181', estado: 'entrega', janela: 'j1', entregador: 'motoboy da casa',
      itens: [{ sku: 'RAV-ABO', qtd: 2, obs: 'molho à parte' }],
    },
    mensagens: [
      { id: 'c3m1', dir: 'in', texto: 'Oi Thati! Vou querer dois raviólis de abóbora pro almoço.', em: min(96) },
      { id: 'c3m2', dir: 'out', texto: 'Oi Laís! Anotado, molho à parte como sempre. Entrego entre 11h30 e 12h30.', em: min(94), status: 'lida' },
      { id: 'c3m3', dir: 'in', texto: 'Perfeito, obrigada!', em: min(41) },
    ],
  },
  {
    id: 'c4',
    conta: 'cliente',
    nome: 'Marcelo Teixeira',
    canal: 'Chat do site',
    estado: 'Aberto',
    responsavel: null,
    janelaExpiraEm: null,
    ultimaEm: min(52),
    atrasada: false,
    cliente: {
      desde: 'agosto de 2026',
      endereco: 'Rua Harmonia, 88, Vila Madalena, 05435-000',
      enderecoCapturado: null,
      pedidos: 2,
      tags: ['Intolerante a lactose'],
      notas: [
        { autor: 'Thatiane', em: '30/08/2026 12:20', texto: 'Intolerante a lactose. Não entra em campanha de lasanha nem de bechamel.' },
      ],
    },
    pedido: null,
    mensagens: [
      { id: 'c4m1', dir: 'in', texto: 'Boa tarde. Vocês têm alguma massa sem leite na receita?', em: min(52) },
    ],
  },
  {
    id: 'c5',
    conta: 'cliente',
    nome: 'Maria Moretti',
    canal: 'WhatsApp',
    estado: 'Encerrado',
    responsavel: 'Thatiane',
    janelaExpiraEm: null,
    ultimaEm: min(1490),
    atrasada: false,
    cliente: {
      desde: 'abril de 2026',
      endereco: 'Rua Girassol, 412, apto 71, Vila Madalena, 05433-001',
      enderecoCapturado: null,
      pedidos: 5,
      tags: ['Só pede ravióli', 'Pede às sextas'],
      notas: [
        { autor: 'Thatiane', em: '02/08/2026 19:41', texto: 'Mesmo endereço do José Moretti. Cadastros separados por proteção de dados.' },
      ],
    },
    pedido: {
      numero: '2026-0177', estado: 'entregue', janela: 'j3', entregador: 'motoboy da casa',
      itens: [{ sku: 'RAV-LIM', qtd: 1, obs: '' }],
    },
    mensagens: [
      { id: 'c5m1', dir: 'in', texto: 'Chegou certinho, obrigada!', em: min(1490) },
      { id: 'c5m2', dir: 'out', texto: 'Que bom, Maria! Obrigada pela preferência. Boa massa.', em: min(1488), status: 'lida' },
    ],
  },
  {
    id: 'c6',
    conta: 'cliente',
    nome: 'Rafael Nunes',
    canal: 'WhatsApp',
    estado: 'Em atendimento',
    responsavel: 'Thatiane',
    janelaExpiraEm: min(180),
    ultimaEm: min(1620),
    atrasada: false,
    cliente: {
      desde: 'maio de 2026',
      endereco: 'Rua Fradique Coutinho, 940, Pinheiros, 05416-011',
      enderecoCapturado: null,
      pedidos: 3,
      tags: ['Pede para o jantar'],
      notas: [
        { autor: 'Thatiane', em: '12/09/2026 21:05', texto: 'Some no meio do pedido. Confirmar antes de produzir.' },
      ],
    },
    pedido: null,
    mensagens: [
      { id: 'c6m1', dir: 'in', texto: 'Oi, boa noite! Tem pappardelle pra amanhã?', em: min(1626) },
      { id: 'c6m2', dir: 'out', texto: 'Oi Rafael! Tenho sim. Quer para o almoço ou para o jantar?', em: min(1620), status: 'lida' },
    ],
  },
  // Cliente pedindo direto no chat um item com saldo zero (Lasanha verde,
  // LAS-VER em infra/catalogo.js): sem esta conversa não dá para provar na
  // tela o automático recusando essa pergunta, só a venda manual do mesmo
  // item (achado do QA4, S1, rodada 3).
  {
    id: 'c7',
    conta: 'cliente',
    nome: 'Vinícius Andrade',
    canal: 'WhatsApp',
    estado: 'Aberto',
    responsavel: null,
    janelaExpiraEm: null,
    ultimaEm: min(6),
    atrasada: false,
    cliente: {
      desde: 'junho de 2026',
      endereco: 'Rua Wisard, 145, Vila Madalena, 05434-080',
      enderecoCapturado: null,
      pedidos: 4,
      tags: ['Pede lasanha verde'],
      notas: [],
    },
    pedido: null,
    mensagens: [
      { id: 'c7m1', dir: 'in', texto: 'Oi Thatiane! Separa uma lasanha verde pra mim pro jantar de hoje?', em: min(6) },
    ],
  },
]
