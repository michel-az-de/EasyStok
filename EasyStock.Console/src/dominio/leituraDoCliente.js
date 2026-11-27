// #3344: a leitura do cliente que abre a Ficha, com as mesmas palavras da lista de Clientes (#3254): estágio em 4
// passos, "em risco", há quantos dias pediu e os sinais. Tudo vem pronto da API (`relacao`, a linha da lista); sem a
// linha (ficha aberta pelo Atendimento ou pela Buscar na Casa) não há leitura, nunca um estágio calculado aqui.
import { estagioDoCliente, posicaoDoEstagio, diasDesde } from './relacionamento'

// #3387: os nomes dos 4 passos, na ordem, para a régua da ficha mostrar onde a pessoa está e o que vem depois.
export const ESTAGIOS_DA_LEITURA = Object.freeze(['Sem pedido', 'Primeira compra', 'Recorrente', 'Fiel'])
// #3387: 30 dias sem pedir é o mesmo marco do "Em risco" da API (RegraDeRecompra). Aqui não vira rótulo nem sinal
// novo (o sinal é da API, spec 3237): só destaca o fato "pediu há N dias" para quem olha.
export const DIAS_PARADA = 30

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
    dias,
    parada: dias !== null && dias >= DIAS_PARADA && Number(relacao.compras) > 0,
    ultimaCompra: dias !== null ? `Pediu ${textoDosDias(dias)}` : Number(relacao.compras) === 0 ? 'Ainda não pediu' : null,
    reclamacaoAberta: abertas > 0 ? (abertas === 1 ? '1 reclamação aberta' : `${abertas} reclamações abertas`) : null,
    reclamacaoResolvida: abertas === 0 && resolvidas > 0 ? (resolvidas === 1 ? '1 reclamação resolvida' : `${resolvidas} reclamações resolvidas`) : null,
  }
}

// O próximo passo que a leitura pede, em uma frase para quem atende. Só afirma o que a API já disse.
export function proximoPassoDoCliente(leitura) {
  if (!leitura) return null
  if (leitura.reclamacaoAberta) return 'Resolva a reclamação antes de oferecer algo.'
  if (leitura.emRisco) return 'Está há tempo sem pedir: vale chamar com um prato de que a pessoa gosta.'
  if (leitura.posicao === 0) return 'Ainda não pediu: apresente o cardápio.'
  if (leitura.posicao === 1) return 'Primeira compra feita: convide para repetir.'
  // #3387: quem está em dia também tem um passo; a ficha sempre diz o que fazer agora.
  if (leitura.posicao === 2) return 'Pede com frequência: ofereça o que a pessoa costuma pedir.'
  if (leitura.posicao === 3) return 'Cliente fiel: agradeça a preferência e apresente uma novidade.'
  return null
}

// #3387: o ritmo de compra é da API (mediana dos intervalos entre as últimas compras, mínimo 3 compras). A ficha só
// escreve o que recebeu; amostra menor que 3 ou ritmo zero não vira frase.
export function ritmoDoCliente(ritmo) {
  const dias = Number(ritmo?.dias), compras = Number(ritmo?.compras)
  if (!Number.isInteger(dias) || dias < 1 || !Number.isInteger(compras) || compras < 3) return null
  return { texto: dias === 1 ? 'Pede todo dia' : `Pede a cada ~${dias} dias`, apoio: `mediana dos últimos ${compras} dias com compra` }
}
