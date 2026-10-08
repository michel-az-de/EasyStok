// Leitura da conversa para o agente. Puro, sem rede: decide QUE PERGUNTA fazer
// ao modelo e como apresentar a resposta. A chamada em si mora em infra.
//
// Regra de produto: o agente PROPÕE, a dona decide. Nada sai sem ela mandar,
// salvo as regras automáticas que ela mesma ligou.
//
// Duas travas vieram da entrevista e valem para qualquer transporte:
// 1. Restrição alimentar nunca é respondida pelo automático. Vai para a dona.
// 2. Item sem saldo: à mão a casa avisa e vende; o automático não vende e
//    passa para a dona (divisão da regra "avisa, não trava", 22/09/2026).

// Com extensão: este arquivo também é carregado direto pelo Node em
// ferramentas/avaliar-agente.mjs, e o ESM do Node não resolve caminho sem .js.
import {
  faixaDepoisDeEntre, faixaParaCliente, janelaDisponivel, janelaPorId,
} from './entrega.js'
import {
  foraDaArea, precisaEscalarPorArea, respostaDeAreaFora, situacaoDoCep,
} from './areaEntrega.js'
import { moeda } from './formato.js'
import { estaRemovido, oferecivelPeloAutomatico } from './cardapio.js'

export const INTENCOES = {
  PEDIR_CARDAPIO: { chave: 'pedir-cardapio', rotulo: 'Quer ver o cardápio' },
  FECHAR_PEDIDO: { chave: 'fechar-pedido', rotulo: 'Quer fechar o pedido' },
  PERGUNTA_ENTREGA: { chave: 'pergunta-entrega', rotulo: 'Pergunta sobre entrega' },
  RESTRICAO: { chave: 'restricao', rotulo: 'Restrição alimentar' },
  ACOMPANHAR: { chave: 'acompanhar', rotulo: 'Quer saber do pedido' },
  ELOGIO_OU_QUEIXA: { chave: 'elogio-ou-queixa', rotulo: 'Retorno sobre o pedido' },
  OUTRO: { chave: 'outro', rotulo: 'Assunto fora do roteiro' },
}

// O que o agente recomenda fazer com a resposta.
export const ACOES = {
  PROPOR: 'propor',
  PASSAR_PARA_DONA: 'passar_para_dona',
}

const PISTAS = [
  // Radical, não palavra fechada: "vegana" não é "vegano", e a cliente escreve
  // como fala. Restrição que escapa por causa de uma letra é restrição que o
  // automático responde sozinho, que é justamente o que não pode acontecer.
  [INTENCOES.RESTRICAO, ['glúten', 'gluten', 'lactose', 'laticínio', 'leite', 'vegan', 'vegetarian',
    'alergia', 'alérgic', 'alergic', 'celíac', 'celiac', 'ingrediente', 'composição', 'composicao', 'intoler']],
  [INTENCOES.PERGUNTA_ENTREGA, ['entrega', 'entregam', 'chega', 'região', 'regiao', 'cep', 'bairro', 'frete']],
  [INTENCOES.ACOMPANHAR, ['quanto tempo', 'já saiu', 'ja saiu', 'previsão', 'previsao', 'cadê', 'cade', 'demora']],
  [INTENCOES.FECHAR_PEDIDO, ['quero', 'vou querer', 'me manda', 'fechar', 'pode ser', 'pedido']],
  [INTENCOES.PEDIR_CARDAPIO, ['o que tem', 'cardápio', 'cardapio', 'menu', 'opções', 'opcoes']],
  [INTENCOES.ELOGIO_OU_QUEIXA, ['adorei', 'gostei', 'delícia', 'delicia', 'obrigad', 'ruim', 'fria', 'estragad', 'errad']],
]

const normalizar = (texto) => texto.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')

const PALAVRAS_VAZIAS = new Set(['para', 'extra', 'ralado'])
const palavrasChave = (nome) => normalizar(nome).split(/[^a-z]+/)
  .filter((p) => p.length >= 4 && !PALAVRAS_VAZIAS.has(p))

// Decisão do dono, 22/09/2026: o automático NUNCA responde composição ou
// restrição. Lista de palavra por ingrediente não fecha (sempre falta uma),
// então a lógica inverte: pergunta no formato "tem/leva/contém X?" ou "sem X"
// é bloqueada por padrão, mesmo sem nenhuma palavra de restrição conhecida.
// "tem X?" fica de fora de propósito: pergunta de disponibilidade ("tem
// pappardelle pra hoje?") usa o mesmo verbo e não é composição. "Leva",
// "contém" e "possui" já perguntam o que está dentro do prato.
const PADRAO_COMPOSICAO = /\b(leva|contem|contém|possui|é feito|e feito|vai com)\b[^?]{0,60}\?/
// "sem X" sozinho pega ingrediente de verdade ("sem cebola", "sem gluten"),
// mas também qualquer bordão do dia a dia que usa "sem" sem falar de prato
// ("sem pressa", "sem crise", "sem mais essa", "deixa sem troco"). Ingrediente
// não fecha em lista (mesmo motivo do parágrafo acima), mas conectivo e
// substantivo abstrato do "sem" descompromissado fecham: essa lista é curta e
// aberta a novo item, não uma tentativa de cobrir todo ingrediente do mundo.
const PALAVRAS_SEM_SEM_RESTRICAO = new Set([
  'pressa', 'problema', 'problemas', 'duvida', 'falta', 'crise', 'chance',
  'graca', 'vergonha', 'nocao', 'essa', 'esse', 'nenhuma', 'nenhum', 'mais', 'troco',
])
const PADRAO_SEM_ALGO = /\bsem\s+(\w+)/
const pareceComposicao = (texto) => {
  if (PADRAO_COMPOSICAO.test(texto)) return true
  const semAlgo = texto.match(PADRAO_SEM_ALGO)
  return Boolean(semAlgo) && !PALAVRAS_SEM_SEM_RESTRICAO.has(semAlgo[1])
}

// RN-38: reclamação de pedido entregue nunca é restrição alimentar, mesmo
// quando a frase de fechamento do cliente cai no padrão de composição ("sem
// drama", "sem mais essa" batem o mesmo "sem X" de "sem cebola"). Checada
// antes de `pareceComposicao`, de propósito: "chegou virada", "estava
// estragada" e "pode devolver o valor" (fala da entrevista, áudio 07) valem
// mais que um "sem" solto no fim da frase.
const PISTAS_RECLAMACAO_DE_PRODUTO = [
  'estragad', 'virad', 'azed', 'passad', 'veio errad', 'pedido errad',
  'devolu', 'devolv', 'reembols', 'estorn',
]
const pareceReclamacaoDeProduto = (texto) => PISTAS_RECLAMACAO_DE_PRODUTO.some((p) => texto.includes(p))

const ultimaDoCliente = (conversa) => [...conversa.mensagens].reverse().find((m) => m.dir === 'in')

// Entre as últimas falas do cliente, acha a que de fato carrega a pista de
// reclamação. Nem sempre é a mais recente: "chegou virada" pode vir duas
// mensagens antes de um "sem drama" de fechamento. A ocorrência (dominio/
// ocorrencia.js) precisa do relato de verdade no histórico, não da última
// frase de qualquer jeito.
const mensagemComPista = (conversa, teste, quantas = 3) => [...conversa.mensagens]
  .filter((m) => m.dir === 'in').slice(-quantas).find((m) => teste(normalizar(m.texto))) ?? null

// As últimas falas do cliente, juntas: o item pedido pode estar duas mensagens atrás.
const falaRecenteDoCliente = (conversa, quantas = 3) => conversa.mensagens
  .filter((m) => m.dir === 'in').slice(-quantas).map((m) => normalizar(m.texto)).join(' ')

// Itens do cardápio citados nas últimas falas do cliente e que estão sem
// saldo. Exigir todas as palavras do nome juntas deixa passar apelido ou nome
// parcial ("a de carne", "aquela lasanha"). Uma palavra-chave de 5+ letras já
// basta, mas só quando ela é exclusiva daquele item no cardápio: "lasanha"
// sozinha não decide entre a clássica (com saldo) e a verde (sem saldo).
export function itensSemSaldoCitados(conversa, catalogo) {
  const texto = falaRecenteDoCliente(conversa)
  if (!texto) return []
  const usoDaPalavra = new Map()
  catalogo.cardapio.forEach((item) => palavrasChave(item.nome)
    .forEach((p) => usoDaPalavra.set(p, (usoDaPalavra.get(p) ?? 0) + 1)))
  return catalogo.cardapio.filter((item) => {
    if (item.estoque !== 0) return false
    const palavras = palavrasChave(item.nome)
    const citadoPorInteiro = palavras.every((p) => texto.includes(p))
    const citadoPorApelido = palavras
      .some((p) => p.length >= 5 && usoDaPalavra.get(p) === 1 && texto.includes(p))
    return citadoPorInteiro || citadoPorApelido
  })
}

// Situações que o automático nunca resolve sozinho, além de restrição e saldo:
// comida com problema, devolução, mexer em pedido fechado, desligar avisos.
const PISTAS_PARA_DONA = [
  'estragad', 'virad', 'azed', 'passad', 'devolu', 'reembols', 'estorn', 'cozida demais', 'crua',
  'veio errad', 'pedido errad', 'desligar', 'parar de mandar', 'nao quero mais receber', 'nao me manda mais',
  'ja paguei', 'encaixa', 'encaixe', 'fora da janela', 'fora do horario',
]
const pedeADona = (conversa) => {
  const texto = falaRecenteDoCliente(conversa, 2)
  return PISTAS_PARA_DONA.some((p) => texto.includes(p))
}

// Vagas da janela pela ocupação MEDIDA, que chega de fora (dominio/entrega.js
// conta dos pedidos reais). O `ocupadas` do catálogo é número fixo que não
// conversa com as conversas, e era ele que fazia o rascunho oferecer janela que
// o painel mostrava cheia.
//
// Janela que a ficha não deixa escolher vale zero aqui, seja por lotação, seja
// por ter passado do corte: o agente e o seletor de janela precisam dar a mesma
// resposta. Sem medida nenhuma a casa não promete vaga.
const vagasDaJanela = (ocupacoes, janela) => {
  const ocupacao = ocupacoes.find((o) => o.id === janela.id)
  if (!ocupacao || !janelaDisponivel(ocupacao)) return 0
  return ocupacao.vagas
}

// Monta o prompt que vai para o modelo. Fica visível na tela de propósito,
// para a dona enxergar o que a casa manda para fora.
export function montarPrompt(conversa, catalogo, ocupacoes = []) {
  const ultimas = conversa.mensagens.slice(-6)
    .map((m) => (m.dir === 'in' ? 'Cliente: ' : 'Casa: ') + m.texto)
    .join('\n')
  const disponiveis = catalogo.cardapio
    .filter(oferecivelPeloAutomatico)
    .map((i) => `${i.nome} (${i.porcao}, ${moeda(i.preco)})`)
    .join('; ')
  const esgotados = catalogo.cardapio.filter((i) => i.estoque === 0 && !estaRemovido(i) && !i.emValidacao).map((i) => i.nome).join('; ') || 'nenhum'
  // Rodada 13 (issue #42, UC-03 E1): "oferece só janela com vaga" começa por
  // nem CITAR a pausada ou fora do dia. `ocupacoes` já chega filtrada por
  // `ocupacaoDeHoje`, então a janela ausente dali nunca teve pedido nenhum
  // hoje: sem membro em `ocupacoes`, não entra no texto para o modelo.
  const janelas = catalogo.janelas
    .filter((j) => ocupacoes.some((o) => o.id === j.id))
    .map((j) => `${j.faixa} (${vagasDaJanela(ocupacoes, j)} vagas)`)
    .join('; ')
  const tags = conversa.cliente.tags.length ? ' · ' + conversa.cliente.tags.join(', ') : ''
  const pedido = conversa.pedido
    ? `Pedido em andamento: ${conversa.pedido.numero}, situação ${conversa.pedido.estado}`
    : 'Sem pedido em andamento'
  return [
    'Você atende pela Casa da Baba, massa artesanal com entrega agendada em São Paulo.',
    'Tom caloroso e direto, de gente da casa. Nunca invente item, preço, janela ou prazo fora do que está aqui.',
    'Entrega só nas janelas com vaga. Nunca prometa antes da janela.',
    '',
    'Cardápio disponível: ' + disponiveis,
    'Esgotado hoje: ' + esgotados,
    'Janelas de entrega de hoje: ' + janelas,
    'Cliente: ' + conversa.nome + tags,
    pedido,
    '',
    'Conversa:',
    ultimas,
    '',
    'Decida a acao por esta lista, na ordem. Se qualquer item valer, acao é "passar_para_dona":',
    '1. O cliente falou de restrição alimentar, alergia, intolerância ou perguntou ingrediente: não afirme nada sobre composição, diga que a Thatiane confirma.',
    '2. O cliente pediu item da lista "Esgotado hoje": não venda, não ofereça troca, diga que vai conferir no congelador.',
    '3. Comida com problema, pedido errado, devolução ou reembolso: acolha e não prometa nada.',
    '4. Mudar pedido já pago, encaixe fora das janelas com vaga, ou pedido para desligar avisos.',
    'Nada disso valeu: acao é "propor", e você responde direto, com janela e preço reais quando couber.',
    '',
    'Responda SOMENTE com um JSON de uma linha, sem texto em volta:',
    '{"acao": "propor" ou "passar_para_dona", "texto": "sua resposta ao cliente em uma ou duas frases"}',
  ].join('\n')
}

// A intenção sai das MESMAS três últimas falas que o resto do arquivo lê. Com
// uma fala só, "Claro, sem pressa" apagava a restrição alimentar dita na linha
// anterior e o automático respondia sozinho o que nunca pode responder.
export function classificarIntencao(conversa) {
  const ultima = ultimaDoCliente(conversa)
  if (!ultima) return { intencao: INTENCOES.OUTRO, trecho: null, reclamacaoDeProduto: false }
  const texto = falaRecenteDoCliente(conversa)
  // Reclamação de produto entregue vem antes de tudo, inclusive de
  // composição (RN-38): ver PISTAS_RECLAMACAO_DE_PRODUTO acima.
  if (pareceReclamacaoDeProduto(texto)) {
    const origem = mensagemComPista(conversa, pareceReclamacaoDeProduto) ?? ultima
    return { intencao: INTENCOES.ELOGIO_OU_QUEIXA, trecho: origem.texto, reclamacaoDeProduto: true }
  }
  // Composição vem antes das outras pistas, de propósito: "me manda sem
  // cebola" também bate a pista genérica de "fechar pedido", mas restrição
  // não pode perder para um casamento de palavra menos específico. Só a
  // pista de RESTRICAO propriamente dita ou uma frase marcada como segura
  // furam esta checagem primeiro.
  if (pareceComposicao(texto)) return { intencao: INTENCOES.RESTRICAO, trecho: ultima.texto, reclamacaoDeProduto: false }
  const achada = PISTAS.find(([, pistas]) => pistas.some((p) => texto.includes(normalizar(p))))
  return { intencao: achada ? achada[0] : INTENCOES.OUTRO, trecho: ultima.texto, reclamacaoDeProduto: false }
}

// A ação que o modo simulado recomenda. Segue as mesmas travas do prompt.
// RN-38: reclamação de produto entregue nunca é respondida sozinha, mesmo
// quando a fala de fechamento do cliente escapa da lista de `pedeADona`
// (ex.: "devolver" não é "devolu"). `reclamacaoDeProduto` já veio decidido
// de `classificarIntencao`, uma pista só, não duas listas divergindo.
export function acaoSugerida({ intencao, reclamacaoDeProduto }, conversa, catalogo) {
  if (intencao.chave === INTENCOES.RESTRICAO.chave) return ACOES.PASSAR_PARA_DONA
  if (reclamacaoDeProduto) return ACOES.PASSAR_PARA_DONA
  if (itensSemSaldoCitados(conversa, catalogo).length > 0) return ACOES.PASSAR_PARA_DONA
  if (pedeADona(conversa)) return ACOES.PASSAR_PARA_DONA
  // RN-11: só escala quando o lead já foi avisado que está fora e insistiu.
  if (precisaEscalarPorArea(conversa, catalogo.prefixosCepAtendidos)) return ACOES.PASSAR_PARA_DONA
  return ACOES.PROPOR
}

// Lê a resposta crua do modelo. Aceita JSON limpo, JSON dentro de cerca de
// código e, no pior caso, texto solto, que vira proposta sem estrutura.
export function interpretarResposta(bruto) {
  const limpo = String(bruto ?? '').replace(/```(?:json)?/gi, '').trim()
  const inicio = limpo.indexOf('{')
  const fim = limpo.lastIndexOf('}')
  if (inicio >= 0 && fim > inicio) {
    try {
      const objeto = JSON.parse(limpo.slice(inicio, fim + 1))
      const texto = typeof objeto.texto === 'string' ? objeto.texto.trim() : ''
      if (texto) {
        const acao = objeto.acao === ACOES.PASSAR_PARA_DONA ? ACOES.PASSAR_PARA_DONA : ACOES.PROPOR
        return { texto, acao, estruturada: true }
      }
    } catch {
      // não era JSON: cai no texto cru
    }
  }
  return { texto: limpo, acao: ACOES.PROPOR, estruturada: false }
}

// Rascunho determinístico. Faz o papel da resposta do modelo no modo simulado
// e tem a mesma forma do que a API devolve.
export function rascunhoSugerido({ intencao }, conversa, catalogo, ocupacoes = [], agora = Date.now()) {
  const nome = conversa.nome.split(' ')[0]
  const disponivel = catalogo.cardapio.find(oferecivelPeloAutomatico)
  const janelaLivre = catalogo.janelas.find((j) => vagasDaJanela(ocupacoes, j) > 0)
  const semSaldo = itensSemSaldoCitados(conversa, catalogo)

  // RN-10 vem antes de tudo: nunca promete entrega antes de saber o CEP, e o
  // aviso de fora da área não pode perder para outra pista da mensagem.
  if (precisaEscalarPorArea(conversa, catalogo.prefixosCepAtendidos)) {
    return `Peraí, ${nome}. Vou ver com a Thatiane se dá uma exceção para a sua região.`
  }
  if (foraDaArea(situacaoDoCep(ultimaDoCliente(conversa)?.texto, catalogo.prefixosCepAtendidos))) {
    return respostaDeAreaFora(nome)
  }

  if (semSaldo.length > 0) {
    return `Oi ${nome}! ${semSaldo[0].nome} eu preciso conferir no congelador antes de fechar. Já te respondo.`
  }

  switch (intencao.chave) {
    case INTENCOES.PEDIR_CARDAPIO.chave:
      return `Oi ${nome}! Hoje tem ${disponivel?.nome ?? 'massa fresca'} e mais opções no cardápio. `
        + 'Te mando o link agora, é só escolher e jogar no carrinho.'
    case INTENCOES.FECHAR_PEDIDO.chave:
      return `Fechado, ${nome}! Confirma o endereço e eu já gero o pedido com o link de pagamento.`
    case INTENCOES.PERGUNTA_ENTREGA.chave:
      return `Oi ${nome}! Me passa rua, número e CEP que eu confirmo a área na hora. `
        + `Hoje ainda tenho a janela das ${janelaLivre?.faixa ?? 'próximas horas'}.`
    case INTENCOES.RESTRICAO.chave:
      return `Oi ${nome}, obrigada por avisar. Isso eu confiro na cozinha antes de responder, para não errar.`
    case INTENCOES.ACOMPANHAR.chave: {
      // RN-06 e RN-22 (US-028): "quanto tempo falta" nunca promete menos que
      // o respiro. Sem janela escolhida ainda não há prazo para dar.
      const janelaDoPedido = conversa.pedido?.janela
        ? janelaPorId(catalogo.janelas, conversa.pedido.janela)
        : null
      if (!janelaDoPedido) {
        return `Oi ${nome}! Seu pedido está na fila, te aviso aqui assim que entrar no preparo.`
      }
      return `Oi ${nome}! Seu pedido está em preparo, chega entre ${faixaDepoisDeEntre(faixaParaCliente(janelaDoPedido.faixa, agora))}.`
    }
    case INTENCOES.ELOGIO_OU_QUEIXA.chave:
      return `Que bom ter seu retorno, ${nome}. Me conta o que dá para melhorar que eu anoto na sua ficha.`
    default:
      return `Oi ${nome}! Me conta um pouco mais que eu te ajudo já.`
  }
}

export const confiancaDe = ({ intencao }) =>
  (intencao.chave === INTENCOES.OUTRO.chave ? 0.42 : 0.88)

// #1445 (homologação 07/10: "está sendo muito prolixo"): régua da resposta ao cliente no
// WhatsApp, a mesma dos prompts. Até 3 frases curtas, até 320 caracteres e sem fecho de
// cortesia genérico. `ferramentas/avaliar-agente.mjs` conta quem passa da régua.
export const LIMITE_FRASES = 3
export const LIMITE_CARACTERES = 320
const FLOREIOS = ['fico a disposicao', 'estamos a disposicao', 'qualquer duvida', 'nao hesite', 'sera um prazer', 'conte comigo']

const semAcento = (texto) => texto.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '')

export function objetividade(texto) {
  const limpo = String(texto ?? '').trim()
  // Link não é frase: o ponto do domínio não conta.
  const semLinks = limpo.replace(/https?:\/\/\S+/g, 'link')
  const frases = semLinks.split(/[.!?…]+(?:\s+|$)/).filter((f) => f.trim()).length
  const normalizado = semAcento(limpo)
  const floreios = FLOREIOS.filter((f) => normalizado.includes(f))
  const caracteres = limpo.length
  return {
    frases, caracteres, floreios,
    prolixo: frases > LIMITE_FRASES || caracteres > LIMITE_CARACTERES || floreios.length > 0,
  }
}
