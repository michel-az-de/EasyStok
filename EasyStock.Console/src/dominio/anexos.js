// Frente Anexos (rodada 7, pedido do dono 24/09/2026 04h12: "poder enviar
// arquivos também anexar, enviar áudio, receber áudio" e "cadastro de fotos
// crud também interativo com nome e descrição"). Domínio puro: nenhum import
// fora de `dominio`, nada de DOM/File/Blob/Buffer aqui dentro, tudo chega e
// sai como número, string ou array (ferramentas/verificar-camadas.mjs e a
// regra de portabilidade Node+navegador do gerador de áudio simulado).

// --- Arquivo anexado (imagem ou PDF) ----------------------------------------

export const TIPOS_IMAGEM_ACEITOS = ['image/jpeg', 'image/png', 'image/webp', 'image/gif']
export const TIPO_PDF = 'application/pdf'
export const LIMITE_ARQUIVO_BYTES = 8 * 1024 * 1024

// 'imagem' reaproveita a bolha que a carta de cardápio já usa (miniatura com
// legenda); 'pdf' vira o cartão do arquivo (seção B da pesquisa: "Prévia com
// miniatura, nome do arquivo, badge de tipo"). Fora dessas duas, a casa não
// promete formato que não sabe mostrar.
export function tipoDeArquivo(mime) {
  if (TIPOS_IMAGEM_ACEITOS.includes(mime)) return 'imagem'
  if (mime === TIPO_PDF) return 'pdf'
  return null
}

export function validarArquivo({ tipo, tamanho }) {
  const reconhecido = tipoDeArquivo(tipo)
  if (!reconhecido) return { aceito: false, motivo: 'Envie uma imagem (JPG, PNG, WEBP, GIF) ou um PDF.' }
  if (tamanho <= 0) return { aceito: false, motivo: 'Arquivo vazio.' }
  if (tamanho > LIMITE_ARQUIVO_BYTES) return { aceito: false, motivo: 'Arquivo muito grande. O limite é 8 MB.' }
  return { aceito: true, motivo: null }
}

// "482 KB", "1,2 MB": vírgula decimal (pt-BR), sem casa decimal em KB porque
// ninguém lê "482,3 KB" num cartão de anexo.
export function formatarTamanho(bytes) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} MB`
}

// --- Áudio -------------------------------------------------------------------

export const LIMITE_AUDIO_MS = 120000

export const duracaoExcedida = (ms) => ms > LIMITE_AUDIO_MS

// Relógio de gravação e player, "0:07", "1:32": diferente de `formato.js:duracao`
// (que arredonda para "8 min", pensado para "há quanto tempo", não para o
// cronômetro de um áudio de poucos segundos).
export function formatarDuracao(ms) {
  const segundosTotais = Math.max(0, Math.round(ms / 1000))
  const minutos = Math.floor(segundosTotais / 60)
  const segundos = segundosTotais % 60
  return `${minutos}:${String(segundos).padStart(2, '0')}`
}

// Fração tocada (0 a 1) para a barra de progresso. Guardado contra áudio sem
// metadado carregado ainda (`duracaoTotal` 0 ou NaN não pode virar Infinity).
export function fracaoTocada(atualMs, duracaoTotalMs) {
  if (!duracaoTotalMs || Number.isNaN(duracaoTotalMs)) return 0
  return Math.min(1, Math.max(0, atualMs / duracaoTotalMs))
}

// Amplitude simulada da onda enquanto grava (seção B da pesquisa: "pode ser
// simulada, não precisa FFT real no protótipo"). Determinística por posição
// (sem Math.random em domínio puro): soma de duas ondas primas entre si para
// não repetir período curto, sempre entre 0.15 e 1.
export function amplitudeSimulada(indice) {
  const a = Math.sin(indice * 0.9) * 0.5 + 0.5
  const b = Math.sin(indice * 2.3 + 1) * 0.5 + 0.5
  return 0.15 + 0.85 * ((a + b) / 2)
}

// Codificador base64 sem `Buffer` nem `btoa`: este arquivo roda no navegador
// (chamado de dentro do reducer, via `casos/simulacao.js`) E no teste Node
// puro. Alfabeto padrão RFC 4648.
const ALFABETO_BASE64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'

export function paraBase64(bytes) {
  let saida = ''
  for (let i = 0; i < bytes.length; i += 3) {
    const b0 = bytes[i]
    const b1 = i + 1 < bytes.length ? bytes[i + 1] : null
    const b2 = i + 2 < bytes.length ? bytes[i + 2] : null
    saida += ALFABETO_BASE64[b0 >> 2]
    saida += ALFABETO_BASE64[((b0 & 0x03) << 4) | (b1 === null ? 0 : b1 >> 4)]
    saida += b1 === null ? '=' : ALFABETO_BASE64[((b1 & 0x0f) << 2) | (b2 === null ? 0 : b2 >> 6)]
    saida += b2 === null ? '=' : ALFABETO_BASE64[b2 & 0x3f]
  }
  return saida
}

// Áudio fictício do cliente no Simular (US do dono, 24/09/2026: "receber
// áudio"; cenário "Cliente manda áudio"). Um tom curto e suave (PCM 8 bits,
// 8 kHz mono: arquivo pequeno, header WAV de 44 bytes) para o <audio> da
// bolha ter duração e forma de onda de verdade, sem depender de asset binário
// no repositório nem de rede.
export function gerarAudioSimulado(duracaoMs) {
  const taxaAmostragem = 8000
  const numAmostras = Math.max(1, Math.round((taxaAmostragem * duracaoMs) / 1000))
  const dados = new Uint8Array(numAmostras)
  for (let i = 0; i < numAmostras; i += 1) {
    // Envelope curto no início e no fim (evita estalo de corte abrupto) sobre
    // um tom de 300 Hz, grave o bastante para soar como voz e não como apito.
    const envelope = Math.min(1, i / 400, (numAmostras - i) / 400)
    const onda = Math.sin((2 * Math.PI * 300 * i) / taxaAmostragem)
    dados[i] = Math.round(128 + envelope * 40 * onda)
  }

  const tamanhoDados = dados.length
  const cabecalho = new Uint8Array(44)
  const vista = new DataView(cabecalho.buffer)
  const texto = (offset, str) => { for (let i = 0; i < str.length; i += 1) cabecalho[offset + i] = str.charCodeAt(i) }
  texto(0, 'RIFF')
  vista.setUint32(4, 36 + tamanhoDados, true)
  texto(8, 'WAVE')
  texto(12, 'fmt ')
  vista.setUint32(16, 16, true) // tamanho do subchunk fmt
  vista.setUint16(20, 1, true) // PCM
  vista.setUint16(22, 1, true) // mono
  vista.setUint32(24, taxaAmostragem, true)
  vista.setUint32(28, taxaAmostragem, true) // byteRate (1 byte/amostra)
  vista.setUint16(32, 1, true) // blockAlign
  vista.setUint16(34, 8, true) // bitsPerSample
  texto(36, 'data')
  vista.setUint32(40, tamanhoDados, true)

  const arquivo = new Uint8Array(44 + tamanhoDados)
  arquivo.set(cabecalho, 0)
  arquivo.set(dados, 44)
  return `data:audio/wav;base64,${paraBase64(arquivo)}`
}

// --- Peças da galeria (cadastro de fotos, US do dono: "cadastro de fotos crud
// também interativo com nome e descrição") -----------------------------------
// Uma peça é uma foto pronta para enviar na conversa: prato do cardápio,
// "Horário de funcionamento", ou qualquer outra que a dona cadastrar. CRUD
// simples (id, nome, descrição, foto), sem ligação viva com `catalogo.cardapio`
// depois de criada: editar o preço do prato não deveria mudar, sozinho, uma
// legenda que a dona escreveu à mão.

// Cada problema carrega o campo (para marcar e focar) e a frase curta que
// aparece junto dele (nunca clique sem resposta). Ordem = ordem visual do
// formulário (foto, nome, descrição): "o primeiro campo vazio" é o primeiro
// nessa ordem, não a ordem de checagem.
export function validarPeca({ nome, descricao, foto }) {
  const problemas = []
  if (!foto) problemas.push({ campo: 'foto', mensagem: 'Escolha uma foto do computador.' })
  if (!nome?.trim()) problemas.push({ campo: 'nome', mensagem: 'Dê um nome para a peça.' })
  if (!descricao?.trim()) problemas.push({ campo: 'descricao', mensagem: 'Escreva uma descrição curta.' })
  return problemas
}

export const pecaPorId = (pecas, id) => pecas.find((p) => p.id === id) ?? null

// Id gerado do nome (mesmo método de `dominio/cardapio.js:gerarSkuNovoItem`):
// quem cadastra pensa em "Torta de limão", não digita id nenhum, e nenhuma
// camada acima (features não importa infra) precisa gerar identificador.
function gerarIdDaPeca(pecas, nome) {
  const base = 'peca-' + (nome
    .normalize('NFD').replace(/[̀-ͯ]/g, '')
    .toLowerCase().replace(/[^a-z0-9 ]/g, '')
    .trim().replace(/\s+/g, '-') || 'nova')
  let id = base
  let n = 2
  while (pecas.some((p) => p.id === id)) { id = `${base}-${n}`; n += 1 }
  return id
}

export function incluirPeca(pecas, dados, agora) {
  const peca = {
    id: gerarIdDaPeca(pecas, dados.nome),
    nome: dados.nome.trim(),
    descricao: dados.descricao.trim(),
    foto: dados.foto,
    criadoEm: new Date(agora).toISOString(),
  }
  return [...pecas, peca]
}

export function editarPeca(pecas, id, dados) {
  return pecas.map((p) => (p.id === id
    ? { ...p, nome: dados.nome.trim(), descricao: dados.descricao.trim(), foto: dados.foto ?? p.foto }
    : p))
}

export const tirarPeca = (pecas, id) => pecas.filter((p) => p.id !== id)

// --- Moldes de mensagem -------------------------------------------------------
// Cada um devolve exatamente os campos extras que `comMensagem` (reducer)
// espalha na mensagem gravada, além de `dir`/`status` que quem despacha já
// resolve. Ponto único: Composer, a galeria e a prévia do Balcão concordam no
// mesmo formato de mensagem sem duplicar a forma em três lugares.

export function mensagemDeImagemAnexada({ nomeArquivo, dataUrl, legenda }) {
  // `nomeArquivo` e `legenda` separados (F06): no modo API a foto sobe com o nome e
  // a legenda vai só se ela escreveu, nunca o nome do arquivo no lugar.
  return {
    formato: 'imagem', arte: dataUrl, texto: legenda?.trim() || nomeArquivo, nomeArquivo, legenda: legenda?.trim() ?? '',
  }
}

export function mensagemDeArquivoAnexado({ nomeArquivo, tamanho, dataUrl }) {
  return {
    formato: 'arquivo', arte: dataUrl, texto: nomeArquivo, nomeArquivo, tamanhoArquivo: tamanho,
  }
}

export function mensagemDeAudio({ dataUrl, duracaoMs }) {
  return { formato: 'audio', arte: dataUrl, duracaoMs, texto: 'Mensagem de áudio' }
}

export function mensagemDePeca({ nome, descricao, foto }) {
  return {
    formato: 'peca', arte: foto, nome, descricao, texto: nome,
  }
}
