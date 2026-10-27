import qrcode from './qrcode'

// #2081 (A4): QR e código de barras da etiqueta de produção, como o PWA do APK imprime. Copiado do PWA
// no master (EasyStock.Api/wwwroot/pwa/index.html: `generateQrSvg`, `CODE128_PATTERNS`, `code128Values`);
// aqui devolve só a geometria, e o desenho fica com a etiqueta (SVG).

// QR: matriz de módulos escuros. Nível M (cerca de 15% de correção), versão automática.
export function matrizDoQr(texto) {
  const qr = qrcode(0, 'M')
  qr.addData(String(texto ?? ''))
  qr.make()
  const n = qr.getModuleCount()
  return Array.from({ length: n }, (_, linha) => Array.from({ length: n }, (__, coluna) => qr.isDark(linha, coluna)))
}

// Um caminho SVG só, um quadrado por módulo escuro, deslocado pela zona de silêncio.
export function caminhoDoQr(matriz, silencio = 4) {
  let d = ''
  matriz.forEach((linha, y) => linha.forEach((escuro, x) => { if (escuro) d += `M${x + silencio} ${y + silencio}h1v1h-1z` }))
  return d
}

// Code 128, conjunto B (ASCII 32 a 126), com o dígito verificador e o fim. Cada padrão é a largura
// de barra, espaço, barra... em módulos.
const CODE128 = [
  '212222', '222122', '222221', '121223', '121322', '131222', '122213', '122312', '132212', '221213',
  '221312', '231212', '112232', '122132', '122231', '113222', '123122', '123221', '223211', '221132',
  '221231', '213212', '223112', '312131', '311222', '321122', '321221', '312212', '322112', '322211',
  '212123', '212321', '232121', '111323', '131123', '131321', '112313', '132113', '132311', '211313',
  '231113', '231311', '112133', '112331', '132131', '113123', '113321', '133121', '313121', '211331',
  '231131', '213113', '213311', '213131', '311123', '311321', '331121', '312113', '312311', '332111',
  '314111', '221411', '431111', '111224', '111422', '121124', '121421', '141122', '141221', '112214',
  '112412', '122114', '122411', '142112', '142211', '241211', '221114', '413111', '241112', '134111',
  '111242', '121142', '121241', '114212', '124112', '124211', '411212', '421112', '421211', '212141',
  '214121', '412121', '111143', '111341', '131141', '114113', '114311', '411113', '411311', '113141',
  '114131', '311141', '411131', '211412', '211214', '211232', '2331112',
]
const INICIO_B = 104
const FIM = 106

export function valoresCode128(texto) {
  const limpo = String(texto ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^\x20-\x7E]/g, '')
  const valores = [INICIO_B, ...[...limpo].map((c) => c.charCodeAt(0) - 32)]
  const soma = valores.reduce((total, v, i) => total + v * (i === 0 ? 1 : i), 0)
  return [...valores, soma % 103, FIM]
}

// As barras como retângulos em módulos: [{ x, largura }], começando depois da zona de silêncio.
export function barrasDoCode128(texto, silencio = 10) {
  const larguras = valoresCode128(texto).map((v) => CODE128[v]).join('')
  const barras = []
  let x = silencio
  ;[...larguras].forEach((l, i) => {
    const largura = Number(l)
    if (i % 2 === 0) barras.push({ x, largura })
    x += largura
  })
  return { barras, total: x + silencio }
}
