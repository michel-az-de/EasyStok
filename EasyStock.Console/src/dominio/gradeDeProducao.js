// Grade de toque da Produção do dia (#1624). A dona produz como no PWA: toca no prato para somar
// uma porção, vê o saldo no cartão e confere o lote antes de confirmar. Aqui ficam as contas da
// grade; a linha do lote continua a mesma que a API recebe (dominio/producao.js, dominio/porcoes.js).
import { linhaComPorcao, linhaComPrato, pesoDeReferenciaDoPrato } from './porcoes.js'

export const VALIDADES_RAPIDAS = [3, 5, 7, 10, 15]

const chaveDoCartao = (sku, variacaoId = '') => `${sku}|${variacaoId}`

// Um cartão por coisa que se produz: o prato de tamanho único ou cada porção do prato. `saldo` nulo
// é prato que ainda não tem estoque ligado (a primeira produção liga): a tela não mostra número.
export function cartoesDaGrade(pratos, estoque = []) {
  const saldos = new Map((estoque ?? []).map((p) => [p.sku, p]))
  return (pratos ?? []).flatMap((prato) => {
    const noEstoque = saldos.get(prato.sku) ?? null
    const base = { sku: prato.sku, nome: prato.nome, categoria: prato.categoria || null }
    if ((prato.porcoes ?? []).length === 0) {
      return [{ ...base, chave: chaveDoCartao(prato.sku), variacaoId: '', tamanho: prato.porcao || '', saldo: noEstoque?.saldo ?? null }]
    }
    return prato.porcoes.map((porcao) => ({
      ...base, chave: chaveDoCartao(prato.sku, porcao.id), variacaoId: porcao.id, tamanho: porcao.rotulo,
      saldo: noEstoque ? noEstoque.porcoes?.find((s) => s.variacaoId === porcao.id)?.saldo ?? 0 : null,
    }))
  })
}

const semAcento = (valor) => String(valor ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('pt-BR')

// O cardápio da casa já escreve o tamanho no nome ("Talharim 500 g", porção "500 g"): repetir no
// cartão só ocupa espaço. O tamanho some só quando o nome termina com ele, palavra por palavra:
// "Panqueca de Frango" contém "p" e "g", e as porções P e G têm de continuar escritas.
export function tamanhoNoCartao(cartao) {
  const tamanho = semAcento(cartao.tamanho).trim()
  return tamanho && !` ${semAcento(cartao.nome).trim()}`.endsWith(` ${tamanho}`) ? cartao.tamanho : ''
}

// Como a dona chama o cartão: "Talharim 500 g".
export const nomeDoCartao = (cartao) => [cartao.nome, tamanhoNoCartao(cartao)].filter(Boolean).join(' ')

const ehDoCartao = (linha, cartao) => linha.sku === cartao.sku && (linha.variacaoId || '') === cartao.variacaoId
const porcoesDaLinha = (linha) => {
  const n = Number(String(linha.porcoes ?? '').trim())
  return Number.isInteger(n) && n > 0 ? n : 0
}

export const porcoesNoLote = (linhas, cartao) =>
  linhas.filter((l) => ehDoCartao(l, cartao)).reduce((soma, l) => soma + porcoesDaLinha(l), 0)

// Quantas porções do cartão vão no lote: cria a linha, troca o número ou tira a linha (zero).
// A linha nova já traz o peso que o cardápio diz; peso errado é pior que campo vazio, então só
// entra o que `dominio/porcoes.js` consegue ler.
export function definirNoLote(linhas, cartao, quantidade, { pratos, novaLinha }) {
  const alvo = Math.max(0, Math.trunc(quantidade))
  if (alvo === 0) return linhas.filter((l) => !ehDoCartao(l, cartao))
  // Um cartão, uma linha: se o mesmo prato entrou duas vezes (do Planejamento e do toque), a
  // primeira linha fica com o total e as outras saem. Sem isso o "−" somava em vez de tirar.
  const primeira = linhas.find((l) => ehDoCartao(l, cartao))
  if (primeira) {
    return linhas.flatMap((l) => (l === primeira ? [{ ...l, porcoes: String(alvo) }] : ehDoCartao(l, cartao) ? [] : [l]))
  }
  const comPrato = linhaComPrato(novaLinha(), pratos, cartao.sku)
  const peso = pesoDeReferenciaDoPrato(pratos.find((p) => p.sku === cartao.sku))
  const linha = cartao.variacaoId
    ? linhaComPorcao(comPrato, pratos, cartao.variacaoId)
    : { ...comPrato, pesoPorPorcaoG: peso == null ? '' : String(peso) }
  return [...linhas, { ...linha, porcoes: String(alvo) }]
}

export const somarNoLote = (linhas, cartao, delta, apoio) =>
  definirNoLote(linhas, cartao, porcoesNoLote(linhas, cartao) + delta, apoio)

export function resumoDoLote(linhas) {
  const comPrato = linhas.filter((l) => l.sku)
  return { pratos: comPrato.length, porcoes: comPrato.reduce((soma, l) => soma + porcoesDaLinha(l), 0) }
}

export const categoriasDaGrade = (cartoes) => [...new Set(cartoes.map((c) => c.categoria).filter(Boolean))]

export function filtrarGrade(cartoes, { categoria = null, busca = '' } = {}) {
  const termos = semAcento(busca).split(/\s+/).filter(Boolean)
  return cartoes.filter((c) => (!categoria || c.categoria === categoria)
    && termos.every((termo) => semAcento(nomeDoCartao(c)).includes(termo)))
}

// "Falta produzir": a sugestão do planejamento (mínimo + pedidos agendados + vendido sem saldo −
// estoque), por prato. Prato de um tamanho só vai direto para o lote (`cartao`); prato com porções
// não tem como adivinhar o tamanho, então a tela mostra os tamanhos dele para ela escolher.
export function faltaProduzir(sugestoes, cartoes) {
  return (sugestoes ?? []).filter((s) => s.sugestao > 0).flatMap((s) => {
    const doPrato = cartoes.filter((c) => c.sku === s.sku)
    if (doPrato.length === 0) return []
    // O nome é o do cardápio (o da sugestão vem do estoque, em minúsculas).
    return [{ sku: s.sku, nome: doPrato[0].nome, quantidade: s.sugestao, agendados: s.agendados ?? 0, cartao: doPrato.length === 1 ? doPrato[0] : null }]
  })
}
