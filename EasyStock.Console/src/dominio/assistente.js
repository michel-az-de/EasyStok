// Assistente da Thatiane (rodada 7, seção D da pesquisa). Mora no balão
// flutuante, nunca dentro do fio: ele fala COM ela sobre o cliente, nunca
// fala pelo cliente nem manda nada sozinho. Cada função aqui é pura, olha
// para o que já existe na conversa e no histórico, e devolve dado pronto
// para um cartão dispensável. Sem React, sem infra, sem `fetch`
// (`ferramentas/verificar-camadas.mjs` reprova import na direção errada).
//
// Âncora: fala do dono 24/09/2026 04h12 ("assistente chatezinho no canto
// fechável... cliente tal é assim... análise de sentimento... revisa
// ortográfica"). US-051/RN-38 (sentimento), US-049/US-050/RN-35/RN-36 (CDC),
// "2.2 O que acende a Thatiane" de `03-ANALISE-DIRETRIZES-CASOS-DE-USO.md`
// ("HD aqui da cabeça"), seção D de `auditoria/pesquisa-funcional-itens-4-a-7.md`.

// Extensão explícita nas três (diferente do resto de dominio/): plain `node`
// do teste puro (`teste-assistente.mjs`) não resolve specifier sem
// extensão como o bundler do Vite resolve; com `.js` os dois entendem igual.
import { resumoFinanceiro, chegouQuando } from './cliente.js'
import { ocorrenciaAberta } from './ocorrencia.js'
import { primeiroNome } from './mensagem.js'

// Sem acento, sem caixa: mesmo espírito do `normalizar` de dominio/agente.js,
// duplicado aqui (duas linhas) para o assistente não depender de um símbolo
// privado de outro módulo.
const normalizar = (s) => (s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

// --- Quem é o cliente --------------------------------------------------

// "HD aqui da cabeça" (entrevista, 2.2): o que a Thatiane já sabe de cor sobre
// quem pediu, pronto num cartão. Números vêm de `resumoFinanceiro`, que já é
// a mesma conta da Ficha (nunca duas contas divergindo).
export function resumoCliente(conversa, historico, agora) {
  if (!conversa) return null
  const { total, ticketMedio, ultimoEm } = resumoFinanceiro(historico)
  const validos = (historico ?? []).filter((p) => p.estado !== 'cancelado')
  // Integração 50: a mesma régua da Ficha. Cliente mostra "cliente desde" e a
  // contagem do cadastro; sem histórico carregado não há soma (dizia
  // "0 pedidos · R$ 0,00" para quem tem 4 pedidos na Ficha).
  const desde = conversa.cliente?.desde
  return {
    nome: conversa.nome,
    tags: conversa.cliente?.tags ?? [],
    chegou: desde ? `cliente desde ${desde}` : chegouQuando(conversa, agora),
    pedidos: conversa.cliente?.pedidos ?? validos.length,
    total,
    ticketMedio,
    ultimoEm,
  }
}

// --- Padrão de compra e sugestão de promoção ----------------------------

const NOMES_DIA = [
  'domingo', 'segunda-feira', 'terça-feira', 'quarta-feira', 'quinta-feira', 'sexta-feira', 'sábado',
]

// Meio-dia foge do fuso empurrar a data ISO ("2026-09-14") para o dia
// anterior ou seguinte quando o navegador não está em UTC.
const diaDaSemana = (iso) => new Date(`${iso}T12:00:00`).getDay()

// "2× Ravióli de abóbora" -> "Ravióli de abóbora".
const nomeDoPrato = (itemTexto) => itemTexto.replace(/^\d+×\s*/, '').trim()

function maisFrequente(lista) {
  const contagem = new Map()
  for (const item of lista) contagem.set(item, (contagem.get(item) ?? 0) + 1)
  let melhor = null
  for (const [chave, vezes] of contagem) {
    if (!melhor || vezes > melhor.vezes) melhor = { chave, vezes }
  }
  return melhor
}

const MINIMO_PEDIDOS_PARA_PADRAO = 2

// Exemplo do dono (24/09, 04h12): "essa pessoa sempre compra de domingo, e
// ela compra muita lasanha, que tal enviar uma promoção?". Só vira "padrão"
// quando o dia OU o prato aparecem em pelo menos metade dos pedidos válidos
// (cancelado não conta): um pedido de cada dia da semana não é padrão, é
// ruído, e ruído lido como padrão é a Thatiane mandando promoção errada.
export function padraoDeCompra(historico, nomeCompleto) {
  const validos = (historico ?? []).filter((p) => p.estado !== 'cancelado')
  if (validos.length < MINIMO_PEDIDOS_PARA_PADRAO) return null

  const diaTop = maisFrequente(validos.map((p) => NOMES_DIA[diaDaSemana(p.em)]))
  const pratoTop = maisFrequente(validos.flatMap((p) => p.itens.map(nomeDoPrato)))
  if (!diaTop || !pratoTop) return null

  const metade = validos.length / 2
  const diaForte = diaTop.vezes >= 2 && diaTop.vezes >= metade
  const pratoForte = pratoTop.vezes >= 2 && pratoTop.vezes >= metade
  if (!diaForte && !pratoForte) return null

  const nome = nomeCompleto ? primeiroNome(nomeCompleto) : 'Oi'
  const sugestaoTexto = pratoForte
    ? `Oi, ${nome}! Vi aqui que ${pratoTop.chave} é a sua favorita. Separei uma condição especial para o seu próximo pedido, quer que eu já reserve?`
    // "${dia} costuma ser o dia" foge do gênero de "todo/toda" e "ao/à" variar
    // com o nome do dia (domingo/sábado é masculino, segunda a sexta é feminino).
    : `Oi, ${nome}! Reparei que ${diaTop.chave} costuma ser o dia que você pede aqui. Que tal já garantir o seu com uma condição especial nessa semana?`

  return {
    dia: diaForte ? diaTop.chave : null,
    prato: pratoForte ? pratoTop.chave : null,
    pedidos: validos.length,
    sugestaoTexto,
  }
}

// --- Sentimento da conversa (US-051, RN-38) -----------------------------

// Lista curta e literal, própria do assistente: não é a mesma lista de
// `dominio/agente.js` (aquela decide se o automático pode responder; esta só
// informa a Thatiane). RN-38: o classificador sugere, nunca prioriza sozinho
// nem responde nada, então o cartão só mostra o rótulo e o trecho.
const MARCAS_NEGATIVAS = [
  'pessimo', 'pessima', 'horrivel', 'muito ruim', 'fria', 'frio mesmo', 'errado', 'errada',
  'nunca mais', 'absurdo', 'revoltante', 'insuportavel', 'demorou muito', 'ninguem responde',
  'decepcion', 'estragad', 'virad', 'azedo', 'azeda', 'passado', 'passada', 'detestei', 'odiei',
  'lamentavel', 'que descaso', 'inaceitavel', 'vergonha', 'nojent', 'cansei disso',
]
const MARCAS_POSITIVAS = [
  'obrigad', 'adorei', 'perfeit', 'delici', 'maravilh', 'amei', 'parabens', 'excelente',
  'adoro', 'nota dez', 'recomendo', 'sensacional', 'show de bola', 'sempre otimo', 'incrivel',
]

// Só as últimas 6 falas do cliente (mesma janela de `montarPrompt`): humor de
// duas semanas atrás não decide a prioridade de agora. Uma passada só, da
// mais recente para trás, e cada mensagem decide sozinha (negativo vence
// empate dentro da própria fala): assim "que ruim, mas obrigada por
// resolver" não lê como positivo só porque a palavra boa veio depois, e uma
// queixa antiga já superada por um agradecimento recente não fica presa.
export function sentimentoDaConversa(mensagens) {
  const doCliente = (mensagens ?? []).filter((m) => m.dir === 'in').slice(-6).reverse()
  for (const msg of doCliente) {
    const alvo = normalizar(msg.texto)
    if (MARCAS_NEGATIVAS.some((m) => alvo.includes(m))) return { classificacao: 'negativo', trecho: msg.texto }
    if (MARCAS_POSITIVAS.some((m) => alvo.includes(m))) return { classificacao: 'positivo', trecho: msg.texto }
  }
  return { classificacao: 'neutro', trecho: null }
}

// --- Reclamação ou devolução (gancho de CDC e de foto suspeita) --------

const PISTAS_RECLAMACAO = [
  'estragad', 'virad', 'azed', 'passad', 'veio errad', 'pedido errad', 'chegou errad',
  'devolu', 'devolv', 'reembols', 'estorn', 'nao deu para comer', 'nao deu pra comer',
]

// Conta como reclamação/devolução por dois sinais, o que vier primeiro: a
// ocorrência já aberta pela frente Reclamação (RN-35, `dominio/ocorrencia.js`)
// OU uma pista literal na última fala do cliente. A ocorrência sozinha não
// basta: ela só nasce depois que alguém consultou o agente (`acoes.js`:
// "ABRIR_OCORRENCIA nasce... de dentro de consultarAgente") ou que o roteiro
// de simulação a abriu direto; a pista cobre o intervalo entre a mensagem
// chegar e qualquer uma dessas duas coisas acontecerem.
function ehReclamacaoOuDevolucao(conversa) {
  if (!conversa) return false
  if (ocorrenciaAberta(conversa.pedido?.ocorrencia)) return true
  const ultima = [...(conversa.mensagens ?? [])].reverse().find((m) => m.dir === 'in')
  if (!ultima) return false
  const alvo = normalizar(ultima.texto)
  return PISTAS_RECLAMACAO.some((p) => alvo.includes(p))
}

// --- Dica de CDC em linguagem de dica (US-049, US-050, RN-35, RN-36) ---

// Conteúdo adaptado da seção "CDC em linguagem de dica para o atendente" de
// `auditoria/pesquisa-funcional-itens-4-a-7.md` (seção D), reescrito curto
// para caber num cartão. É dica para a Thatiane decidir, nunca uma resposta
// que sai sozinha para o cliente.
const CDC_DEFEITO = {
  artigo: 'Art. 18 e Art. 26 do CDC',
  titulo: 'Defeito no alimento',
  dica: 'A casa tem até 30 dias para resolver. Se o defeito compromete a qualidade da comida, '
    + 'o cliente já pode pedir troca, o dinheiro de volta ou abatimento no preço, sem esperar os 30 dias.',
}
const CDC_ARREPENDIMENTO = {
  artigo: 'Art. 49 do CDC',
  titulo: 'Arrependimento da compra',
  dica: 'Vale só para compra fora da loja física, como WhatsApp ou site, prazo de 7 dias. '
    + 'Para comida já entregue e consumida quase não se aplica, mas se o pedido ainda não saiu nem foi aberto, o cliente pode desistir.',
}
const CDC_OFERTA = {
  artigo: 'Art. 35 do CDC',
  titulo: 'Troca sem avisar',
  dica: 'Trocar o prato pedido por outro sem confirmar antes é descumprir a oferta. '
    + 'O cliente decide: leva o prato original, aceita um equivalente ou cancela com o dinheiro de volta.',
}
const PISTAS_ARREPENDIMENTO = ['me arrependi', 'desisti do pedido', 'quero cancelar antes', 'posso cancelar o pedido']
const PISTAS_OFERTA = ['trocou sem avisar', 'mandou outro prato', 'nao era o que eu pedi', 'substituiu sem falar']

// Cada artigo tem a própria pista, checada direto (não passa pelo gancho de
// `ehReclamacaoOuDevolucao`, que só cobre o radical de produto com defeito):
// "me arrependi" e "trocou sem avisar" não têm "estragad"/"devolu" na frase,
// então o gancho genérico nunca os acharia. Oferta e arrependimento primeiro
// porque são mais específicos; defeito de produto é o padrão mais comum
// (RN-35 abre ocorrência sozinha nesse caso), por isso vem por último.
export function dicaCDC(conversa) {
  if (!conversa) return null
  const ultima = [...(conversa.mensagens ?? [])].reverse().find((m) => m.dir === 'in')
  const alvo = normalizar(ultima?.texto)
  if (PISTAS_OFERTA.some((p) => alvo.includes(p))) return CDC_OFERTA
  if (PISTAS_ARREPENDIMENTO.some((p) => alvo.includes(p))) return CDC_ARREPENDIMENTO
  if (ehReclamacaoOuDevolucao(conversa)) return CDC_DEFEITO
  return null
}

// --- Foto suspeita de imagem gerada por IA ------------------------------

const MOTIVOS_POSSIVEIS = [
  'sem metadado de câmera na imagem (EXIF ausente)',
  'textura do prato repete um padrão sintético',
  'sombra do prato não bate com a luz do ambiente',
  'iluminação uniforme demais para uma foto de celular',
  'borda do alimento com contorno artificial demais',
]

// Confiança e motivos vêm de uma marca fictícia, deterministica a partir do
// texto da própria mensagem (não sorteio): é indício simulado para o
// protótipo, nunca uma checagem real de imagem. Por isso o cartão sempre
// avisa "pista, não prova".
function marcaFicticia(texto) {
  let soma = 0
  for (let i = 0; i < texto.length; i += 1) soma += texto.charCodeAt(i)
  return soma
}

export function analisarFotoSuspeita(conversa) {
  if (!ehReclamacaoOuDevolucao(conversa)) return null
  const comFoto = [...(conversa?.mensagens ?? [])].reverse().find((m) => m.dir === 'in' && m.midia)
  if (!comFoto) return null

  const marca = marcaFicticia(comFoto.texto || 'foto')
  const confianca = 0.6 + (marca % 30) / 100 // entre 60% e 89%, estável para o mesmo texto
  const motivos = [...new Set([
    MOTIVOS_POSSIVEIS[marca % MOTIVOS_POSSIVEIS.length],
    MOTIVOS_POSSIVEIS[(marca + 2) % MOTIVOS_POSSIVEIS.length],
  ])]
  return { confianca, motivos }
}

// --- Revisão ortográfica do rascunho ------------------------------------

// Dicionário curto de abreviação/erro comum de chat. Não mexe em "pra"
// (informalidade válida, não erro) nem em gíria: só o que é abreviação ou
// grafia errada de verdade, que numa mensagem para cliente lê como descuido.
const CORRECOES = [
  [/\bvc\b/gi, 'você'], [/\bvcs\b/gi, 'vocês'], [/\bpq\b/gi, 'porque'],
  [/\btbm\b/gi, 'também'], [/\btb\b/gi, 'também'], [/\bobg\b/gi, 'obrigada'],
  [/\bhj\b/gi, 'hoje'], [/\bqdo\b/gi, 'quando'], [/\bmto\b/gi, 'muito'],
  [/\bmt\b/gi, 'muito'], [/\bentaum\b/gi, 'então'], [/\bnaum\b/gi, 'não'],
  [/\baki\b/gi, 'aqui'], [/\bqq\b/gi, 'qualquer'], [/\bblz\b/gi, 'beleza'],
  [/\bflw\b/gi, 'falou'], [/\bmsm\b/gi, 'mesmo'], [/\bvlw\b/gi, 'valeu'],
]

function primeiraMaiuscula(texto) {
  return texto.replace(/(^\s*\p{L}|[.!?]\s+\p{L})/gu, (m) => m.toUpperCase())
}

export function revisarOrtografia(textoOriginal) {
  const texto = textoOriginal ?? ''
  if (!texto.trim()) return { corrigido: texto, mudou: false, motivos: [] }

  let corrigido = texto
  const motivos = []

  for (const [padrao, troca] of CORRECOES) {
    if (padrao.test(corrigido)) {
      motivos.push(`"${troca}" no lugar da abreviação`)
      corrigido = corrigido.replace(padrao, troca)
    }
  }
  if (/ {2,}/.test(corrigido)) {
    motivos.push('espaço duplicado')
    corrigido = corrigido.replace(/ {2,}/g, ' ')
  }
  if (/([!?])\1{1,}/.test(corrigido)) {
    motivos.push('pontuação repetida')
    corrigido = corrigido.replace(/([!?])\1+/g, '$1')
  }
  const comMaiuscula = primeiraMaiuscula(corrigido)
  if (comMaiuscula !== corrigido) {
    motivos.push('maiúscula no início da frase')
    corrigido = comMaiuscula
  }

  corrigido = corrigido.trim()
  return { corrigido, mudou: corrigido !== texto.trim(), motivos }
}

// --- Pergunta livre para a IA de verdade (POST /api/agente) -------------

// Prompt geral, sem o contrato de JSON de `dominio/agente.js` (aquele é só
// para o rascunho de resposta ao cliente): aqui a pergunta é da Thatiane para
// o assistente, sobre a própria conversa, e a resposta é texto solto.
export function montarPromptLivre(pergunta, conversa, historico) {
  const tags = conversa?.cliente?.tags?.length ? ' · ' + conversa.cliente.tags.join(', ') : ''
  const pedidos = (historico ?? []).filter((p) => p.estado !== 'cancelado').length
  const ultimas = (conversa?.mensagens ?? []).slice(-6)
    .map((m) => (m.dir === 'in' ? 'Cliente: ' : 'Casa: ') + m.texto)
    .join('\n')
  return [
    'Você é o assistente interno da Casa da Baba, uma rotisseria de massa artesanal em São Paulo.',
    'Fala só com a atendente Thatiane, nunca com o cliente. Responda direto, em português do Brasil, sem travessão, em poucas frases.',
    '',
    'Cliente: ' + (conversa?.nome ?? 'sem conversa selecionada') + tags,
    `Pedidos anteriores: ${pedidos}`,
    'Conversa recente:',
    ultimas || '(sem mensagens ainda)',
    '',
    'Pergunta da Thatiane: ' + pergunta,
  ].join('\n')
}
