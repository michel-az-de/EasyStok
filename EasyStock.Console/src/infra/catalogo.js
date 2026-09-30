// Catálogo da casa. Tudo aqui é dado configurável: canal, cardápio, janela de
// entrega, resposta rápida e modelo. Nenhum componente conhece esses valores
// por dentro, todos recebem por parâmetro.
import { semearRespostasProntas } from '../dominio/respostas.js'

// Canal é dado, não código. Cada linha desta tabela é a regra real do canal, e
// o domínio lê daqui em vez de guardar uma lista por dentro. Canal novo (iFood,
// Telegram) entra acrescentando uma linha, sem tocar em componente nenhum.
//
//   temJanela    janela de resposta livre depois da última mensagem do cliente
//   aceitaModelo modelo aprovado pela mensageria fura a janela fechada
//   aceitaMidia  o que a casa consegue mandar por ali ('arquivo' é PDF/
//                documento; imagem entra por 'foto', rodada 7, frente Anexos)
//   temFoto      o canal entrega foto de perfil do cliente
export const CANAIS = [
  {
    nome: 'WhatsApp',
    icone: 'whatsapp',
    temJanela: true,
    horasJanela: 24,
    aceitaModelo: true,
    aceitaMidia: {
      foto: true, figurinha: true, audio: true, arquivo: true,
    },
    temFoto: true,
    explicacao: [
      'Janela de 24 h contada da última mensagem do cliente.',
      'Fora da janela só sai modelo aprovado. Aceita foto, figurinha, áudio e arquivo.',
    ],
  },
  {
    nome: 'Instagram',
    icone: 'instagram',
    temJanela: true,
    horasJanela: 24,
    aceitaModelo: false,
    aceitaMidia: {
      foto: true, figurinha: false, audio: true, arquivo: false,
    },
    temFoto: true,
    explicacao: [
      'Janela de 24 h, sem modelo aprovado para furar.',
      'Fora da janela, espera o cliente voltar. Sem figurinha nem arquivo (documento).',
    ],
  },
  {
    nome: 'Chat do site',
    icone: 'globo',
    temJanela: false,
    horasJanela: 0,
    aceitaModelo: false,
    aceitaMidia: {
      foto: false, figurinha: false, audio: false, arquivo: false,
    },
    temFoto: false,
    explicacao: [
      'Sem janela: dá para escrever a qualquer hora, só texto e link.',
      'Sem foto de perfil nem anexo. Peça o WhatsApp depois.',
    ],
  },
]

export const NOMES_DE_CANAL = CANAIS.map((c) => c.nome)

export const ESTADOS_CONVERSA = ['Aberto', 'Em atendimento', 'Encerrado']

export const LINHAS_PRODUTO = {
  servir: { rotulo: 'Para servir', dica: 'Sai quente, pronto para comer' },
  casa: { rotulo: 'Preparar em casa', dica: 'Refrigerado, com instrução de finalização' },
}

export const CARDAPIO = [
  { sku: 'LAS-CLA', nome: 'Lasanha clássica', linha: 'servir', porcao: '800 g', preco: 85, estoque: 4 },
  { sku: 'LAS-VER', nome: 'Lasanha verde', linha: 'servir', porcao: '800 g', preco: 89, estoque: 0 },
  { sku: 'RAV-LIM', nome: 'Ravióli de limão siciliano', linha: 'casa', porcao: '500 g', preco: 62, estoque: 6 },
  { sku: 'RAV-ABO', nome: 'Ravióli de abóbora com alho-poró', linha: 'casa', porcao: '500 g', preco: 62, estoque: 3 },
  { sku: 'TOR-COS', nome: 'Tortéi de costela', linha: 'casa', porcao: '500 g', preco: 68, estoque: 2 },
  { sku: 'PAP-RAG', nome: 'Pappardelle com ragu de costela', linha: 'servir', porcao: '600 g', preco: 78, estoque: 5 },
  { sku: 'EXT-PAR', nome: 'Parmesão ralado (extra)', linha: 'casa', porcao: '80 g', preco: 14, estoque: 12 },
]

// Rodada 13 (issue #42): campos novos por janela para a tela de configuração
// da Thati (dominio/entrega.js, `criarJanela`/`editarJanela`). `horaInicio`/
// `horaFim` são a fonte, `faixa` continua igual (todo o resto do app já lê
// dela) porque nasce de `faixaDeHorarios(horaInicio, horaFim)`. `corteMinutos`
// repete o chão de sempre (`MINUTOS_DE_CORTE`, 60) para não mudar o
// comportamento medido hoje; `diasSemana` todo dia (`TODOS_OS_DIAS`);
// `capacidadePorLinha: null` mantém a Q2 desligada (proposta ajustável,
// ver dominio/entrega.js).
export const JANELAS_ENTREGA = [
  {
    id: 'j1', horaInicio: '11:30', horaFim: '12:30', faixa: '11h30 às 12h30',
    capacidade: 4, ocupadas: 4, corteMinutos: 60, diasSemana: [0, 1, 2, 3, 4, 5, 6],
    capacidadePorLinha: null, ativa: true,
  },
  {
    id: 'j2', horaInicio: '12:30', horaFim: '13:30', faixa: '12h30 às 13h30',
    capacidade: 4, ocupadas: 2, corteMinutos: 60, diasSemana: [0, 1, 2, 3, 4, 5, 6],
    capacidadePorLinha: null, ativa: true,
  },
  {
    id: 'j3', horaInicio: '18:30', horaFim: '19:30', faixa: '18h30 às 19h30',
    capacidade: 5, ocupadas: 1, corteMinutos: 60, diasSemana: [0, 1, 2, 3, 4, 5, 6],
    capacidadePorLinha: null, ativa: true,
  },
  {
    id: 'j4', horaInicio: '19:30', horaFim: '20:30', faixa: '19h30 às 20h30',
    capacidade: 5, ocupadas: 0, corteMinutos: 60, diasSemana: [0, 1, 2, 3, 4, 5, 6],
    capacidadePorLinha: null, ativa: true,
  },
]

// Chão do respiro (RN-22, "pelo menos 40 minutos"): valor inicial do
// catálogo, ajustável na tela de Janelas sem nunca descer de 40
// (`ajustarRespiroMinimo`, dominio/entrega.js).
export const RESPIRO_MINUTOS_PADRAO = 40

export const janelaPorId = (id) => JANELAS_ENTREGA.find((j) => j.id === id) ?? null

export const RESPOSTAS_RAPIDAS = [
  { cat: 'Cardápio', titulo: 'Enviar cardápio', texto: 'Olá! Segue nosso cardápio de hoje. Me chama quando escolher que eu já anoto.' },
  { cat: 'Cardápio', titulo: 'Item esgotado', texto: 'Esse hoje encerrou, produzi pouco. Posso te sugerir outra massa do cardápio?' },
  { cat: 'Entrega', titulo: 'Janelas do dia', texto: 'Hoje tenho entrega das 12h30 às 13h30 e das 18h30 às 19h30. Qual fica melhor?' },
  // Achado 5, P2 (banca 10): este texto é uma OFERTA da dona (encomenda
  // agendada), diferente da fala automática de fora de área que o agente
  // manda sozinho (dominio/areaEntrega.js#respostaDeAreaFora, com {nome} e
  // Thatiane). Título deixa isso claro, para ela não achar que editar aqui
  // muda o que o automático fala.
  { cat: 'Entrega', titulo: 'Fora da área, oferta de retirada', texto: 'Ainda não entrego nessa região. Se quiser, trabalho com encomenda agendada para retirada.' },
  // Item E (US-009, critério de aceite literal): "o texto é enviado já
  // preenchido com nome do cliente e dados do pedido em curso". Das 11
  // respostas de fábrica, nenhuma citava {pedido} antes desta rodada.
  { cat: 'Pagamento', titulo: 'Enviar cobrança', texto: 'Já gerei seu pedido {pedido}. Segue o link de pagamento, assim que cair eu começo o preparo.' },
  { cat: 'Pós-venda', titulo: 'Agradecer', texto: 'Obrigada pela preferência! Se puder, me conta o que achou. Boa massa.' },
]

export const MODELOS = [
  {
    nome: 'retomada_pedido', categoria: 'Utilidade', aprovado: true,
    texto: 'Oi {{1}}, aqui é a Casa da Baba. Vi que seu pedido ficou em aberto. Quer que eu retome?',
  },
  {
    nome: 'novidade_cardapio', categoria: 'Marketing', aprovado: true,
    texto: 'Oi {{1}}, entrou {{2}} no cardápio da Casa da Baba. Quer provar?',
  },
]

// Templates da casa. Texto livre com variável, usados DENTRO da janela.
// Diferente dos modelos aprovados, que só existem para furar a janela fechada.
export const TEMPLATES_INTERNOS = [
  {
    id: 'confirmar-janela',
    nome: 'Confirmar janela de entrega',
    grupo: 'Entrega',
    texto: 'Oi {nome}! Fechei sua entrega para {faixa}. Pode ser?',
  },
  {
    id: 'sugerir-acompanhamento',
    nome: 'Sugerir acompanhamento',
    grupo: 'Venda',
    texto: 'Oi {nome}, quer um parmesão ralado à parte para finalizar? '
      + 'Vai bem com o que você pediu.',
  },
  {
    id: 'instrucao-finalizacao',
    nome: 'Como finalizar em casa',
    grupo: 'Pós-venda',
    texto: 'Oi {nome}! Para finalizar em casa: forno a 180 graus por 25 minutos, '
      + 'coberto com papel-alumínio nos primeiros 15. Tira o papel no fim para gratinar.',
  },
  {
    id: 'reserva',
    nome: 'Reservar do próximo dia de produção',
    grupo: 'Venda',
    texto: 'Oi {nome}! Hoje encerrou, mas produzo de novo nesta semana. '
      + 'Quer que eu reserve uma porção no seu nome?',
  },
  {
    id: 'atraso',
    nome: 'Avisar atraso',
    grupo: 'Entrega',
    texto: 'Oi {nome}, sua entrega vai sair um pouco depois do combinado. '
      + 'Prefiro atrasar a mandar massa fora do ponto. Já te aviso quando sair.',
  },
  // Item E (US-009): segunda resposta de fábrica com {pedido} de verdade
  // (a primeira é "Enviar cobrança", acima em RESPOSTAS_RAPIDAS).
  {
    id: 'status-pedido',
    nome: 'Status do pedido',
    grupo: 'Entrega',
    texto: 'Oi {nome}! Seu pedido {pedido} está a caminho.',
  },
]

// Biblioteca de respostas prontas (rodada 7, pedido do dono 24/09/2026): junta
// resposta rápida e texto interno num catálogo só, editável e arquivável pela
// tela (dominio/respostas.js). Semente gerada uma vez só, aqui; dali para a
// frente quem manda é `estado.catalogo.respostasProntas` no reducer.
export const RESPOSTAS_PRONTAS = semearRespostasProntas(RESPOSTAS_RAPIDAS, TEMPLATES_INTERNOS)

// Motivos de bloqueio (RN-14). Lista curta e do vocabulário dela: o que aparece
// no menu é o que ela já falou em voz alta. "Outro motivo" existe para não
// empurrar o caso real para uma gaveta errada.
export const MOTIVOS_BLOQUEIO = [
  'Calote: recebeu e contestou o pagamento',
  'Cancelou em cima da janela agendada, mais de uma vez',
  'Ofensa ou ameaça no atendimento',
  'Tentativa de golpe ou pedido falso',
  'Outro motivo',
]

export const INSTANTE_INICIAL = '2026-09-22T11:05:00-03:00'

// ---------------------------------------------------------------------------
// Rodada 2 · adicionais. Acrescentado no fim do arquivo.
//
// Ela vende parmesão e molho à parte e combina isso por texto hoje, o que cobra
// errado (áudio 02). Aqui o adicional é um SKU do próprio cardápio: tem preço,
// porção e saldo como qualquer item, e baixa estoque igual. Nada de lista
// paralela que ninguém atualiza.
// ---------------------------------------------------------------------------

export const ADICIONAIS = [
  { sku: 'EXT-MOL', nome: 'Molho de tomate rústico (extra)', linha: 'casa', porcao: '300 g', preco: 22, estoque: 7 },
  { sku: 'EXT-PAO', nome: 'Pão de alho da casa (extra)', linha: 'casa', porcao: '4 unidades', preco: 18, estoque: 5 },
]

export const ADICIONAIS_POR_SKU = {
  'LAS-CLA': ['EXT-PAR', 'EXT-PAO'],
  'LAS-VER': ['EXT-PAR', 'EXT-PAO'],
  'RAV-LIM': ['EXT-PAR', 'EXT-MOL'],
  'RAV-ABO': ['EXT-PAR', 'EXT-MOL'],
  'TOR-COS': ['EXT-PAR', 'EXT-MOL'],
  'PAP-RAG': ['EXT-PAR', 'EXT-PAO'],
}

// ---------------------------------------------------------------------------
// Rodada 5 · passo zero da direção visual (seção 8). Catálogo novo que a
// Cobrança (seção 4), a Ficha (seção 2), a Comanda (seção 3) e as Entregas
// (seção 6) vão ler. Nenhum componente conhece provedor nem fórmula por
// dentro: só lê a tabela e chama a interface (seção 4, "Onde o estudo do
// arquiteto encaixa").
// ---------------------------------------------------------------------------

// Pedido do dono (23/09/2026, ao ver a rodada 4): "precisamos ver o que o
// Mercado Pago oferece para gerar links de cobrança e receber por cartão,
// Pix, VR, VA". O estudo (decisao 18, secao 3) mediu: Pix e cartão de
// crédito por link são produtos do Mercado Pago (Pix dinâmico e Checkout
// Pro), os dois emitidos ONLINE com prazo de validade. VR e VA o Mercado
// Pago só aceita na maquininha (Point), NUNCA online (secao 3.2 do estudo:
// "Checkout Pro lista cartão de crédito ou débito, Pix, boleto... "). Por
// isso "vale-refeicao" fica ativo com o mesmo comportamento de "maquininha"
// (cobra na entrega, sem link), e o rótulo diz a verdade em vez de prometer
// "em breve" um online que o provedor não oferece.
//
// AQUI ENTRA A API REAL DO MERCADO PAGO: quem lê `provedor === 'mercadopago'`
// e decide a chamada é `infra/provedoresDeCobranca.js`. Nenhum componente
// desta tabela para baixo sabe que existe Mercado Pago.
// Rodada 12 (issue #13, sugestão da Thatiane): Pix e cartão em destaque, as
// formas de pagar na entrega em segundo plano (`destaque`).
export const MEIOS_DE_PAGAMENTO = [
  {
    id: 'pix', rotulo: 'Pix', situacao: 'ativo', provedor: 'mercadopago', liberaEsteiraSemPagar: false,
    destaque: true,
  },
  {
    id: 'cartao-link', rotulo: 'Cartão por link', situacao: 'ativo', provedor: 'mercadopago',
    liberaEsteiraSemPagar: false, destaque: true,
  },
  {
    id: 'maquininha', rotulo: 'Maquininha na entrega', situacao: 'ativo', provedor: 'manual',
    liberaEsteiraSemPagar: true, destaque: false,
  },
  {
    id: 'vale-refeicao', rotulo: 'Vale-refeição · na maquininha', situacao: 'ativo', provedor: 'manual',
    liberaEsteiraSemPagar: true, destaque: false,
  },
]

// Origem das viagens de entrega (seção 6, "Gerar rota"): o Google Maps entra
// com este endereço como origem.
export const ENDERECO_DA_CASA = 'Rua Girassol, 200, Vila Madalena, 05433-000'

// Marcos de cada entrega, calculados de trás para frente a partir do início
// da janela (seção 6). Constantes, todas trocáveis por aqui.
export const MINUTOS_TRECHO = 15
export const MINUTOS_CHEGADA_ENTREGADOR = 15
export const MINUTOS_CONFERIR = 20

// Restrições alimentares (seção 2, tags): decidem campanha (RN-39) e o que o
// agente não responde sozinho (RN-52). Vêm sempre primeiro entre as tags.
export const RESTRICOES = ['Lactose', 'Glúten', 'Celíaco', 'Vegano', 'Alergia']

// Toques rápidos da anotação da comanda (seção 3.3). "Sem lactose" não mora
// aqui: só aparece quando o cliente carrega a tag de restrição.
export const ANOTACOES_RAPIDAS = ['Sem pimenta', 'Molho à parte', 'Sem queijo', 'Pouco sal', 'Sem tempero verde']

// Prefixos de CEP atendidos (RN-10, RN-11): mesmo grão grosseiro que a massa
// já usa para achar bairro por texto, o bastante para o protótipo aceitar ou
// recusar um endereço.
export const PREFIXOS_CEP_ATENDIDOS = ['05433', '05435', '05408', '05416', '05022']

// ---------------------------------------------------------------------------
// Rodada 7 · frente Anexos (pedido do dono, 24/09/2026 04h12: "poder enviar
// ali na hora... até pra mensagens de horário de funcionamento e etc").
// Peça de seed sem foto de prato: texto bate com FUNCIONAMENTO_PADRAO
// (dominio/funcionamento.js, 08:00-22:00 todo dia), editável pela dona depois
// como qualquer outra peça da galeria.
// ---------------------------------------------------------------------------
export const PECA_HORARIO_PADRAO = {
  id: 'peca-horario-funcionamento',
  nome: 'Horário de funcionamento',
  descricao: 'Funcionamos todos os dias, das 8h às 22h.',
}
