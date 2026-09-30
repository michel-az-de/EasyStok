// Emissão de cobrança Pix. É infraestrutura de propósito: identificador, código
// copia e cola e link são coisas que um provedor emite, não regra da casa.
//
// AQUI ENTRA O PROVEDOR REAL (Mercado Pago ou Asaas, por webhook). Esta função
// vira uma chamada HTTP que devolve txid, BR Code e link da cobrança, e a baixa
// deixa de ser botão na tela: chega pelo webhook do provedor e cai no mesmo
// despacho de pagamento confirmado que o botão usa hoje.
//
// Enquanto o provedor não existe, o protótipo gera a MESMA FORMA do dado real:
// BR Code no padrão EMV do Banco Central, com comprimento por campo e CRC16
// calculado de verdade. A chave e o domínio é que são fictícios.

const CHAVE_PIX = 'contato@casadababa.com.br'
const NOME_RECEBEDOR = 'CASA DA BABA'
const CIDADE = 'SAO PAULO'
const DOMINIO_COBRANCA = 'https://pix.casadababa.com.br/c/'

// Campo EMV: identificador de 2 dígitos, tamanho de 2 dígitos, valor.
const campo = (id, valor) => id + String(valor.length).padStart(2, '0') + valor

// CRC16-CCITT (polinômio 0x1021, semente 0xFFFF), o mesmo do BR Code.
function crc16(texto) {
  let crc = 0xFFFF
  for (let i = 0; i < texto.length; i += 1) {
    crc ^= texto.charCodeAt(i) << 8
    for (let bit = 0; bit < 8; bit += 1) {
      crc = (crc & 0x8000) === 0 ? (crc << 1) & 0xFFFF : ((crc << 1) ^ 0x1021) & 0xFFFF
    }
  }
  return crc.toString(16).toUpperCase().padStart(4, '0')
}

// Sufixo curto para duas cobranças do mesmo pedido não nascerem com o mesmo
// txid. No provedor real quem garante unicidade é ele.
let sequencia = 0
const sufixo = () => {
  sequencia += 1
  return String(sequencia).padStart(2, '0') + Math.random().toString(36).slice(2, 5).toUpperCase()
}

const somenteAlfanumerico = (texto) => texto.replace(/[^A-Za-z0-9]/g, '')

export function emitirCobrancaPix({ numeroPedido, valor }) {
  const identificador = ('CDB' + somenteAlfanumerico(String(numeroPedido)) + sufixo()).slice(0, 25)
  const valorFormatado = valor.toFixed(2)

  const semCrc = campo('00', '01')
    + campo('26', campo('00', 'br.gov.bcb.pix') + campo('01', CHAVE_PIX))
    + campo('52', '0000')
    + campo('53', '986')
    + campo('54', valorFormatado)
    + campo('58', 'BR')
    + campo('59', NOME_RECEBEDOR)
    + campo('60', CIDADE)
    + campo('62', campo('05', identificador))
    + '6304'

  return {
    identificador,
    copiaECola: semCrc + crc16(semCrc),
    link: DOMINIO_COBRANCA + identificador.toLowerCase(),
  }
}
