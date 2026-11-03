import { API_BASE } from '../fonteDados'
import { chamarApi } from './cliente'
import { listarLojasDaEmpresa } from './lojasApi'

export const prepararImpresso = (pedidoId, corpo, sinal) =>
  chamarApi(`/api/pedidos/${pedidoId}/impressoes`, { metodo: 'POST', corpo, sinal })
// #1839: o papel para a tela, sem guardar tentativa no histórico.
export const previaDoImpresso = (pedidoId, corpo, sinal) =>
  chamarApi(`/api/pedidos/${pedidoId}/impressoes/previa`, { metodo: 'POST', corpo, texto: true, sinal, prazoComCancelamento: true })
// #1839: o resumo do Encerrar no layout dos impressos; a API não guarda tentativa.
export const resumoDoAtendimentoImpresso = (modelo, corpo, sinal) =>
  chamarApi(`/api/impressao/resumo-atendimento?modelo=${encodeURIComponent(modelo)}`, { metodo: 'POST', corpo, texto: true, sinal, prazoComCancelamento: true })
export const obterOriginalImpresso = (pedidoId, id, sinal) =>
  chamarApi(`/api/pedidos/${pedidoId}/impressoes/${id}/html`, { texto: true, sinal, prazoComCancelamento: true })
export const historicoDoPedido = (pedidoId, pagina, sinal) =>
  chamarApi(`/api/pedidos/${pedidoId}/impressoes?pagina=${pagina}`, { sinal, prazoComCancelamento: true })
export const confirmarDocumentoImpresso = (id, canhotoId = null) =>
  chamarApi(`/api/impressao/${id}/confirmar-documento`, { metodo: 'POST', corpo: { canhotoId } })

// Caminho relativo da API vira endereço que o <img> abre.
export const urlDoLogo = (url) => (typeof url === 'string' && url ? (url.startsWith('/') ? API_BASE + url : url) : null)

// A loja que recebe o logo: a primeira ativa da empresa logada (tenant único, uma loja).
export async function obterLojaDaEmpresa() {
  const lojas = await listarLojasDaEmpresa()
  const loja = lojas.find((l) => l.ativa !== false) ?? null
  if (!loja) throw new Error('Nenhuma loja ativa encontrada para esta empresa.')
  return { id: loja.id, nome: loja.nome ?? '', logoUrl: urlDoLogo(loja.logoUrl) }
}

// A URL como a API gravou na loja: é ela que o site guarda também (#2070).
export async function enviarArquivoDoLogo(lojaId, arquivo) {
  const formulario = new FormData()
  formulario.append('file', arquivo)
  const resultado = await chamarApi(`/api/uploads/loja/logo?lojaId=${encodeURIComponent(lojaId)}`, {
    metodo: 'POST', formulario,
  })
  return resultado?.url ?? null
}

export const enviarLogoDaLoja = async (lojaId, arquivo) => urlDoLogo(await enviarArquivoDoLogo(lojaId, arquivo))
