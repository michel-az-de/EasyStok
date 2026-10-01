import { chamarApi } from './cliente'

// Inbox do console (S07): `api/atendimento/conversas`. Devolve o formato da API;
// a tradução para o formato do console mora em traducaoConversas.js.
const BASE = '/api/atendimento/conversas'

export const listarConversas = ({ limite = 50 } = {}) => chamarApi(`${BASE}?limite=${limite}`)

export const listarMensagens = (id, { limite = 100 } = {}) =>
  chamarApi(`${BASE}/${id}/mensagens?limite=${limite}`)

export const enviarTexto = (id, texto) =>
  chamarApi(`${BASE}/${id}/mensagens`, { metodo: 'POST', corpo: { texto } })

export const assumir = (id) => chamarApi(`${BASE}/${id}/assumir`, { metodo: 'POST' })
export const liberarAutomatico = (id) => chamarApi(`${BASE}/${id}/liberar-automatico`, { metodo: 'POST' })
export const encerrar = (id) => chamarApi(`${BASE}/${id}/encerrar`, { metodo: 'POST' })
export const marcarLida = (id) => chamarApi(`${BASE}/${id}/marcar-lida`, { metodo: 'POST' })

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
