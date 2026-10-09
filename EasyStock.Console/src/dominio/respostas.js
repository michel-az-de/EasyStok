// Biblioteca de respostas prontas (pedido do dono, 24/09/2026: "respostas
// deveria abrir todas as respostas automáticas com possibilidade de enviar,
// editar ali na hora e até cadastrar uma nova"). Junta resposta rápida e
// texto interno num catálogo só, editável e arquivável, no mesmo molde do
// cardápio editável (dominio/cardapio.js): nunca apaga, arquivar alterna.
//
// Mensagem automática (regra de dominio/automacao.js) NÃO nasce aqui, nem
// ganha cópia: a biblioteca só lê `estado.regras` e edita pela mesma porta
// que `features/automacoes/ModalAutomacoes.jsx` já usa (EDITAR_REGRA,
// ALTERNAR_REGRA), então editar uma automática na biblioteca É editar a
// mesma que a outra tela mostra.
import { primeiroNome } from './mensagem.js'
import { PASSOS } from './esteira.js'
import { VARIAVEIS } from './automacao.js'

const semAcento = (texto) => texto.normalize('NFD').replace(/[̀-ͯ]/g, '')

export const normalizarBusca = (texto) => semAcento(String(texto ?? '').toLowerCase()).trim()

// Atalho de barra a partir do título: "Enviar cardápio" -> "/enviar-cardapio".
export function gerarAtalho(titulo) {
  const slug = semAcento(String(titulo ?? '').toLowerCase())
    .replace(/[^a-z0-9 ]/g, '').trim().replace(/\s+/g, '-')
  return `/${slug || 'resposta'}`
}

function idLivre(lista, base) {
  let id = base
  let n = 2
  while (lista.some((r) => r.id === id)) { id = `${base}-${n}`; n += 1 }
  return id
}

// ---------------------------------------------------------------------------
// Semente: uma linha por resposta rápida e por texto interno (infra/catalogo.js
// hoje), id estável por origem (nunca muda depois de gerado) e `arquivada:
// false`. Chamada uma vez só, na largada do catálogo.
// ---------------------------------------------------------------------------
export function semearRespostasProntas(respostasRapidas, templatesInternos) {
  const deRapida = respostasRapidas.map((r) => ({
    id: `rr-${gerarAtalho(r.titulo).slice(1)}`,
    titulo: r.titulo,
    categoria: r.cat,
    atalho: gerarAtalho(r.titulo),
    texto: r.texto,
    arquivada: false,
  }))
  const deTemplate = templatesInternos.map((t) => ({
    id: `ti-${t.id}`,
    titulo: t.nome,
    categoria: t.grupo,
    atalho: gerarAtalho(t.nome),
    texto: t.texto,
    arquivada: false,
  }))
  return [...deRapida, ...deTemplate]
}

// ---------------------------------------------------------------------------
// CRUD imutável, mesmo molde de incluirItemCardapio/editarItemCardapio/
// alternarRemocaoItem (dominio/cardapio.js). `estado.catalogo.respostasProntas`
// mora no reducer pelo mesmo caminho do cardápio editável (aplicacao/casos/respostas.js).
// ---------------------------------------------------------------------------
export function incluirRespostaPronta(lista, dados) {
  const id = idLivre(lista, `custom-${gerarAtalho(dados.titulo).slice(1)}`)
  const item = {
    id,
    titulo: dados.titulo.trim(),
    categoria: dados.categoria?.trim() || 'Geral',
    atalho: dados.atalho?.trim() || gerarAtalho(dados.titulo),
    texto: dados.texto.trim(),
    arquivada: false,
  }
  return { lista: [...lista, item], id }
}

export function editarRespostaPronta(lista, id, dados) {
  return lista.map((r) => (r.id === id ? { ...r, ...dados } : r))
}

// Nunca apaga: marca arquivada e a mesma ação repõe (mesmo padrão de
// alternarRemocaoItem). Some da busca padrão, mantém histórico de envio.
export function alternarArquivamentoRespostaPronta(lista, id) {
  return lista.map((r) => (r.id === id ? { ...r, arquivada: !r.arquivada } : r))
}

// ---------------------------------------------------------------------------
// Variáveis {nome} e {pedido}: resolve pela conversa aberta, e diz o que
// faltou em vez de inventar um valor. A automática tem contexto próprio, com
// respiro seguro (dominio/automacao.js, contextoDePrevia) porque dispara sem
// ninguém olhar antes; aqui é a atendente quem vê o texto antes de mandar, e
// prefere ver o aviso a mandar um valor errado calado.
// ---------------------------------------------------------------------------
const ROTULO_VARIAVEL = { nome: 'nome do cliente', pedido: 'número do pedido', faixa: 'janela de entrega' }

// `extras` recebe variável já resolvida por quem chama (ex.: `faixa`, que
// ModalBiblioteca já calcula com o mesmo respiro de contextoDePrevia): entra
// no mesmo balaio de nome/pedido, sem sujeito próprio de "faltando" para não
// duplicar aviso que a automática já trata do jeito dela.
export function resolverVariaveis(texto, conversa, extras = {}) {
  const valores = {
    nome: conversa?.nome ? primeiroNome(conversa.nome) : '',
    pedido: conversa?.pedido?.numero ? String(conversa.pedido.numero) : '',
    ...extras,
  }
  const chavesNoTexto = [...texto.matchAll(/\{(\w+)\}/g)].map((m) => m[1])
  const faltando = [...new Set(chavesNoTexto.filter((chave) => chave in valores && !valores[chave]))]
  const textoResolvido = texto.replace(/\{(\w+)\}/g, (todo, chave) => (valores[chave] ? valores[chave] : todo))
  return { texto: textoResolvido, faltando }
}

export const avisoDeFaltando = (faltando) => (faltando.length === 0
  ? null
  : `Sem ${faltando.map((c) => ROTULO_VARIAVEL[c]).join(' e ')} nesta conversa.`)

// #1474: variável do sistema que sobrou no texto do campo ("Pedido {pedido}.") sairia literal
// para o cliente. Só as que o sistema conhece bloqueiam (as de `VARIAVEIS`, mais as de
// ROTULO_VARIAVEL); texto da dona entre chaves, como {CUPOM10} ou {1}, segue. Reconhece a
// variável escrita com acento, maiúscula ou espaço ({endereço}, {Pedido}, { nome }), porque
// também sairia literal. Uma por grafia, na ordem em que aparece, do jeito que ela escreveu.
const ROTULOS_DO_SISTEMA = {
  ...Object.fromEntries(VARIAVEIS.map((v) => [v.chave, v.descricao])),
  abre: 'horário em que a loja abre de novo',
  linkCardapio: 'link do cardápio',
  endereco: 'endereço de entrega',
  ...ROTULO_VARIAVEL,
}
const chaveNormalizada = (chave) => semAcento(chave).toLowerCase()
const ROTULO_POR_CHAVE = new Map(Object.entries(ROTULOS_DO_SISTEMA).map(([chave, rotulo]) => [chaveNormalizada(chave), rotulo]))
const MARCADOR = /\{\s*([\p{L}\p{N}_]+)\s*\}/gu

const marcadoresDoSistema = (texto) =>
  [...String(texto ?? '').matchAll(MARCADOR)].filter((m) => ROTULO_POR_CHAVE.has(chaveNormalizada(m[1])))

export const variaveisNaoResolvidas = (texto) => [...new Set(marcadoresDoSistema(texto).map((m) => m[0]))]

export function avisoDeVariaveisNaoResolvidas(texto) {
  const frases = variaveisNaoResolvidas(texto).map((marcador) => {
    const chave = chaveNormalizada(marcador.slice(1, -1).trim())
    return `Esta conversa não tem ${ROTULO_POR_CHAVE.get(chave)}: apague ${marcador} ou escreva no lugar.`
  })
  return frases.length === 0 ? null : frases.join(' ')
}

// ---------------------------------------------------------------------------
// Leitura unificada para a biblioteca (Modal e atalho "/" no composer):
// resposta pronta (ativa, ou também arquivada quando pedido) e automática do
// sistema, um item por linha, mesmo formato pras duas telas nunca divergirem.
// ---------------------------------------------------------------------------
export const CATEGORIA_AUTOMATICA = 'Automáticas do sistema'

const itemDeRespostaPronta = (r) => ({
  id: r.id, tipo: 'pronta', titulo: r.titulo, categoria: r.categoria,
  atalho: r.atalho, texto: r.texto, arquivada: r.arquivada, ativa: true, regraId: null,
  editavel: true, descricao: null,
})

// Achado 3, P1 (banca 10): a mesma regra perdia a `descricao` (o que dispara
// ela) ao entrar na biblioteca, então editando por aqui ela via só nome e
// texto, nunca a condição. `descricao` já existe em REGRAS_PADRAO
// (dominio/automacao.js) e features/automacoes/ModalAutomacoes.jsx já
// mostra; só faltava copiar para o item.
const itemDeRegra = (regra) => ({
  id: `regra-${regra.id}`, tipo: 'automatica', titulo: regra.nome, categoria: CATEGORIA_AUTOMATICA,
  atalho: gerarAtalho(regra.nome), texto: regra.texto, arquivada: false, ativa: regra.ativa,
  regraId: regra.id, editavel: true, descricao: regra.descricao,
})

// ---------------------------------------------------------------------------
// Achado 2, P0 (banca 10): Pix e as cinco mensagens de mudança de status da
// esteira (dominio/esteira.js#PASSOS) são geradas e marcadas "automática" na
// conversa, mas não entravam neste catálogo — quem quisesse reenviar, rever
// ou achar "o que a casa falou sozinha" nesses casos não achava onde. Os dois
// grupos abaixo somam à mesma categoria, junto das seis regras de
// dominio/automacao.js.
//
// Diferente das regras de automacao.js, o texto da esteira e da cobrança não
// é um campo solto no estado (`estado.regras`): a esteira é um passo fixo do
// pedido e a cobrança é gerada com valor, link e copia e cola de CADA pedido
// (dominio/cobranca.js#textoDaCobranca). Editar isso pediria mover as duas
// coisas para dentro do reducer, mudança grande demais para o achado que
// pediu "aparecer na lista" — mesmo corte que "Modelos aprovados" já usa
// nesta tela (`features/respostas/ModalBiblioteca.jsx`, texto fixo, sem
// edição). `editavel: false` aqui é esse mesmo corte, não descuido.
// ---------------------------------------------------------------------------
const ROTULO_PASSO_BIBLIOTECA = {
  pago: 'Esteira: pagamento confirmado',
  preparo: 'Esteira: pedido em preparo',
  embalado: 'Esteira: pedido pronto e embalado',
  entrega: 'Esteira: pedido saiu para entrega',
  entregue: 'Esteira: pedido entregue',
}

const DESCRICAO_PASSO_BIBLIOTECA = {
  pago: 'Sai quando a dona marca o pedido como pago na esteira.',
  preparo: 'Sai quando a dona marca "Em preparo" na esteira.',
  embalado: 'Sai quando a dona marca "Embalado" na esteira.',
  entrega: 'Sai quando a dona marca "Em entrega" na esteira, com o nome de quem leva.',
  entregue: 'Sai quando a dona marca "Entregue" na esteira.',
}

const itemDePassoEsteira = (passo) => {
  const titulo = ROTULO_PASSO_BIBLIOTECA[passo.id] ?? passo.rotulo
  return {
    id: `passo-${passo.id}`, tipo: 'automatica', titulo, categoria: CATEGORIA_AUTOMATICA,
    atalho: gerarAtalho(titulo), texto: passo.mensagem, arquivada: false, ativa: true,
    regraId: `esteira-${passo.id}`, editavel: false, descricao: DESCRICAO_PASSO_BIBLIOTECA[passo.id] ?? null,
  }
}

export const ID_ITEM_COBRANCA_PIX = 'cobranca-pix'

// `texto: null` de propósito: quem exibe (ModalBiblioteca) resolve pelo
// pedido e cobrança da conversa aberta (dominio/cobranca.js#textoDaCobranca),
// porque o texto muda por pedido (valor, link, copia e cola). `normalizarBusca`
// trata null como string vazia, então a busca por "pix" ainda acha este item
// pelo título e pelo atalho.
const ITEM_COBRANCA_PIX = {
  id: ID_ITEM_COBRANCA_PIX,
  tipo: 'automatica',
  titulo: 'Cobrança gerada (Pix ou cartão por link)',
  categoria: CATEGORIA_AUTOMATICA,
  atalho: '/cobranca-pix',
  texto: null,
  arquivada: false,
  ativa: true,
  regraId: ID_ITEM_COBRANCA_PIX,
  editavel: false,
  descricao: 'Sai assim que a dona gera a cobrança do pedido, com valor, link e copia e cola.',
}

export function listarBiblioteca({ respostasProntas, regras, incluirArquivadas = false }) {
  const prontas = respostasProntas
    .filter((r) => incluirArquivadas || !r.arquivada)
    .map(itemDeRespostaPronta)
  const automaticas = regras.map(itemDeRegra)
  const daEsteira = PASSOS.filter((p) => p.mensagem).map(itemDePassoEsteira)
  return [...automaticas, ...daEsteira, ITEM_COBRANCA_PIX, ...prontas]
}

// ---------------------------------------------------------------------------
// Item D (banca 10): a etiqueta "automática" do balão da conversa
// (features/atendimento/Balao.jsx) leva direto à definição, sem procurar
// pelo nome. `mensagem.regra` (aplicacao/reducer.js) é o elo: id de
// REGRAS_PADRAO na maioria dos casos, `esteira-<passo>` ou `cobranca-pix`
// para os itens que este arquivo acabou de somar. O que não bate com nada
// aqui (resumo de atendimento, cancelamento, a resposta do agente de fora de
// área) devolve `null`: quem chama abre a biblioteca inteira, sem foco, e
// ainda é uma ação de verdade, não nada.
// ---------------------------------------------------------------------------
const PREFIXO_ESTEIRA = 'esteira-'
// 'captura' (rodada 11, registro 92): as perguntas do automático que montam o
// cadastro saem de `dominio/captura.js`, não de uma regra editável.
const SEM_DEFINICAO_NA_BIBLIOTECA = new Set(['resumo', 'cancelamento', 'fora-de-area', 'saudacao-recorrente', 'captura'])

export function itemDaBibliotecaPelaRegra(regra) {
  if (!regra) return null
  if (regra === ID_ITEM_COBRANCA_PIX) return ID_ITEM_COBRANCA_PIX
  if (regra.startsWith(PREFIXO_ESTEIRA)) return `passo-${regra.slice(PREFIXO_ESTEIRA.length)}`
  if (SEM_DEFINICAO_NA_BIBLIOTECA.has(regra)) return null
  return `regra-${regra}`
}

export function filtrarBiblioteca(itens, termo) {
  const alvo = normalizarBusca(termo)
  if (!alvo) return itens
  return itens.filter((item) => [item.titulo, item.atalho, item.categoria, item.texto]
    .some((campo) => normalizarBusca(campo).includes(alvo)))
}

// Automáticas do sistema sempre primeiro (foi o pedido que trouxe a
// biblioteca inteira), o resto em ordem alfabética do nome da categoria.
const ORDEM_CATEGORIA = [CATEGORIA_AUTOMATICA]

export function agruparPorCategoria(itens) {
  const porCategoria = new Map()
  for (const item of itens) {
    const lista = porCategoria.get(item.categoria) ?? []
    lista.push(item)
    porCategoria.set(item.categoria, lista)
  }
  const categorias = [...porCategoria.keys()].sort((a, b) => {
    const pa = ORDEM_CATEGORIA.indexOf(a)
    const pb = ORDEM_CATEGORIA.indexOf(b)
    if (pa !== -1 || pb !== -1) return (pa === -1 ? 99 : pa) - (pb === -1 ? 99 : pb)
    return a.localeCompare(b, 'pt-BR')
  })
  return categorias.map((categoria) => ({ categoria, itens: porCategoria.get(categoria) }))
}

// ---------------------------------------------------------------------------
// Seletor rápido do compositor (#1441, homologação de 07/10: "uma resposta
// rápida e já bem direta"). Só resposta pronta ativa: automática é do sistema
// e mora na Gestão, não no caminho do atendimento. Ordem: atalho que começa
// com o termo, depois título que começa, depois quem só contém; empate pelo
// título. Sem termo, ordem alfabética do título.
// ---------------------------------------------------------------------------
const semBarra = (atalho) => String(atalho ?? '').replace(/^\//, '')

function pesoNoSeletor(resposta, alvo) {
  if (!alvo) return 0
  if (normalizarBusca(semBarra(resposta.atalho)).startsWith(alvo)) return 0
  if (normalizarBusca(resposta.titulo).startsWith(alvo)) return 1
  if ([resposta.atalho, resposta.titulo, resposta.texto].some((c) => normalizarBusca(c).includes(alvo))) return 2
  return null
}

export function respostasDoSeletor({ respostasProntas, termo = '', limite = 8 }) {
  const alvo = normalizarBusca(semBarra(termo))
  return (respostasProntas ?? [])
    .filter((r) => !r.arquivada)
    .map((r) => ({ r, peso: pesoNoSeletor(r, alvo) }))
    .filter(({ peso }) => peso !== null)
    .sort((a, b) => a.peso - b.peso || a.r.titulo.localeCompare(b.r.titulo, 'pt-BR'))
    .slice(0, limite)
    .map(({ r }) => r)
}

// "/" no começo do campo e um token sem espaço: o resto é o termo da busca.
export const termoDaBarra = (rascunho) => /^\/(\S*)$/.exec(rascunho ?? '')?.[1] ?? null

// Setas do seletor: dão a volta nas pontas, lista vazia fica em zero.
export const moverDestaque = (indice, delta, total) => (total > 0 ? (indice + delta + total) % total : 0)
