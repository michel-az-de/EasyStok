import { API_BASE } from '../fonteDados'
import { lerSessao, limparSessao } from './sessao'

// Erro de chamada à API com o que a tela precisa para falar com a dona:
// status HTTP, código da API (`error.code` ou `erro`), mensagem legível e o detalhe
// (`error.detail`), que no CANAL_FALHOU traz o motivo devolvido pelo canal (#1339).
// `dados` (`error.details`): no CANAL_FALHOU, a mensagem que o EasyStok gravou como falhou (#1396).
export class ErroApi extends Error {
  constructor(status, codigo, mensagem, detalhe = null, dados = null) {
    super(mensagem)
    this.status = status
    this.codigo = codigo
    this.detalhe = detalhe
    this.dados = dados
  }
}

// Quem escuta este evento (a sessão da aplicação) volta para o login.
export const EVENTO_SESSAO_EXPIRADA = 'easystok:sessao-expirada'

const MENSAGEM_POR_STATUS = {
  401: 'Sessão expirada. Entre de novo.',
  403: 'Seu usuário não tem permissão para isso.',
  404: 'Não encontrado.',
  502: 'O canal não respondeu. Tente de novo.',
}

// #1474: títulos genéricos do GlobalExceptionHandler da API. Com eles o motivo útil está no
// `detail` ("Requisição inválida" sozinho não diz à dona o que fazer).
const MENSAGENS_GENERICAS = new Set([
  'Requisição inválida', 'Violação de regra de negócio', 'Argumento invalido', 'Operacao invalida',
  'Formato invalido', 'Quantidade inválida', 'Registro duplicado', 'Conflito de concorrência',
])

function mensagemDoErro(json, status) {
  const mensagem = json?.error?.message
  const detalhe = json?.error?.detail
  if (typeof detalhe === 'string' && detalhe.trim() && (!mensagem || MENSAGENS_GENERICAS.has(mensagem))) return detalhe
  return mensagem ?? MENSAGEM_POR_STATUS[status] ?? (json?.title ? 'Confira os campos.' : 'Não deu certo. Tente de novo.')
}

const comoJson = (texto) => {
  if (!texto) return null
  try { return JSON.parse(texto) } catch { return null }
}

// Envelope da EasyStock.Api: sucesso em `{ data, meta }`; erro em `{ error: { code, message } }`,
// exceto o 409 da janela de 24 h (`{ erro, sugestao }`) e os 401/403 de corpo vazio.
// `formulario` (FormData) vai como multipart: o navegador escreve o Content-Type com o boundary.
// `texto`: página pronta fora do envelope (canhoto HTML da S20); devolve o corpo cru.
// `arquivo`: binário fora do envelope (mídia da conversa, #1287); devolve o Blob.
// `chave` (#1491): Idempotency-Key das rotas que criam algo caro de duplicar (produção, pedido).
export async function chamarApi(caminho, {
  metodo = 'GET', corpo, formulario, autenticado = true, texto = false, arquivo = false, chave = null,
} = {}) {
  const cabecalhos = { Accept: arquivo ? '*/*' : 'application/json' }
  if (corpo !== undefined) cabecalhos['Content-Type'] = 'application/json'
  if (chave) cabecalhos['Idempotency-Key'] = chave
  if (autenticado) {
    const sessao = lerSessao()
    if (sessao) cabecalhos.Authorization = `Bearer ${sessao.token}`
  }
  let resposta
  try {
    resposta = await fetch(API_BASE + caminho, {
      method: metodo,
      headers: cabecalhos,
      body: formulario ?? (corpo === undefined ? undefined : JSON.stringify(corpo)),
    })
  } catch {
    throw new ErroApi(0, 'SEM_CONEXAO', 'Sem conexão com o EasyStok.')
  }
  if (resposta.ok && arquivo) return resposta.blob()
  const bruto = await resposta.text()
  if (resposta.ok && texto) return bruto
  const json = comoJson(bruto)
  if (resposta.ok) return json?.data ?? null

  if (resposta.status === 401 && autenticado) {
    limparSessao()
    window.dispatchEvent(new Event(EVENTO_SESSAO_EXPIRADA))
  }
  const codigo = json?.error?.code ?? json?.erro ?? `HTTP_${resposta.status}`
  const mensagem = mensagemDoErro(json, resposta.status)
  throw new ErroApi(resposta.status, codigo, mensagem, json?.error?.detail ?? null, json?.error?.details ?? null)
}
