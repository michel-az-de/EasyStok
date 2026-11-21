import qrcode from './motor/qrcode'
import './motor/impressao-papeis.js'
import './motor/etiqueta-resolver.js'
import './motor/etiqueta-canvas.js'
import fonte400 from './motor/fontes/NunitoSans-400.woff2'
import fonte700 from './motor/fontes/NunitoSans-700.woff2'

// #2247 (impressão v3, G1 item 7): o motor da etiqueta do app, no Console. Os arquivos de
// ./motor são cópia gerada da PWA (node tools/contratos/sincronizar-motor-console.js): a
// prévia e a folha daqui são o mesmo canvas que o app manda para a térmica.
// O motor lê o gerador de QR do global, como na PWA.
globalThis.qrcode ??= qrcode

const motor = () => ({ resolver: globalThis.cdbResolverEtiqueta, canvas: globalThis.cdbEtiquetaCanvas })

export const PONTOS_MM = motor().canvas.PONTOS_MM
export const fonteDoMotorPronta = () => motor().resolver.fontePronta()

// Carrega a Nunito Sans do motor uma vez (os woff2 com hash do Vite). Nunca lança.
export const carregarFonteDoMotor = () => motor().resolver.carregarFonte({ urls: { 400: fonte400, 700: fonte700 } })

// { campos, papel, orientacao } -> o que o motor devolve: { canvas, recusa, avisos, omitidos }.
export const desenharNoMotor = ({ campos, papel, orientacao }) =>
  motor().canvas.previa({ campos, papel, orientacao, codigos: ['qr'] })
