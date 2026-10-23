// Abrir a loja é abrir o caixa (#1443, decisão do Felipe em 07/10): antes de abrir, a tela mostra
// como ficou o caixa de ontem, pede a conferência do saldo inicial e, se ela mudar o valor, o
// motivo da retificação. Fechar a loja dentro do horário pede justificativa (a API recusa sem).
// Puro: o caixa chega já traduzido (`infra/api/caixaApi.js`) e "agora" chega por parâmetro.

import { gavetaDoDia } from './caixa.js'
import { moeda } from './formato.js'
import { dentroDoHorario } from './funcionamento.js'

// Mesmo mínimo do DefinirControleExpedienteUseCase: a API recusa abaixo disso.
export const JUSTIFICATIVA_MINIMA = 10

export const justificativaValida = (texto) => (texto ?? '').trim().length >= JUSTIFICATIVA_MINIMA

// "2026-10-04" -> "04/10".
export const diaMes = (iso) => (iso ? `${iso.slice(8, 10)}/${iso.slice(5, 7)}` : '')

// O que a dona precisa saber antes de abrir:
//   esquecido    o caixa de um dia anterior ficou aberto; fecha (conferindo) antes de abrir o de hoje;
//   fechadoHoje  o caixa de hoje já fechou e não reabre (regra da API);
//   aberto       o caixa de hoje já está aberto; a loja abre direto;
//   novo         abre o caixa de hoje; o saldo sugerido é o que ficou na gaveta no último fechamento.
// #1474: `naGaveta` é o que ela confere (só dinheiro, `gavetaDoDia`); `saldoEsperado` é o total do
// dia da API, que soma Pix e cartão e por isso não serve para contar a gaveta.
export function situacaoDoCaixaParaAbrirLoja(dia, ultimoFechamento) {
  if (dia?.esquecidoAberto) {
    return {
      tipo: 'esquecido', desde: dia.abertoDesde, saldoEsperado: dia.saldoEsperado,
      naGaveta: gavetaDoDia(dia).naGaveta, ultimo: ultimoFechamento ?? null,
    }
  }
  if (dia?.fechado) return { tipo: 'fechadoHoje', fechamento: dia.fechamento }
  if (dia?.aberto) {
    return { tipo: 'aberto', saldoInicial: dia.saldoInicial, saldoEsperado: dia.saldoEsperado, naGaveta: gavetaDoDia(dia).naGaveta }
  }
  return { tipo: 'novo', ultimo: ultimoFechamento ?? null, saldoSugerido: gavetaDoFechamento(ultimoFechamento) }
}

// #1776-11: o que ficou na gaveta ao fechar é só dinheiro; `saldoFinal` soma Pix e cartão. Fechamento antigo
// (sem o campo) continua sugerindo o saldo final, como antes.
export const gavetaDoFechamento = (fechamento) => fechamento?.dinheiroNaGaveta ?? fechamento?.saldoFinal ?? 0

const centavos = (valor) => Math.round((Number(valor) || 0) * 100)

// Diferença entre o que ela conferiu na gaveta e o que o último fechamento deixou. Sem fechamento
// anterior não há contra o que conferir: qualquer valor vale sem motivo.
export function retificacaoDoSaldo(saldoInformado, situacao) {
  if (situacao?.tipo !== 'novo' || !situacao.ultimo) return { diferenca: 0, exigeMotivo: false }
  const diferenca = (centavos(saldoInformado) - centavos(gavetaDoFechamento(situacao.ultimo))) / 100
  return { diferenca, exigeMotivo: diferenca !== 0 }
}

// J2 (#1990), item 6: "100" digitado abriu o caixa com R$ 1,00 sem ninguém perceber. Valor muito longe
// do que se espera pede um segundo toque: com fechamento anterior, menos da metade ou mais do dobro do
// que ficou na gaveta (e ao menos R$ 20 de diferença, troco que mudou pouco não pergunta); na primeira
// abertura, valor com cara de centavo (abaixo de R$ 10) ou de zero a mais (R$ 2.000 ou mais).
const DIFERENCA_MINIMA_PARA_CONFIRMAR = 20
const PRIMEIRA_ABERTURA_MINIMA = 10
const PRIMEIRA_ABERTURA_MAXIMA = 2000
export function aberturaPedeConfirmacao(saldoInformado, situacao) {
  if (situacao?.tipo !== 'novo') return false
  const saldo = Number(saldoInformado) || 0
  if (!situacao.ultimo) return (saldo > 0 && saldo < PRIMEIRA_ABERTURA_MINIMA) || saldo >= PRIMEIRA_ABERTURA_MAXIMA
  const sugerido = gavetaDoFechamento(situacao.ultimo)
  if (Math.abs(saldo - sugerido) < DIFERENCA_MINIMA_PARA_CONFIRMAR) return false
  return saldo < sugerido / 2 || saldo > sugerido * 2
}

export function textoDaConfirmacaoDaAbertura(saldoInformado, situacao) {
  const pergunta = `Abrir o caixa com ${moeda(Number(saldoInformado) || 0)}?`
  const ultimo = situacao?.ultimo
  return ultimo
    ? `${pergunta} O fechamento de ${diaMes(ultimo.data)} deixou ${moeda(gavetaDoFechamento(ultimo))} na gaveta.`
    : `${pergunta} Confira: o valor é em reais (100 é R$ 100,00).`
}

const comSinal = (valor) => (valor > 0 ? `+${moeda(valor)}` : moeda(valor))

// Vai para `Observacoes` do movimento de abertura: o rastro de contra o que ela conferiu.
export function observacaoDaAbertura({ saldoInformado, situacao, motivo }) {
  const ultimo = situacao?.ultimo
  if (!ultimo) return `Primeira abertura conferida: ${moeda(Number(saldoInformado) || 0)}.`
  const base = `Conferido contra o fechamento de ${diaMes(ultimo.data)} (${moeda(gavetaDoFechamento(ultimo))}).`
  const { diferenca } = retificacaoDoSaldo(saldoInformado, situacao)
  if (diferenca === 0) return base
  return `${base} Retificado em ${comSinal(diferenca)}: ${(motivo ?? '').trim()}`
}

// A API não guarda o contado do fechamento; vai nas observações para a conferência ficar registrada.
export function observacaoDoFechamento({ contado, saldoEsperado, nota }) {
  const diferenca = (centavos(contado) - centavos(saldoEsperado)) / 100
  const texto = `Contado na gaveta: ${moeda(Number(contado) || 0)}. Diferença: ${diferenca === 0 ? 'bateu certo' : comSinal(diferenca)}.`
  const extra = (nota ?? '').trim()
  return extra ? `${texto} ${extra}` : texto
}

// Fechar na mão dentro do horário de funcionamento pede gerente e justificativa; fora dele, não.
export const fecharLojaPedeJustificativa = (agora, funcionamento) => dentroDoHorario(agora, funcionamento)
