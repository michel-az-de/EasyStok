// Primeiros passos (#2070): o assistente que passa por todas as configurações da loja. O estado mora no
// servidor (`GET api/configuracao-inicial`); aqui ficam o mapa de cada passo (o que a tela monta) e as contas
// puras sobre o estado lido. Cada passo grava pelas rotas que já existem; o assistente só marca o passo.
//
// `monta`: a aba do PainelDeAjuste (com `secoes` da AbaAtendimento ou a `secao` do cadastro de entrega) ou um
// componente próprio do assistente. `chavesDeRascunho`: as chaves do `ContextoRascunhosDosAjustes` que seguram
// o "Conferi, seguir" das abas reaproveitadas; terminada em ":" vale como prefixo. Os formulários novos avisam
// pelo `aoMudarPendencia`.
import { resumoDoWhatsApp } from './canais'

export const MODULO_DOS_PRIMEIROS_PASSOS = 'configuracoes'
export const TELA_PRIMEIROS_PASSOS = 'primeiros-passos'
export const PARTE_ESSENCIAL = 'essencial'
export const PARTE_AJUSTES = 'ajustes'
export const PARTE_FIM = 'fim'
export const PASSO_FINAL = 'pronto'
// Passo que a API conhece e o console ainda não: a tela leva a Configurações, sem rota própria.
export const DESTINO_DESCONHECIDO = `#/m/${MODULO_DOS_PRIMEIROS_PASSOS}`
export const TEXTO_DESCONHECIDO = 'Abrir em Configurações'

const passo = (id, rotulo, parte, descricao, monta, chavesDeRascunho = []) => Object.freeze({ id, rotulo, parte, descricao, monta, chavesDeRascunho })

export const PASSOS = Object.freeze([
  passo('boas-vindas', 'Boas-vindas', PARTE_ESSENCIAL, 'Antes de começar, confira seu nome e o que o suporte já deixou ligado.',
    [{ componente: 'boas-vindas' }]),
  passo('horarios', 'Horários', PARTE_ESSENCIAL, 'Os dias e horários em que a loja atende e as mensagens de quando ela não atende.',
    [{ aba: 'atendimento', secoes: ['horarios'] }], ['horarios-atendimento', 'mensagens-expediente', 'expediente-atendimento']),
  passo('site-e-impressos', 'Site e impressos', PARTE_ESSENCIAL, 'O logo, a cor e o WhatsApp de pedidos que aparecem no site e nos impressos.',
    [{ aba: 'site-e-impressos' }]),
  passo('cardapio', 'Cardápio', PARTE_ESSENCIAL, 'As categorias e os itens que o cliente vê, com preço e foto.',
    [{ aba: 'categorias' }, { aba: 'cardapio' }]),
  passo('janelas', 'Quando a loja entrega', PARTE_ESSENCIAL, 'Os dias, os horários e as vagas de entrega.',
    [{ aba: 'janelas', secao: 'janelas' }], ['janela-de-entrega:']),
  passo('frete', 'Frete', PARTE_ESSENCIAL, 'Quanto custa a entrega em cada bairro ou faixa de CEP.',
    [{ aba: 'janelas', secao: 'zonas' }], ['zona-de-frete:', 'frete-por-distancia']),
  passo('equipe', 'Equipe', PARTE_ESSENCIAL, 'Convide quem trabalha com você como Gerência, Atendimento ou Cozinha. A pessoa entra com a conta Google deste e-mail.',
    [{ aba: 'usuarios' }]),
  passo('dados-da-loja', 'Dados da loja', PARTE_AJUSTES, 'Nome, descrição, documento, endereço e telefone da loja.',
    [{ aba: 'dados-da-loja' }]),
  passo('atendimento', 'Atendimento', PARTE_AJUSTES, 'O tom, as saudações e os tempos do atendimento. Ligar o atendimento automático fica em Atendimento.',
    [{ aba: 'atendimento', secoes: ['configuracao'] }], ['configuracao-atendimento']),
  passo('respostas', 'Respostas', PARTE_AJUSTES, 'As respostas prontas do Balcão e as mensagens automáticas.',
    [{ aba: 'respostas' }]),
  passo('agente', 'Agente', PARTE_AJUSTES, 'O que o atendimento automático sabe sobre a loja e se ele está ligado.',
    [{ aba: 'caderno' }, { aba: 'atendimento', secoes: ['agente'] }]),
  passo('producao', 'Produção', PARTE_AJUSTES, 'Os insumos e as receitas, de onde sai o custo de cada prato.',
    [{ aba: 'insumos' }, { aba: 'receitas' }]),
  passo('estoque', 'Estoque', PARTE_AJUSTES, 'Quando o EasyStok avisa estoque baixo, validade perto do fim e produto parado.',
    [{ aba: 'alertas-de-estoque' }]),
  passo('entregadores', 'Entregadores', PARTE_AJUSTES, 'Quem faz as entregas da loja.',
    [{ aba: 'entregadores' }]),
  passo('impressora', 'Impressora', PARTE_AJUSTES, 'O papel de cada documento. Vale só neste aparelho.',
    [{ aba: 'impressao' }]),
  passo('avisos', 'Avisos', PARTE_AJUSTES, 'O que você recebe e por onde. Ligar os avisos vale só neste aparelho.',
    [{ aba: 'avisos' }]),
  passo(PASSO_FINAL, 'Revisão', PARTE_FIM, 'Confira o que ficou pronto e conclua. O que ficou para depois continua em Configurações.',
    [{ componente: 'pronto' }]),
])

export const ROTULO_DA_PARTE = { [PARTE_ESSENCIAL]: 'Parte 1 · O essencial', [PARTE_AJUSTES]: 'Parte 2 · Mais ajustes' }
export const AVISO_DA_PARTE_2 = 'Cada passo desta parte pode ser pulado.'

const POR_ID = new Map(PASSOS.map((p) => [p.id, p]))
export const passoDoMapa = (id) => POR_ID.get(id) ?? null
export const ehPassoConhecido = (id) => POR_ID.has(id)
export const passosDaParte = (parte) => PASSOS.filter((p) => p.parte === parte)
export const abasDoPasso = (id) => (passoDoMapa(id)?.monta ?? []).filter((m) => m.aba).map((m) => m.aba)

// A linha da API para o passo do mapa. Passo que a API não mandou conta como em aberto e disponível.
const daApi = (estado, id) => estado?.passos?.find((p) => p?.passo === id) ?? null
const situacaoDe = (estado, id) => daApi(estado, id)?.situacao ?? null
const disponivel = (estado, id) => daApi(estado, id)?.disponivel !== false

// O primeiro passo em aberto (sem situação), na ordem do mapa, que o perfil pode abrir; tudo marcado leva à revisão.
export function passoDaRetomada(estado) {
  if (!estado) return PASSOS[0].id
  return PASSOS.find((p) => p.id !== PASSO_FINAL && disponivel(estado, p.id) && situacaoDe(estado, p.id) === null)?.id ?? PASSO_FINAL
}

// Voltar e seguir na ordem do mapa, pulando o passo que o perfil não abre.
export function passoVizinho(id, direcao, estado) {
  let indice = PASSOS.findIndex((p) => p.id === id)
  if (indice < 0) return null
  for (indice += direcao; indice >= 0 && indice < PASSOS.length; indice += direcao) {
    if (PASSOS[indice].id === PASSO_FINAL || disponivel(estado, PASSOS[indice].id)) return PASSOS[indice].id
  }
  return null
}

export function textoDoProgresso(id) {
  const definicao = passoDoMapa(id)
  if (!definicao) return ''
  if (definicao.parte === PARTE_FIM) return 'Revisão final'
  const parte = passosDaParte(definicao.parte)
  return `Passo ${parte.indexOf(definicao) + 1} de ${parte.length} · ${definicao.rotulo}`
}

// Para o cartão do hall: quantos passos da parte já têm situação (conferido ou deixado para depois).
export function progressoDaParte(estado, parte) {
  const passos = passosDaParte(parte)
  return { feitos: passos.filter((p) => situacaoDe(estado, p.id) !== null).length, total: passos.length }
}

// Revisão do passo final. Cada passo cai num grupo só: sem acesso, depois, falta (prontidão ou não conferido), pronto.
export function resumoDaRevisao(estado) {
  const resumo = { pronto: [], falta: [], depois: [], semAcesso: [], desconhecidos: [] }
  for (const { id, rotulo } of PASSOS) {
    if (id === PASSO_FINAL) continue
    const linha = daApi(estado, id)
    const prontidao = linha?.prontidao
    if (linha?.disponivel === false) resumo.semAcesso.push({ passo: id, rotulo, texto: linha.motivo ?? 'Seu perfil não abre este ajuste.' })
    else if (linha?.situacao === 'depois') resumo.depois.push({ passo: id, rotulo })
    else if (prontidao && prontidao.ok === false) resumo.falta.push({ passo: id, rotulo, texto: `Falta ${(prontidao.falta ?? []).join(', ') || 'conferir'}` })
    else if (linha?.situacao === 'conferido') resumo.pronto.push({ passo: id, rotulo })
    else resumo.falta.push({ passo: id, rotulo, texto: 'Falta conferir' })
  }
  resumo.desconhecidos = passosDesconhecidos(estado).map((id) => ({ passo: id, texto: TEXTO_DESCONHECIDO, href: DESTINO_DESCONHECIDO }))
  return resumo
}

export const passosDesconhecidos = (estado) => (estado?.passos ?? []).map((p) => p?.passo).filter((id) => typeof id === 'string' && !ehPassoConhecido(id))

// Rascunho ou gravação em curso numa das chaves do passo.
export function pendenciaDoPasso(id, contexto) {
  const chaves = passoDoMapa(id)?.chavesDeRascunho ?? []
  const abertas = [...Object.keys(contexto?.rascunhos ?? {}), ...Object.keys(contexto?.gravando ?? {})]
  return chaves.some((chave) => abertas.some((aberta) => (chave.endsWith(':') ? aberta.startsWith(chave) : aberta === chave)))
}

const ehHall = (hash) => ['', '#', '#/'].includes(hash ?? '')

// Abre sozinha só do hall (ou sem hash), com o `abrirSozinho` do servidor (dona, pendente, não adiada) e uma vez por
// visita. A entrada do perfil pode já ter trocado o hall pela porta antes desta leitura chegar: vale a hash da entrada.
export function deveAbrirSozinho(estado, { hashAtual, hashNaEntrada = null, hashDaPorta = null, jaAbriuNaVisita = false } = {}) {
  if (estado?.abrirSozinho !== true || jaAbriuNaVisita) return false
  return ehHall(hashAtual) || (hashDaPorta !== null && hashAtual === hashDaPorta && ehHall(hashNaEntrada))
}

// O que o suporte já ligou, só leitura (boas-vindas): cobrança online e mapa de `GET api/integracoes`, WhatsApp do status.
const CONFERINDO = 'Conferindo…'
const SEM_RESPOSTA = 'Não foi possível conferir agora'
function daIntegracao(nome, provider, { integracoes, erroIntegracoes }, ligada, desligada) {
  if (!integracoes) return { nome, situacao: erroIntegracoes ? SEM_RESPOSTA : CONFERINDO, ligado: false }
  const ligado = integracoes.some((i) => i?.provider === provider && i.ativo === true)
  return { nome, situacao: ligado ? ligada : desligada, ligado }
}
export function ligadosPeloSuporte({ integracoes = null, whatsapp, erroIntegracoes = null, erroWhatsApp = null }) {
  const resumo = whatsapp === undefined ? null : resumoDoWhatsApp(whatsapp)
  return [
    daIntegracao('Cobrança online', 'mercadopago', { integracoes, erroIntegracoes }, 'Ligada', 'Ainda não ligada: fale com o suporte'),
    { nome: 'WhatsApp', situacao: resumo ? resumo.titulo : erroWhatsApp ? SEM_RESPOSTA : CONFERINDO, ligado: resumo?.tom === 'ok' },
    daIntegracao('Mapa', 'googlemaps', { integracoes, erroIntegracoes }, 'Ligado', 'Ainda não ligado: fale com o suporte'),
  ]
}
