import { chamarApi } from './cliente'

// Inbox do console (S07): `api/atendimento/conversas`. Devolve o formato da API;
// a tradução para o formato do console mora em traducaoConversas.js.
const BASE = '/api/atendimento/conversas'

// A API pagina por `pagina` e corta `limite` em 100 (ListarConversasAtendimentoUseCase).
const POR_PAGINA = 100
const PAGINAS_NO_MAXIMO = 10

export const listarConversas = ({ limite = POR_PAGINA, pagina = 1 } = {}) =>
  chamarApi(`${BASE}?limite=${limite}&pagina=${pagina}`)

// Inbox inteira (F07, item 7): segue as páginas até uma vir incompleta. Conversa que
// subiu de página entre uma chamada e outra aparece uma vez só.
export async function listarTodasConversas() {
  const vistas = new Map()
  for (let pagina = 1; pagina <= PAGINAS_NO_MAXIMO; pagina += 1) {
    const itens = (await listarConversas({ pagina })) ?? []
    for (const item of itens) if (!vistas.has(item.id)) vistas.set(item.id, item)
    if (itens.length < POR_PAGINA) break
  }
  return [...vistas.values()]
}

export const listarMensagens = (id, { limite = 100 } = {}) =>
  chamarApi(`${BASE}/${id}/mensagens?limite=${limite}`)

// Arquivo da mensagem (foto, áudio, documento) do storage privado, autenticado (#1287).
export const baixarMidia = (conversaId, mensagemId) =>
  chamarApi(`${BASE}/${conversaId}/mensagens/${mensagemId}/midia`, { arquivo: true })

export const enviarTexto = (id, texto) =>
  chamarApi(`${BASE}/${id}/mensagens`, { metodo: 'POST', corpo: { texto } })

// Reenvio de texto que falhou (S57): devolve a mensagem atualizada (enviada, ou com o novo erro).
export const reenviarMensagem = (conversaId, mensagemId) =>
  chamarApi(`${BASE}/${conversaId}/mensagens/${mensagemId}/reenviar`, { metodo: 'POST' })

// Mensagens que não chegaram ao cliente, de todas as conversas (S59).
export const listarNaoEntregues = ({ limite = 50 } = {}) => chamarApi(`${BASE}/nao-entregues?limite=${limite}`)

export const assumir = (id) => chamarApi(`${BASE}/${id}/assumir`, { metodo: 'POST' })
export const liberarAutomatico = (id) => chamarApi(`${BASE}/${id}/liberar-automatico`, { metodo: 'POST' })
export const encerrar = (id) => chamarApi(`${BASE}/${id}/encerrar`, { metodo: 'POST' })
export const marcarLida = (id) => chamarApi(`${BASE}/${id}/marcar-lida`, { metodo: 'POST' })

// Link do cardápio da loja ligado à conversa, o mesmo que o agente manda (#1353, S48).
export const gerarLinkCardapio = (id) => chamarApi(`${BASE}/${id}/link-cardapio`, { metodo: 'POST' })

// Cliente da conversa (#1276): cadastra ou atualiza (`{ nome, telefone, endereco }`) e o dossiê (S25)
// que a Ficha relê depois. Conversa sem cliente devolve o dossiê mínimo.
export const cadastrarClienteDaConversa = (id, corpo) => chamarApi(`${BASE}/${id}/cliente`, { metodo: 'POST', corpo })
export const obterDossie = (id) => chamarApi(`${BASE}/${id}/dossie`)

// Foto da dona (S02): multipart com `file` e `legenda`. A imagem chega como data URL
// (o Composer já leu o arquivo para a prévia) e volta a ser binário aqui.
function arquivoDoDataUrl(dataUrl, nomeArquivo) {
  const [cabecalho, base64 = ''] = dataUrl.split(',')
  const tipo = /data:([^;]+)/.exec(cabecalho)?.[1] ?? 'application/octet-stream'
  const binario = atob(base64)
  const bytes = new Uint8Array(binario.length)
  for (let i = 0; i < binario.length; i += 1) bytes[i] = binario.charCodeAt(i)
  return { blob: new Blob([bytes], { type: tipo }), nome: nomeArquivo || 'foto' }
}

export function enviarImagem(id, { dataUrl, nomeArquivo, legenda }) {
  const { blob, nome } = arquivoDoDataUrl(dataUrl, nomeArquivo)
  const formulario = new FormData()
  formulario.append('file', blob, nome)
  if (legenda?.trim()) formulario.append('legenda', legenda.trim())
  return chamarApi(`${BASE}/${id}/mensagens/imagem`, { metodo: 'POST', formulario })
}
