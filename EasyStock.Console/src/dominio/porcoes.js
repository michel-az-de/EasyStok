// Porções do prato (M1.4a, #1529). O preço da porção é absoluto, não um acréscimo (ADR-0035), e
// cada porção de prato ligado ao estoque tem saldo próprio (D-M1-03). Regras puras do editor: o
// que a tela mostra, valida e manda. O rascunho guarda o preço como texto, do jeito que ela digita.
import { lerMoeda } from './formato'

let serie = 0
const chaveNova = () => `nova-${serie += 1}`

// Mesma leitura do campo Preço: "34.90" e "34,90" são trinta e quatro e noventa. Tirar todo ponto
// antes da vírgula fazia "34.90" virar R$ 3.490,00 no cardápio.
export const precoDoTexto = (texto) => (String(texto ?? '').trim() === '' ? NaN : lerMoeda(texto) / 100)
const textoDoPreco = (preco) => (preco == null ? '' : Number(preco).toFixed(2).replace('.', ','))

export const porcaoNova = () => ({ chave: chaveNova(), id: null, rotulo: '', peso: '', preco: '', disponivel: true, padrao: false, sku: null })

// M1.4e (#1552): `embalagemId` só existe na linha quando o EasyStok mandou ou quando ela escolheu.
// Sem a chave, a embalagem não vai no corpo e o EasyStok não mexe nela.
export const rascunhoDasPorcoes = (porcoes) => (porcoes ?? []).map((p) => ({
  chave: p.id ?? chaveNova(), id: p.id ?? null, rotulo: p.rotulo ?? '', peso: p.peso ?? '', preco: textoDoPreco(p.preco),
  disponivel: p.disponivel !== false, padrao: p.padrao === true, sku: p.sku ?? null,
  ...(p.embalagemId !== undefined ? { embalagemId: p.embalagemId ?? null } : {}),
}))

export function erroDasPorcoes(linhas) {
  if (linhas.some((l) => !l.rotulo.trim())) return 'Dê um nome a cada porção (ex.: 300 g).'
  if (linhas.some((l) => !(precoDoTexto(l.preco) > 0))) return 'Cada porção precisa de preço.'
  const nomes = linhas.map((l) => l.rotulo.trim().toLowerCase())
  if (new Set(nomes).size !== nomes.length) return 'Duas porções com o mesmo nome.'
  return null
}

// Só uma padrão: marcar uma desmarca as outras; a primeira vale quando nenhuma está marcada.
export const comPadrao = (linhas, chave) => linhas.map((l) => ({ ...l, padrao: l.chave === chave }))

export function corpoDasPorcoes(linhas) {
  const temPadrao = linhas.some((l) => l.padrao)
  return linhas.map((l, i) => ({
    id: l.id, rotulo: l.rotulo.trim(), preco: precoDoTexto(l.preco), peso: l.peso.trim() || null,
    disponivel: l.disponivel, padrao: temPadrao ? l.padrao : i === 0, sku: l.sku ?? null,
    ...(l.embalagemId !== undefined ? { embalagemId: l.embalagemId || null, mexerEmbalagem: true } : {}),
  }))
}

// "300 g", "800g", "1,2 kg" e "1.200 g" viram gramas inteiros; texto sem peso ("Individual") vira null.
// O peso para mostrar vale antes do nome da porção.
const PESO = /(\d+(?:[.,]\d+)?)\s*(kg|quilos?|gramas?|gr|g)(?![a-zà-ú])/i
export function gramasDaPorcao(porcao) {
  for (const texto of [porcao?.peso, porcao?.rotulo]) {
    const achado = PESO.exec(String(texto ?? ''))
    if (!achado) continue
    const [, numero, unidade] = achado
    const emQuilos = /^(k|q)/i.test(unidade)
    const valor = !emQuilos && /^\d{1,3}[.,]\d{3}$/.test(numero) ? Number(numero.replace(/[.,]/, '')) : Number(numero.replace(',', '.'))
    const gramas = Math.round(valor * (emQuilos ? 1000 : 1))
    if (gramas > 0) return gramas
  }
  return null
}

// M1.4e (#1552): escolher a porção na produção já traz o peso dela, que é o que faz a receita em peso
// baixar o insumo certo. O que ela digitou fica; só troca o peso que veio da porção anterior.
export function pesoAoEscolherPorcao(pesoAtual, porcaoAnterior, porcaoEscolhida) {
  const digitado = String(pesoAtual ?? '').trim()
  const anterior = gramasDaPorcao(porcaoAnterior)
  if (digitado !== '' && digitado !== String(anterior ?? '')) return digitado
  const novo = gramasDaPorcao(porcaoEscolhida)
  return novo == null ? '' : String(novo)
}

// Compara o que vai com o que veio: sem mudança, o formulário não manda as porções (null = não mexe).
export const porcoesMudaram = (iniciais, linhas) =>
  JSON.stringify(corpoDasPorcoes(rascunhoDasPorcoes(iniciais))) !== JSON.stringify(corpoDasPorcoes(linhas))

// Prato com porções: o preço e a porção do item acompanham a padrão (o "a partir de" é do EasyStok).
export function resumoDoItem(linhas) {
  const corpo = corpoDasPorcoes(linhas)
  const padrao = corpo.find((p) => p.padrao) ?? corpo[0]
  return padrao ? { preco: padrao.preco, porcao: padrao.peso ?? padrao.rotulo } : null
}
