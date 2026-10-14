import { caminhoDoImpresso } from '../../dominio/impressos'
import { API_BASE } from '../fonteDados'
import { chamarApi } from './cliente'
import { lerSessao } from './sessao'

// Impressos do pedido em HTML pronto (#1632): a API devolve a página autocontida, fora do
// envelope, já com o `@page` do papel. O documento "cozinha" sai da rota `comanda`.
export const obterHtmlDoImpresso = (pedidoId, opcoes) =>
  chamarApi(caminhoDoImpresso(pedidoId, opcoes), { texto: true })

export const prepararImpresso = (pedidoId, corpo, sinal) =>
  chamarApi(`/api/pedidos/${pedidoId}/impressoes`, { metodo: 'POST', corpo, sinal })
export const obterOriginalImpresso = (pedidoId, id, sinal) =>
  chamarApi(`/api/pedidos/${pedidoId}/impressoes/${id}/html`, { texto: true, sinal, prazoComCancelamento: true })
export const historicoDoPedido = (pedidoId, pagina, sinal) =>
  chamarApi(`/api/pedidos/${pedidoId}/impressoes?pagina=${pagina}`, { sinal, prazoComCancelamento: true })
export const confirmarDocumentoImpresso = (id, canhotoId = null) =>
  chamarApi(`/api/impressao/${id}/confirmar-documento`, { metodo: 'POST', corpo: { canhotoId } })

const urlDoLogo = (url) => (typeof url === 'string' && url ? (url.startsWith('/') ? API_BASE + url : url) : null)

// A loja que recebe o logo: a primeira ativa da empresa logada (tenant único, uma loja).
export async function obterLojaDaEmpresa() {
  const empresaId = lerSessao()?.empresa?.id
  if (!empresaId) throw new Error('Entre novamente na empresa para continuar.')
  const lojas = await chamarApi(`/api/lojas?empresaId=${encodeURIComponent(empresaId)}`)
  const loja = (lojas ?? []).find((l) => l.ativa !== false) ?? null
  if (!loja) throw new Error('Nenhuma loja ativa encontrada para esta empresa.')
  return { id: loja.id, nome: loja.nome ?? '', logoUrl: urlDoLogo(loja.logoUrl) }
}

export async function enviarLogoDaLoja(lojaId, arquivo) {
  const formulario = new FormData()
  formulario.append('file', arquivo)
  const resultado = await chamarApi(`/api/uploads/loja/logo?lojaId=${encodeURIComponent(lojaId)}`, {
    metodo: 'POST', formulario,
  })
  return urlDoLogo(resultado?.url)
}
