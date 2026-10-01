import { API_BASE } from '../fonteDados'
import { lerSessao, limparSessao } from './sessao'

// Erro de chamada à API com o que a tela precisa para falar com a dona:
// status HTTP, código da API (`error.code` ou `erro`) e mensagem legível.
export class ErroApi extends Error {
  constructor(status, codigo, mensagem) {
    super(mensagem)
    this.status = status
    this.codigo = codigo
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

async function lerCorpo(resposta) {
  const texto = await resposta.text()
  if (!texto) return null
  try { return JSON.parse(texto) } catch { return null }
}

// Envelope da EasyStock.Api: sucesso em `{ data, meta }`; erro em `{ error: { code, message } }`,
// exceto o 409 da janela de 24 h (`{ erro, sugestao }`) e os 401/403 de corpo vazio.
// `formulario` (FormData) vai como multipart: o navegador escreve o Content-Type com o boundary.
export async function chamarApi(caminho, { metodo = 'GET', corpo, formulario, autenticado = true } = {}) {
  const cabecalhos = { Accept: 'application/json' }
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
  const json = await lerCorpo(resposta)
  if (resposta.ok) return json?.data ?? null

  if (resposta.status === 401 && autenticado) {
    limparSessao()
    window.dispatchEvent(new Event(EVENTO_SESSAO_EXPIRADA))
  }
  const codigo = json?.error?.code ?? json?.erro ?? `HTTP_${resposta.status}`
  const mensagem = json?.error?.message ?? MENSAGEM_POR_STATUS[resposta.status] ?? (json?.title ? 'Confira os campos.' : 'Não deu certo. Tente de novo.')
  throw new ErroApi(resposta.status, codigo, mensagem)
}
