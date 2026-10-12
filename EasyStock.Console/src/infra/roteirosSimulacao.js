// Frente 7 · Menu de simulações (rodada 5, seção 7). "infra/roteirosSimulacao.js:
// os textos das mensagens e as conversas modelo" (seção 7, "Como funciona por
// dentro"). Camada infra: só pode importar de infra e dominio
// (`ferramentas/verificar-camadas.mjs`), e aqui nem isso precisa.
//
// Cada função de ESQUELETOS devolve o "molde" de uma conversa nova, no MESMO
// formato final que `infra/massaConversas.js` entrega para o resto da tela
// (ultimaEm em ISO, mensagens com `em`, nunca `minutosAtras`): quem injeta
// direto no estado é a F7, sem passar pelo arquivo da massa (comentário já
// deixado lá: "rede de segurança para conversa simulada que a F7 injetar").
// `aplicacao/acoes/simulacao.js` é quem chama isto e completa com id e agora.

const clienteBase = (extra = {}) => ({
  desde: null, endereco: null, enderecoCapturado: null, telefone: null, pedidos: 0, tags: [], notas: [], ...extra,
})

// `entregador: null` pelo mesmo motivo de `dominio/pedido.js: novoPedido`
// (US-040): ninguém foi escalado ainda, texto fixo aqui vira mentira assim
// que o pedido simulado despachar.
const pedidoBase = (numero, { estado = 'aguardando', itens = [], janela = 'j3', cobranca = null } = {}) => ({
  numero, estado, janela, entregador: null, itens, agradecimentoEnviado: false,
  pagamentos: [], cobranca,
})

const cobrancaBase = (agora, { valor, minutosParaExpirar = 30, comprovanteEm = null, pagaEm = null, valorPago = null } = {}) => ({
  id: `pix-sim-${Math.round(agora)}`,
  valor,
  copiaECola: '00020126… (simulação)',
  link: 'https://pix.exemplo/simulacao',
  criadaEm: agora,
  expiraEm: agora + minutosParaExpirar * 60000,
  comprovanteEm,
  pagaEm,
  valorPago,
  liberadaEm: null,
  tentativa: 1,
  diferenca: false,
  estornadaEm: null,
})

export const ESQUELETOS = {
  // P0 (banca3/e1-e3, achado 1): no WhatsApp de verdade o número do remetente
  // chega junto com toda mensagem, antes de qualquer cadastro. `telefoneCanal`
  // representa esse dado do canal; `cliente.telefone` continua null até ela
  // cadastrar (RN-02/US-002 seguem lendo só `telefone`).
  //
  // Rodada 11 (issue #8, registro 92): o lead novo chega pelo Instagram, que
  // não entrega telefone nenhum, e com o apelido do perfil ("Rafa") no lugar
  // do nome. É o caso em que o automático precisa captar da conversa os três
  // dados do cadastro (nome, telefone e endereço), em vez de herdar do canal.
  'lead-novo': () => ({
    conta: 'lead', nome: 'Rafa', canal: 'Instagram',
    cliente: clienteBase({ tags: ['Chegou pelo Instagram'], usuario: 'rafa.tavares' }),
    pedido: null,
  }),
  recorrente: () => ({
    conta: 'cliente', nome: 'José Moretti', canal: 'WhatsApp', cadastroId: 'cad-01',
    cliente: clienteBase({
      desde: 'março de 2026', endereco: 'Rua Girassol, 412, apto 71, Vila Madalena, 05433-001',
      telefone: '(11) 90000-0137', pedidos: 7, tags: ['Gosta de lasanha'],
    }),
    pedido: null,
  }),
  restricao: () => ({
    conta: 'lead', nome: 'Beatriz Nogueira', canal: 'WhatsApp',
    cliente: clienteBase({ tags: ['Chegou pelo WhatsApp'] }),
    pedido: null,
  }),
  'pagamento-cai': (agora) => ({
    conta: 'cliente', nome: 'Fábio Andrade', canal: 'WhatsApp', cadastroId: 'sim-cad-fabio',
    cliente: clienteBase({ desde: 'julho de 2026', telefone: '(11) 90000-0222', pedidos: 3 }),
    pedido: pedidoBase('2026-0501', {
      estado: 'aguardando',
      itens: [{ sku: 'LAS-CLA', qtd: 1, obs: '', acrescimo: false, entrouEm: null }],
      cobranca: cobrancaBase(agora, { valor: 85, minutosParaExpirar: 25 }),
    }),
  }),
  'pix-vence': (agora) => ({
    conta: 'cliente', nome: 'Camila Duarte', canal: 'WhatsApp', cadastroId: 'sim-cad-camila',
    cliente: clienteBase({ desde: 'agosto de 2026', telefone: '(11) 90000-0333', pedidos: 2 }),
    pedido: pedidoBase('2026-0502', {
      estado: 'aguardando',
      itens: [{ sku: 'RAV-LIM', qtd: 1, obs: '', acrescimo: false, entrouEm: null }],
      cobranca: cobrancaBase(agora, { valor: 62, minutosParaExpirar: 0.5 }),
    }),
  }),
  // P1 (banca3/e4-e7, achado 2), reincidência da decisão 28 (Otávio) e QA-7:
  // pedido entregue sem cobrança não tem o que devolver, e "Marcar estorno"
  // (BarraProximoPasso.jsx, exige cobranca.pagaEm) nunca aparecia, contra
  // RN-36/US-050. Pix pago no valor de tabela da Lasanha clássica (R$ 85).
  reclamacao: (agora) => ({
    conta: 'cliente', nome: 'Marina Lopes', canal: 'WhatsApp', cadastroId: 'sim-cad-marina',
    cliente: clienteBase({ desde: 'junho de 2026', telefone: '(11) 90000-0444', pedidos: 4 }),
    pedido: pedidoBase('2026-0503', {
      estado: 'entregue',
      itens: [{ sku: 'LAS-CLA', qtd: 1, obs: '', acrescimo: false, entrouEm: null }],
      cobranca: cobrancaBase(agora, { valor: 85, pagaEm: agora, valorPago: 85 }),
    }),
  }),
  'fora-de-area': () => ({
    conta: 'lead', nome: 'Diego Farias', canal: 'Instagram',
    cliente: clienteBase({ tags: ['Chegou pelo Instagram'] }),
    pedido: null,
  }),
  'pagou-a-menos': (agora) => ({
    conta: 'cliente', nome: 'Helena Prado', canal: 'WhatsApp', cadastroId: 'sim-cad-helena',
    cliente: clienteBase({ desde: 'maio de 2026', telefone: '(11) 90000-0555', pedidos: 5 }),
    pedido: pedidoBase('2026-0504', {
      estado: 'aguardando',
      itens: [{ sku: 'PAP-RAG', qtd: 1, obs: '', acrescimo: false, entrouEm: null }],
      cobranca: cobrancaBase(agora, { valor: 68, minutosParaExpirar: 25 }),
    }),
  }),
  'entrega-atrasa': () => ({
    conta: 'cliente', nome: 'Tiago Nunes', canal: 'WhatsApp', cadastroId: 'sim-cad-tiago',
    cliente: clienteBase({ desde: 'abril de 2026', telefone: '(11) 90000-0666', pedidos: 6 }),
    pedido: pedidoBase('2026-0505', { estado: 'preparo', itens: [{ sku: 'TOR-COS', qtd: 1, obs: '', acrescimo: false, entrouEm: null }] }),
  }),
  'bloqueado-insiste': () => ({
    conta: 'cliente', nome: 'Cadastro Bloqueado (teste)', canal: 'Instagram', cadastroId: 'sim-cad-bloqueado',
    cliente: clienteBase({ tags: ['Bloqueado'] }),
    pedido: null,
    bloqueio: { motivo: 'Cobrou preço de concorrente e xingou no chat, 12/06/2026.', em: '12/06/2026', por: 'Thatiane', alcance: 'todos os canais' },
  }),
  'saldo-zero': () => ({
    conta: 'lead', nome: 'Priscila Gomes', canal: 'WhatsApp',
    cliente: clienteBase({ tags: ['Chegou pelo WhatsApp'] }),
    pedido: null,
  }),
  'janela-lotada': () => ({
    conta: 'lead', nome: 'André Costa', canal: 'WhatsApp',
    cliente: clienteBase({ tags: ['Chegou pelo WhatsApp'] }),
    pedido: null,
  }),
  // Rodada 10 (registro 79, achado P2.7): `avaliacaoPendente` liga a reação
  // do cliente simulado (aplicacao/useReacaoClienteSimulado.js), que manda o
  // toque de avaliação (US-046) em vez do roteiro de texto livre que este
  // cenário reaproveitava da Reclamação.
  'avaliacao-negativa': () => ({
    conta: 'cliente', nome: 'Larissa Melo', canal: 'WhatsApp', cadastroId: 'sim-cad-larissa',
    cliente: clienteBase({ desde: 'fevereiro de 2026', telefone: '(11) 90000-0777', pedidos: 8 }),
    pedido: pedidoBase('2026-0506', { estado: 'entregue', itens: [{ sku: 'RAV-ABO', qtd: 1, obs: '', acrescimo: false, entrouEm: null }] }),
    avaliacaoPendente: 'negativa',
  }),
  'desliga-avisos': () => ({
    conta: 'cliente', nome: 'Otávio Reis', canal: 'WhatsApp', cadastroId: 'sim-cad-otavio',
    cliente: clienteBase({ desde: 'janeiro de 2026', telefone: '(11) 90000-0888', pedidos: 9 }),
    pedido: pedidoBase('2026-0507', { estado: 'preparo', itens: [{ sku: 'PAP-RAG', qtd: 1, obs: '', acrescimo: false, entrouEm: null }] }),
  }),
  'pedido-outro-dia': () => ({
    conta: 'lead', nome: 'Vinícius Alves', canal: 'WhatsApp',
    cliente: clienteBase({ tags: ['Chegou pelo WhatsApp'] }),
    pedido: null,
  }),
  // Rodada 7 · frente Anexos: canal WhatsApp de propósito (aceitaMidia.audio
  // true), para o áudio recebido não esbarrar na restrição do canal.
  'cliente-audio': () => ({
    conta: 'lead', nome: 'Patrícia Souza', canal: 'WhatsApp',
    cliente: clienteBase({ tags: ['Chegou pelo WhatsApp'] }),
    pedido: null,
  }),
}

// Variações do cenário de volume (10 clientes de uma vez): mesmos moldes,
// nomes diferentes para não colidir na lista, sem cobrança nem pedido (o
// ponto do cenário é o VOLUME de conversas chegando, não o pedido de cada
// uma).
const leadVolume = (nome) => ({ conta: 'lead', nome, canal: 'WhatsApp', cliente: clienteBase() })
const recorrenteVolume = (nome, cadastroId) => ({
  conta: 'cliente', nome, canal: 'WhatsApp', cadastroId,
  cliente: clienteBase({ desde: 'março de 2026', pedidos: 3, tags: ['Gosta de ravióli'] }),
})

export const VARIACOES_VOLUME = {
  'lead-0': () => leadVolume('Gustavo Ramos'),
  'lead-1': () => leadVolume('Fernanda Dias'),
  'lead-2': () => leadVolume('Bruno Cardoso'),
  'lead-3': () => leadVolume('Juliana Rocha'),
  'recorrente-0': () => recorrenteVolume('Sandra Vieira', 'sim-cad-sandra'),
  'recorrente-1': () => recorrenteVolume('Paulo Mendes', 'sim-cad-paulo'),
  'recorrente-2': () => recorrenteVolume('Renata Souza', 'sim-cad-renata'),
  'restricao-0': () => leadVolume('Igor Barbosa'),
  'restricao-1': () => leadVolume('Cláudia Farias'),
  'reclamacao-0': () => ({
    conta: 'cliente', nome: 'Eduardo Lima', canal: 'WhatsApp', cadastroId: 'sim-cad-eduardo',
    cliente: clienteBase({ desde: 'maio de 2026', pedidos: 4 }),
    pedido: pedidoBase('2026-0508', { estado: 'entregue', itens: [{ sku: 'LAS-CLA', qtd: 1, obs: '', acrescimo: false, entrouEm: null }] }),
  }),
}

// Falas do cliente por cenário, na ordem em que o roteiro dispara. `foto`
// marca a mensagem como mídia (RN-35: reclamação com foto do prato) e
// `comprovante` marca a mensagem como comprovante de Pix (US-032): é essa
// marca, e não um regex sobre o texto, que diz para o evento conciliar.
// `endereco` é o endereço que a mensagem traz, e vai para a ficha.
export const FALAS = {
  // Rodada 11 (issue #8, registro 92): o cliente responde ao que o automático
  // pergunta, em frase de gente, e NENHUMA fala carrega marca de dado (nada
  // de `endereco:` aqui): quem acha nome, telefone e endereço no texto é
  // `dominio/captura.js`. `reservaPedido` só guarda o número do pedido que o
  // automático anota nesta fala (aplicacao/acoes/simulacao.js). A pergunta
  // fora do roteiro da rodada 10 (P2.6) saiu daqui: o fallback continua
  // valendo para qualquer pergunta que o automático não responda.
  'lead-novo': [
    { texto: 'Oi! Vi o cardápio no Instagram, vocês entregam hoje ainda?' },
    { texto: 'Me chamo Rafael Tavares, prazer!' },
    { texto: 'Claro, é (11) 98765-4321' },
    { texto: 'Rua Girassol, 300, apto 12, Vila Madalena, 05433-002' },
    { texto: 'Quero uma lasanha clássica, adoro lasanha!', reservaPedido: true },
  ],
  recorrente: [{ texto: 'Oi Thatiane! Pode mandar o de sempre pra mim?' }],
  restricao: [{ texto: 'Tem glúten no ravióli? Minha filha é celíaca.' }],
  'fora-de-area': [
    {
      texto: 'Oi! Queria pedir para entregar na Rua Voluntários da Pátria, 1200, Freguesia do Ó, 02420-000',
      endereco: 'Rua Voluntários da Pátria, 1200, Freguesia do Ó, CEP 02420-000',
    },
    { texto: 'Quero sim, mesmo assim. Consegue ver com a Thatiane? Eu pago a taxa extra da corrida.' },
  ],
  'entrega-atrasa': [{ texto: 'Oi, cadê meu pedido? A janela já começou.' }],
  'bloqueado-insiste': [{ texto: 'Oi, sou eu de novo, dá para me atender por aqui?' }],
  'saldo-zero': [{ texto: 'Oi! Pode ser uma lasanha verde para hoje?' }],
  'janela-lotada': [{ texto: 'Quero fechar o pedido para a janela das 11h30, dá?' }],
  'desliga-avisos': [{ texto: 'Pode parar de mandar aviso a cada passo do pedido?' }],
  'pedido-outro-dia': [{ texto: 'Oi, sei que é tarde, mas dá pra deixar anotado um pedido pra sexta?' }],
  reclamacao: [{ texto: 'A lasanha chegou virada e fria, não deu para comer.', foto: true }],
  // `audio: true` funciona como `foto: true` acima: marca de formato, não
  // regex sobre o texto. `texto` some da bolha (a bolha de áudio não mostra
  // transcrição), mas continua útil pro log e pro `alt`/aria-label do player.
  'cliente-audio': [{ texto: 'Pergunta se a entrega inclui a região dela.', audio: true, duracaoMs: 5000 }],
}

export const FALA_VOLUME = { texto: 'Oi! Queria fazer um pedido, pode ser?' }
export const FALA_VOLUME_RECLAMACAO = { texto: 'Chegou tudo errado dessa vez, muito ruim.', foto: true }

