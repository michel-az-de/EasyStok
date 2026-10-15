// Grade de toque da Produção do dia (#1624). A dona produz como no PWA: toca no prato para somar
// uma porção, vê o saldo no cartão e confere o lote antes de confirmar. Aqui ficam as contas da
// grade; a linha do lote continua a mesma que a API recebe (dominio/producao.js, dominio/porcoes.js).
import { categoriasComContagem, daCategoria } from './categorias.js'
import { linhaComPorcao, linhaComPrato, pesoDeReferenciaDoPrato } from './porcoes.js'

export const VALIDADES_RAPIDAS = [3, 5, 7, 10, 15]

const chaveDoCartao = (sku, variacaoId = '') => `${sku}|${variacaoId}`

// Um cartão por coisa que se produz: o prato de tamanho único ou cada porção do prato. `saldo` nulo
// é prato que ainda não tem estoque ligado (a primeira produção liga): a tela não mostra número.
// #1809: a foto é a mesma do cardápio, para ela achar o prato pelo olho.
export function cartoesDaGrade(pratos, estoque = []) {
  const saldos = new Map((estoque ?? []).map((p) => [p.sku, p]))
  return (pratos ?? []).flatMap((prato) => {
    const noEstoque = saldos.get(prato.sku) ?? null
    const base = { sku: prato.sku, nome: prato.nome, categoria: prato.categoria || null, foto: prato.foto || null }
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

// Campo de quantidade (porções, dias de validade): só dígitos, até 4. O toque no cartão soma em
// cima do que está no campo; texto como "12a" contava zero e o toque seguinte trocava por 1.
export const soDigitos = (texto, maximo = 4) => String(texto ?? '').replace(/\D/g, '').slice(0, maximo)

// O lote segue a ordem do cardápio, não a dos toques: ela acha o prato onde ele está na grade, e
// o corpo enviado é o mesmo para o mesmo lote. Com a ordem dos toques, refazer o lote depois de
// uma resposta perdida mudava o corpo, a chave de idempotência e gravava em dobro.
export function ordenarPeloCardapio(linhas, pratos) {
  const posicao = (linha) => {
    const indice = pratos.findIndex((p) => p.sku === linha.sku)
    if (indice < 0) return [pratos.length, 0]
    return [indice, (pratos[indice].porcoes ?? []).findIndex((v) => v.id === linha.variacaoId) + 1]
  }
  return linhas
    .map((linha, ordem) => ({ linha, ordem, chave: posicao(linha) }))
    .sort((a, b) => a.chave[0] - b.chave[0] || a.chave[1] - b.chave[1] || a.ordem - b.ordem)
    .map((item) => item.linha)
}

// As linhas com que a tela abre: o rascunho como ela deixou e, vindo do Planejamento, a linha só
// com prato e porções. Essa ganha o resto da linha nova e o peso que o cardápio diz, igual à linha
// criada pelo toque. `pesoPorPorcaoG` presente é sinal de linha já digitada: não se mexe.
export function linhasIniciais(guardadas, { pratos, novaLinha }) {
  const linhas = (guardadas ?? []).map((guardada) => {
    if (guardada.pesoPorPorcaoG !== undefined) return { ...novaLinha(), ...guardada }
    const peso = (pratos.find((p) => p.sku === guardada.sku)?.porcoes ?? []).length === 0
      ? pesoDeReferenciaDoPrato(pratos.find((p) => p.sku === guardada.sku))
      : null
    return { ...novaLinha(), ...guardada, pesoPorPorcaoG: peso == null ? '' : String(peso) }
  })
  return ordenarPeloCardapio(linhas, pratos)
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
  return ordenarPeloCardapio([...linhas, { ...linha, porcoes: String(alvo) }], pratos)
}

// Linha do Planejamento de prato com porções chega sem a porção (o plano é por prato). O primeiro
// toque num tamanho diz de qual porção são as planejadas: a linha vira daquela porção, com as
// porções que já tinha, em vez de nascer outra ao lado.
export function somarNoLote(linhas, cartao, delta, apoio) {
  const semPorcao = delta > 0 && cartao.variacaoId && !linhas.some((l) => ehDoCartao(l, cartao))
    ? linhas.find((l) => l.sku === cartao.sku && !l.variacaoId)
    : null
  if (semPorcao) {
    return ordenarPeloCardapio(linhas.map((l) => (l === semPorcao ? linhaComPorcao(l, apoio.pratos, cartao.variacaoId) : l)), apoio.pratos)
  }
  return definirNoLote(linhas, cartao, porcoesNoLote(linhas, cartao) + delta, apoio)
}

export function resumoDoLote(linhas) {
  const comPrato = linhas.filter((l) => l.sku)
  return { pratos: comPrato.length, porcoes: comPrato.reduce((soma, l) => soma + porcoesDaLinha(l), 0) }
}

export const categoriasDaGrade = (cartoes) => categoriasComContagem(cartoes).map((c) => c.nome)

export function filtrarGrade(cartoes, { categoria = null, busca = '' } = {}) {
  const termos = semAcento(busca).split(/\s+/).filter(Boolean)
  return cartoes.filter((c) => daCategoria(c, categoria)
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
