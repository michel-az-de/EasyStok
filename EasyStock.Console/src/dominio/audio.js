// Formato da gravação de áudio (#1444). A Meta aceita Ogg/Opus (nota de voz), MP4, MP3, AAC e
// AMR, mas não WebM. O Firefox grava Ogg/Opus e vai direto; o Chrome e o Edge só gravam Opus em
// WebM, que o EasyStok reempacota em Ogg; o Safari grava MP4 (AAC). "audio/mp4" fica por último
// porque no Chrome ele sai com Opus dentro do MP4, formato que a Meta não toca.
const FORMATOS_DE_GRAVACAO = ['audio/ogg;codecs=opus', 'audio/webm;codecs=opus', 'audio/mp4']

// `suporta`: MediaRecorder.isTypeSupported. Sem nenhum dos três, null deixa o navegador escolher
// e o EasyStok recusa com o motivo se não der para enviar.
export function escolherFormatoGravacao(suporta) {
  if (typeof suporta !== 'function') return null
  return FORMATOS_DE_GRAVACAO.find((tipo) => suporta(tipo)) ?? null
}

const EXTENSAO_DO_TIPO = {
  'audio/ogg': 'ogg', 'audio/webm': 'webm', 'audio/mp4': 'm4a', 'audio/mpeg': 'mp3', 'audio/aac': 'aac',
}

// Nome do arquivo no multipart: só informativo, o EasyStok reconhece o formato pelos bytes.
export function nomeDoArquivoDeAudio(tipo) {
  const base = String(tipo ?? '').split(';')[0].trim().toLowerCase()
  const extensao = EXTENSAO_DO_TIPO[base]
  return extensao ? `audio.${extensao}` : 'audio'
}
