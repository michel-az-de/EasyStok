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
  return { tipo: 'novo', ultimo: ultimoFechamento ?? null, saldoSugerido: ultimoFechamento?.saldoFinal ?? 0 }
}

const centavos = (valor) => Math.round((Number(valor) || 0) * 100)

// Diferença entre o que ela conferiu na gaveta e o que o último fechamento deixou. Sem fechamento
// anterior não há contra o que conferir: qualquer valor vale sem motivo.
export function retificacaoDoSaldo(saldoInformado, situacao) {
  if (situacao?.tipo !== 'novo' || !situacao.ultimo) return { diferenca: 0, exigeMotivo: false }
  const diferenca = (centavos(saldoInformado) - centavos(situacao.ultimo.saldoFinal)) / 100
  return { diferenca, exigeMotivo: diferenca !== 0 }
}

const comSinal = (valor) => (valor > 0 ? `+${moeda(valor)}` : moeda(valor))

// Vai para `Observacoes` do movimento de abertura: o rastro de contra o que ela conferiu.
export function observacaoDaAbertura({ saldoInformado, situacao, motivo }) {
  const ultimo = situacao?.ultimo
  if (!ultimo) return `Primeira abertura conferida: ${moeda(Number(saldoInformado) || 0)}.`
  const base = `Conferido contra o fechamento de ${diaMes(ultimo.data)} (${moeda(ultimo.saldoFinal)}).`
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
