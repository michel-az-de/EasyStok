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
import { VARIAVEIS, VARIAVEIS_DA_API, sugestaoDeVariavel } from './automacao.js'

const semAcento = (texto) => texto.normalize('NFD').replace(/[̀-ͯ]/g, '')

export const normalizarBusca = (texto) => semAcento(String(texto ?? '').toLowerCase()).trim()

// Teto do servidor para o atalho (RespostaPronta.AtalhoTamanhoMaximo).
export const ATALHO_MAXIMO = 40

// #3208 (S83): teto do servidor para a categoria (RespostaPronta.CategoriaTamanhoMaximo). Texto livre, opcional.
export const CATEGORIA_MAXIMA = 40
export const ROTULO_SEM_CATEGORIA = 'Sem categoria'

export const normalizarCategoria = (texto) => String(texto ?? '').trim().replace(/\s+/g, ' ') || null

export const motivoDaCategoria = (texto) =>
  (normalizarCategoria(texto)?.length ?? 0) > CATEGORIA_MAXIMA ? `A categoria passa de ${CATEGORIA_MAXIMA} caracteres.` : null

// Categorias já usadas, para sugerir no campo: uma por grafia (sem repetir por caixa ou acento), ordem pt-BR.
export function categoriasUsadas(lista) {
  const vistas = new Map()
  for (const r of lista ?? []) {
    const categoria = normalizarCategoria(r.categoria)
    if (categoria && !vistas.has(normalizarBusca(categoria))) vistas.set(normalizarBusca(categoria), categoria)
  }
  return [...vistas.values()].sort((a, b) => a.localeCompare(b, 'pt-BR'))
}

// Atalho de barra a partir do título: "Enviar cardápio" -> "/enviar-cardapio".
// #2792: o atalho gerado é sugestão do sistema, não texto da pessoa, então já nasce dentro do teto: um título
// longo vira o atalho das primeiras palavras inteiras que cabem em 40 (sem palavra partida quando dá).
export function gerarAtalho(titulo) {
  const slug = semAcento(String(titulo ?? '').toLowerCase())
    .replace(/[^a-z0-9 ]/g, '').trim().replace(/\s+/g, '-')
  const atalho = `/${slug || 'resposta'}`
  if (atalho.length <= ATALHO_MAXIMO) return atalho
  const cabe = atalho.slice(0, ATALHO_MAXIMO)
  const ultimaPalavra = atalho[ATALHO_MAXIMO] === '-' ? ATALHO_MAXIMO : cabe.lastIndexOf('-')
  return ultimaPalavra > 1 ? cabe.slice(0, ultimaPalavra) : cabe
}

function idLivre(lista, base) {
  let id = base
  let n = 2
  while (lista.some((r) => r.id === id)) { id = `${base}-${n}`; n += 1 }
  return id
}

// #3208: o que o formulário compartilhado (componentes/FormResposta) precisa do domínio, num objeto só.
export const REGRAS_DO_FORM = { atalhoMaximo: ATALHO_MAXIMO, categoriaMaxima: CATEGORIA_MAXIMA, gerarAtalho, normalizarCategoria, variaveis: VARIAVEIS_DA_API }

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
    categoria: dados.categoria?.trim() || null,
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
  // `faixa` sempre entra (vazia quando a conversa não tem janela): assim a falta é apontada, nunca calada.
  const valores = {
    nome: conversa?.nome ? primeiroNome(conversa.nome) : '',
    pedido: conversa?.pedido?.numero ? String(conversa.pedido.numero) : '',
    faixa: '',
    ...extras,
  }
  // #2734 (T2): mesma regra do servidor (ModeloTextoAtendimento.cs): só `{minúsculas}`, e só chave própria.
  const marcador = /\{([a-z]+)\}/g
  const chavesNoTexto = [...texto.matchAll(marcador)].map((m) => m[1])
  const temValor = (chave) => Object.hasOwn(valores, chave)
  const faltando = [...new Set(chavesNoTexto.filter((chave) => temValor(chave) && !valores[chave]))]
  const textoResolvido = texto.replace(marcador, (todo, chave) => (temValor(chave) && valores[chave] ? valores[chave] : todo))
  return { texto: textoResolvido, faltando }
}

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

// #2734 (T2): também a grafia que o servidor não preenche mas quis dizer uma variável dele ({nome_cliente}).
const marcadoresDoSistema = (texto) =>
  [...String(texto ?? '').matchAll(MARCADOR)]
    .filter((m) => ROTULO_POR_CHAVE.has(chaveNormalizada(m[1])) || sugestaoDeVariavel(m[1]))

export const variaveisNaoResolvidas = (texto) => [...new Set(marcadoresDoSistema(texto).map((m) => m[0]))]

export function avisoDeVariaveisNaoResolvidas(texto) {
  const frases = variaveisNaoResolvidas(texto).map((marcador) => {
    const escrita = marcador.slice(1, -1)
    const certa = sugestaoDeVariavel(escrita)
    // Grafia que o servidor não preenche: o problema é a escrita, não a falta de dado.
    if (certa && escrita !== certa) return `${marcador} não é preenchido pelo EasyStok: escreva {${certa}}.`
    const chave = chaveNormalizada(escrita.trim())
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

export const ID_ITEM_COBRANCA_PIX = 'cobranca-pix'

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
  if ([resposta.atalho, resposta.titulo, resposta.texto, resposta.categoria].some((c) => normalizarBusca(c).includes(alvo))) return 2
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
    .sort(porGrupo)
}

// #3208: com alguma categoria, o seletor sai em grupos: categorias em ordem alfabética e "Sem categoria" por
// último. O sort é estável, então dentro do grupo vale a relevância da busca. Sem categoria nenhuma, nada muda.
const chaveDoGrupo = (r) => { const c = normalizarCategoria(r.categoria); return c ? normalizarBusca(c) : null }
function porGrupo(a, b) {
  const ka = chaveDoGrupo(a)
  const kb = chaveDoGrupo(b)
  if (ka === kb) return 0
  if (ka === null) return 1
  if (kb === null) return -1
  return ka.localeCompare(kb, 'pt-BR')
}

// Grupos para desenhar a lista já ordenada por `respostasDoSeletor`. `indice` é a posição na lista plana, a mesma
// do destaque e do aria-activedescendant: o teclado anda pelos grupos sem pular nem repetir.
export function agruparRespostasDoSeletor(itens) {
  const grupos = []
  itens.forEach((resposta, indice) => {
    const chave = chaveDoGrupo(resposta)
    let grupo = grupos.find((g) => g.chave === chave)
    if (!grupo) {
      grupo = { chave, rotulo: chave === null ? ROTULO_SEM_CATEGORIA : normalizarCategoria(resposta.categoria), itens: [] }
      grupos.push(grupo)
    }
    grupo.itens.push({ resposta, indice })
  })
  return { agrupado: grupos.some((g) => g.chave !== null), grupos }
}

// "/" no começo do campo e um token sem espaço: o resto é o termo da busca.
export const termoDaBarra = (rascunho) => /^\/(\S*)$/.exec(rascunho ?? '')?.[1] ?? null

// Setas do seletor: dão a volta nas pontas, lista vazia fica em zero.
export const moverDestaque = (indice, delta, total) => (total > 0 ? (indice + delta + total) % total : 0)
