// Fachada de emissão de cobrança, por MEIO. Nenhum componente (nem o
// dominio) conhece Mercado Pago: só chama `emitirCobranca(meio, dados)` e
// recebe de volta a MESMA FORMA sempre, `{ identificador, copiaECola, link }`
// (rodada 5, seção 4, "Onde o estudo do arquiteto encaixa" da decisão 19,
// e estudo 2 da decisão 18).
//
// ---------------------------------------------------------------------------
// AQUI ENTRA A API REAL DO MERCADO PAGO.
//
// Pix ('pix')          -> API de Orders/Payments do Mercado Pago (Pix
//                          dinâmico), QR em base64 + copia-e-cola + ticket_url.
//                          Hoje: `infra/pix.js`, BR Code EMV local.
// Cartão ('cartao-link') -> Checkout Pro (link de pagamento), aceita cartão de
//                          crédito, débito e Pix dentro do link. Hoje: link
//                          local com o mesmo formato do BR Code do Pix, sem
//                          copia-e-cola (cartão não tem).
// As duas validades reais do provedor vão de 30 minutos a 30 dias (achado
// [F51] da decisão 18); a casa promete 30 minutos, exatamente o piso do
// Mercado Pago, então o prazo de `dominio/cobranca.js` (MINUTOS_PARA_EXPIRAR)
// não muda quando o provedor entrar.
//
// Maquininha ('maquininha') e Vale ('vale-refeicao') NÃO passam por aqui como
// link: o Mercado Pago só aceita VR e VA na maquininha (Point), nunca online
// (decisão 18, seção 3.2 — "Checkout Pro lista cartão de crédito ou débito,
// Pix, boleto... ", sem VR/VA). Emitir "cobrança" para esses dois meios é só
// registrar que ela vai ser cobrada na entrega: sem identificador de
// provedor, sem link, sem prazo. A baixa entra por `MARCAR_RECEBIDO_ENTREGA`
// (aplicacao/casos/cobranca.js), à mão, como a maquininha já fazia.
// ---------------------------------------------------------------------------

import { emitirCobrancaPix } from './pix'
import { MEIOS_DE_PAGAMENTO } from './catalogo'

const somenteAlfanumerico = (texto) => String(texto ?? '').replace(/[^A-Za-z0-9]/g, '')

let sequenciaLink = 0

// Link de cartão do protótipo: mesma ideia do BR Code fictício do Pix (mesma
// forma do dado real, chave e domínio fictícios). Quando o Checkout Pro
// entrar, esta função vira a chamada HTTP que devolve o `init_point` da
// preferência.
function emitirLinkCartao({ numeroPedido }) {
  sequenciaLink += 1
  const identificador = ('CDBCARTAO' + somenteAlfanumerico(numeroPedido) + String(sequenciaLink).padStart(2, '0'))
    .slice(0, 25)
  return {
    identificador,
    copiaECola: null,
    link: 'https://link.mercadopago.com.br/casadababa/' + identificador.toLowerCase(),
  }
}

// Maquininha e vale: sem link. O identificador só serve para a cobrança ter
// um `id` (o mesmo campo que o Pix usa), nunca aparece para o cliente.
function registrarCobrancaNaEntrega({ numeroPedido }) {
  sequenciaLink += 1
  return {
    identificador: ('CDBENT' + somenteAlfanumerico(numeroPedido) + String(sequenciaLink).padStart(2, '0')).slice(0, 25),
    copiaECola: null,
    link: null,
  }
}

export function emitirCobranca(meioId, { numeroPedido, valor }) {
  const meio = MEIOS_DE_PAGAMENTO.find((m) => m.id === meioId)
  if (meio?.id === 'pix') return emitirCobrancaPix({ numeroPedido, valor })
  if (meio?.id === 'cartao-link') return emitirLinkCartao({ numeroPedido })
  return registrarCobrancaNaEntrega({ numeroPedido })
}
