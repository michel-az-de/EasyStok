// #3344: a leitura do cliente que abre a Ficha, com as mesmas palavras da lista de Clientes (#3254): estágio em 4
// passos, "em risco", há quantos dias pediu e os sinais. Tudo vem pronto da API (`relacao`, a linha da lista); sem a
// linha (ficha aberta pelo Atendimento ou pela Buscar na Casa) não há leitura, nunca um estágio calculado aqui.
import { estagioDoCliente, posicaoDoEstagio, diasDesde } from './relacionamento'

export const textoDosDias = (d) => (d === 0 ? 'hoje' : d === 1 ? 'há 1 dia' : `há ${d} dias`)

export function leituraDoCliente(relacao, agora = Date.now()) {
  const estagio = estagioDoCliente(relacao?.estagio)
  if (!estagio) return null
  const dias = diasDesde(relacao.ultimaCompraEm, agora)
  const abertas = Number(relacao.ocorrenciasAbertas ?? 0)
  const resolvidas = Math.max(0, Number(relacao.ocorrenciasTotal ?? 0) - abertas)
  return {
    estagio,
    posicao: posicaoDoEstagio(relacao.estagio),
    emRisco: relacao.emRisco === true,
    ultimaCompra: dias !== null ? `Pediu ${textoDosDias(dias)}` : Number(relacao.compras) === 0 ? 'Ainda não pediu' : null,
    reclamacaoAberta: abertas > 0 ? (abertas === 1 ? '1 reclamação aberta' : `${abertas} reclamações abertas`) : null,
    reclamacaoResolvida: abertas === 0 && resolvidas > 0 ? (resolvidas === 1 ? '1 reclamação resolvida' : `${resolvidas} reclamações resolvidas`) : null,
  }
}

// O próximo passo que a leitura pede, em uma frase para quem atende. Só afirma o que a API já disse.
export function proximoPassoDoCliente(leitura) {
  if (!leitura) return null
  if (leitura.reclamacaoAberta) return 'Resolva a reclamação antes de oferecer algo.'
  if (leitura.emRisco) return 'Está há tempo sem pedir: vale chamar com algo que ela gosta.'
  if (leitura.posicao === 0) return 'Ainda não pediu: apresente o cardápio.'
  if (leitura.posicao === 1) return 'Primeira compra feita: convide para repetir.'
  return null
}
