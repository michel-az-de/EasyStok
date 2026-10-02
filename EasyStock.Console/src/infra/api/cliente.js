import { API_BASE } from '../fonteDados'
import { lerSessao, limparSessao } from './sessao'

// Erro de chamada à API com o que a tela precisa para falar com a dona:
// status HTTP, código da API (`error.code` ou `erro`), mensagem legível e o detalhe
// (`error.detail`), que no CANAL_FALHOU traz o motivo devolvido pelo canal (#1339).
export class ErroApi extends Error {
  constructor(status, codigo, mensagem, detalhe = null) {
    super(mensagem)
    this.status = status
    this.codigo = codigo
    this.detalhe = detalhe
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

const comoJson = (texto) => {
  if (!texto) return null
  try { return JSON.parse(texto) } catch { return null }
}

// Envelope da EasyStock.Api: sucesso em `{ data, meta }`; erro em `{ error: { code, message } }`,
// exceto o 409 da janela de 24 h (`{ erro, sugestao }`) e os 401/403 de corpo vazio.
// `formulario` (FormData) vai como multipart: o navegador escreve o Content-Type com o boundary.
// `texto`: página pronta fora do envelope (canhoto HTML da S20); devolve o corpo cru.
// `arquivo`: binário fora do envelope (mídia da conversa, #1287); devolve o Blob.
export async function chamarApi(caminho, {
  metodo = 'GET', corpo, formulario, autenticado = true, texto = false, arquivo = false,
} = {}) {
  const cabecalhos = { Accept: arquivo ? '*/*' : 'application/json' }
  if (corpo !== undefined) cabecalhos['Content-Type'] = 'application/json'
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
  const mensagem = json?.error?.message ?? MENSAGEM_POR_STATUS[resposta.status] ?? (json?.title ? 'Confira os campos.' : 'Não deu certo. Tente de novo.')
  throw new ErroApi(resposta.status, codigo, mensagem, json?.error?.detail ?? null)
}
