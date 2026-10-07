// Mensagens automáticas. Regra é DADO, não código: acrescentar uma regra é
// acrescentar uma entrada, e nenhum componente precisa mudar.
//
// Cada regra declara quando dispara, o que manda e se está ligada. O disparo
// é sempre visível na conversa, marcado como automático, porque a dona
// precisa saber o que a casa falou em nome dela.
import { faixaParaCliente, preencherFaixa } from './entrega'
import { rotaDoCardapioLink } from './cardapioLink'

export const GATILHOS = {
  PRIMEIRO_CONTATO: 'primeiro-contato',
  FORA_DO_HORARIO: 'fora-do-horario',
  LOJA_FECHADA: 'loja-fechada',
  PEDIDO_GERADO: 'pedido-gerado',
  PAGAMENTO_CONFIRMADO: 'pagamento-confirmado',
  SEM_RESPOSTA: 'sem-resposta',
  POS_ENTREGA: 'pos-entrega',
  // Rodada 5, "Encerrar" v2 (decisão 46, pedido do dono 24/09): mensagem de
  // despedida oferecida na confirmação de encerrar. Nome fixo: a frente que
  // lê a biblioteca de respostas depois desta busca por este gatilho.
  ENCERRAMENTO: 'encerramento',
}

export const REGRAS_PADRAO = [
  {
    id: 'boas-vindas',
    nome: 'Primeira resposta',
    gatilho: GATILHOS.PRIMEIRO_CONTATO,
    descricao: 'Primeira mensagem de quem nunca falou com a casa, com a loja aberta.',
    ativa: true,
    // US-001 (regra de negócio) e UC-01 passo 2: o cardápio vai como link de
    // verdade, o mesmo que o botão "Enviar cardápio" do composer monta
    // (dominio/cardapioLink.js, linkDoCardapio). Antes só prometia em texto,
    // sem anexar nada (achado P0.1, banca10/simulacao).
    texto: 'Olá! Aqui é a Casa da Baba, massa artesanal com entrega agendada. '
      + 'Segue nosso cardápio de hoje, é só escolher: {linkCardapio} '
      + 'Me chama quando escolher que eu já anoto.',
  },
  // Fora do horário e Loja fechada (pedido do dono, 24/09/2026): dois
  // gatilhos próprios de novo (o corte #16 da rodada 5 tinha fundido "Fora
  // do horário" em "Primeira resposta", ver auditoria/decisoes/23 e 24).
  // `dominio/simulacoes.js` decide qual das duas dispara, combinando
  // `dominio/funcionamento.js` (horário configurado) com o estado manual da
  // loja; as duas seguram o cliente sem vender e sem prometer entrega.
  {
    id: 'fora-do-horario',
    nome: 'Fora do horário',
    gatilho: GATILHOS.FORA_DO_HORARIO,
    descricao: 'Cliente escreve com a loja aberta no controle, mas fora do horário configurado.',
    ativa: true,
    texto: 'Oi! Hoje já fechamos por aqui, voltamos {abre}. '
      + 'Se quiser já deixar o pedido anotado, me diz o que você quer que eu separo para o próximo horário.',
  },
  {
    id: 'loja-fechada',
    nome: 'Loja fechada',
    gatilho: GATILHOS.LOJA_FECHADA,
    descricao: 'A dona fechou a loja na mão, no controle do topo, mesmo dentro do horário.',
    ativa: true,
    texto: 'Oi! Fechamos a loja por aqui agora. '
      + 'Pode deixar o pedido anotado que eu separo para o próximo horário, sem prometer prazo ainda.',
  },
  // A regra "Link de pagamento" saiu daqui. Texto de regra é texto fixo, e o
  // Pix só existe depois da emissão: a regra prometia "segue o link" sem link
  // nenhum. Gerar o pedido emite a cobrança e manda o texto de cobranca.js, com
  // valor, prazo, link e copia e cola de verdade.
  {
    id: 'recibo',
    nome: 'Confirmação de pagamento',
    gatilho: GATILHOS.PAGAMENTO_CONFIRMADO,
    descricao: 'Confirma o recebimento e avisa a janela de entrega.',
    ativa: true,
    texto: 'Pagamento confirmado, obrigada! Entrego entre {faixa}.',
  },
  {
    id: 'agradecimento',
    nome: 'Agradecimento e avaliação',
    gatilho: GATILHOS.POS_ENTREGA,
    descricao: 'Depois que o pedido é marcado como entregue.',
    ativa: true,
    texto: 'Obrigada pela preferência! Se puder, me conta o que achou. Boa massa.',
  },
  {
    id: 'encerramento',
    nome: 'Encerramento do atendimento',
    gatilho: GATILHOS.ENCERRAMENTO,
    descricao: 'Oferecida na confirmação de Encerrar, só sai quando a dona marca "mandar mensagem".',
    ativa: true,
    texto: 'Por hoje é só, {nome}! Qualquer coisa é só chamar de novo por aqui. Até a próxima!',
  },
]

export const regraDoGatilho = (regras, gatilho) =>
  regras.find((r) => r.gatilho === gatilho && r.ativa) ?? null

export function textoDaRegra(regra, contexto = {}) {
  if (!regra) return null
  const texto = contexto.faixa != null ? preencherFaixa(regra.texto, contexto.faixa) : regra.texto
  return texto.replace(/\{(\w+)\}/g, (todo, chave) => contexto[chave] ?? todo)
}

export const contarAtivas = (regras) => regras.filter((r) => r.ativa).length

// As cinco variáveis que o texto aceita. Quem escreve precisa ver a lista.
export const VARIAVEIS = [
  { chave: 'nome', descricao: 'primeiro nome de quem está na conversa' },
  { chave: 'faixa', descricao: 'janela de entrega do pedido' },
  { chave: 'pedido', descricao: 'número do pedido em andamento' },
  { chave: 'abre', descricao: 'quando a loja abre de novo (só na regra Fora do horário)' },
  { chave: 'linkCardapio', descricao: 'link do cardápio (só na regra Primeira resposta)' },
]

// Contexto da prévia: no que {nome}, {faixa} e {pedido} viram na conversa
// escolhida. Conversa sem pedido ou sem janela cai em padrão seguro.
//
// `agora`, quando chega, aplica o respiro (RN-06, RN-22, US-028) antes de
// {faixa} virar texto: ponto único, `faixaParaCliente` em dominio/entrega.js.
export function contextoDePrevia(conversa, faixa, agora) {
  return {
    nome: conversa?.nome?.split(' ')[0] ?? 'cliente',
    faixa: (agora != null && faixa ? faixaParaCliente(faixa, agora) : faixa) ?? 'o horário combinado',
    pedido: conversa?.pedido?.numero ?? 'seu pedido',
    // Prévia não tem `origem` de verdade (window é infra, este arquivo é
    // domínio puro): mostra a rota, que já é o bastante para ela conferir o
    // texto antes de salvar. O link que sai de verdade usa `linkDoCardapio`
    // com a origem real (dominio/simulacoes.js, respostaParaMensagemDeCliente).
    linkCardapio: rotaDoCardapioLink(conversa?.id ?? 'preview'),
  }
}

// ---------------------------------------------------------------------------
// Modo API (#1441): as seis automáticas do EasyStok (S42, `GatilhoAutomacao`)
// casadas com as regras desta tela pelo gatilho. A descrição diz quando o
// EasyStok dispara de verdade (AutomacoesAtendimentoHandlers), não o roteiro
// da demonstração. Na primeira mensagem sai uma só: loja fechada na mão vence
// fora do horário, que vence a primeira resposta.
// ---------------------------------------------------------------------------
export const GATILHOS_DA_API = {
  PrimeiroContato: {
    id: 'boas-vindas', gatilho: GATILHOS.PRIMEIRO_CONTATO, nome: 'Primeira resposta',
    descricao: 'Primeira mensagem de uma conversa nova, com a loja aberta no horário.',
  },
  ForaDoHorario: {
    id: 'fora-do-horario', gatilho: GATILHOS.FORA_DO_HORARIO, nome: 'Fora do horário',
    descricao: 'Primeira mensagem de uma conversa nova, fora do horário do expediente.',
  },
  LojaFechada: {
    id: 'loja-fechada', gatilho: GATILHOS.LOJA_FECHADA, nome: 'Loja fechada',
    descricao: 'Primeira mensagem de uma conversa nova, com a loja fechada na mão.',
  },
  PagamentoConfirmado: {
    id: 'recibo', gatilho: GATILHOS.PAGAMENTO_CONFIRMADO, nome: 'Pagamento confirmado',
    descricao: 'Quando o pagamento do pedido é confirmado.',
  },
  PosEntrega: {
    id: 'agradecimento', gatilho: GATILHOS.POS_ENTREGA, nome: 'Depois da entrega',
    descricao: 'Quando o pedido é marcado como entregue.',
  },
  Encerramento: {
    id: 'encerramento', gatilho: GATILHOS.ENCERRAMENTO, nome: 'Encerramento',
    descricao: 'Quando o atendimento é encerrado pelo console.',
  },
}

// O EasyStok só preenche estas três (ModeloTextoAtendimento); qualquer outra
// chave entre chaves sai para o cliente do jeito que está escrita.
export const VARIAVEIS_DA_API = [
  { chave: 'nome', descricao: 'primeiro nome do cliente' },
  { chave: 'pedido', descricao: 'código do pedido em andamento' },
  { chave: 'faixa', descricao: 'dia e hora agendados da entrega' },
]

export function variaveisForaDaApi(texto) {
  const conhecidas = new Set(VARIAVEIS_DA_API.map((v) => v.chave))
  const chaves = [...String(texto ?? '').matchAll(/\{(\w+)\}/g)].map((m) => m[1])
  return [...new Set(chaves.filter((c) => !conhecidas.has(c)))]
}

// O que a casa mandou sozinha nesta conversa, do mais novo ao mais antigo:
// agente (IA) e mensagens do sistema (automáticas, avisos de pedido).
export const enviosAutomaticos = (mensagens) => (mensagens ?? [])
  .filter((m) => m.dir === 'out' && m.automatica)
  .sort((a, b) => new Date(b.em) - new Date(a.em))
