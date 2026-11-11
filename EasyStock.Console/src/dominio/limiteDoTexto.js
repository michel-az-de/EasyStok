// #2285: o texto de uma mensagem do Atendimento tem o teto da API (`Mensagem.TextoTamanhoMaximo`, 4096; o
// envio recusa acima disso, depois de aparar as pontas). O campo não usa `maxLength`, que cortaria em
// silêncio o texto colado: avisa quando falta pouco e desliga o Enviar acima do teto. A prova 1587 confere
// o número contra o domínio da API.
export const LIMITE_TEXTO_MENSAGEM = 4096
// Daqui para cima o campo mostra quantos caracteres ainda cabem.
const COMECA_O_AVISO = Math.floor(LIMITE_TEXTO_MENSAGEM * 0.9)

const plural = (n) => (n === 1 ? 'caractere' : 'caracteres')

// null quando o texto está longe do teto; senão `{ acima, texto }` com a frase para a tela.
export function avisoDeLimiteDoTexto(texto) {
  const tamanho = (texto ?? '').trim().length
  if (tamanho > LIMITE_TEXTO_MENSAGEM) {
    const passam = tamanho - LIMITE_TEXTO_MENSAGEM
    return { acima: true, texto: `A mensagem passa ${passam} ${plural(passam)} do limite de ${LIMITE_TEXTO_MENSAGEM}. Encurte ou divida em duas para enviar.` }
  }
  if (tamanho < COMECA_O_AVISO) return null
  const cabem = LIMITE_TEXTO_MENSAGEM - tamanho
  return { acima: false, texto: `Ainda cabem ${cabem} ${plural(cabem)} (limite de ${LIMITE_TEXTO_MENSAGEM}).` }
}
