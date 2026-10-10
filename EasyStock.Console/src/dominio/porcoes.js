// Porções do prato (M1.4a, #1529). O preço da porção é absoluto, não um acréscimo (ADR-0035), e
// cada porção de prato ligado ao estoque tem saldo próprio (D-M1-03). Regras puras do editor: o
// que a tela mostra, valida e manda. O rascunho guarda o preço como texto, do jeito que ela digita.
let serie = 0
const chaveNova = () => `nova-${serie += 1}`

export const precoDoTexto = (texto) => {
  const limpo = String(texto ?? '').trim().replace(/\./g, '').replace(',', '.')
  return limpo === '' ? NaN : Number(limpo)
}
const textoDoPreco = (preco) => (preco == null ? '' : Number(preco).toFixed(2).replace('.', ','))

export const porcaoNova = () => ({ chave: chaveNova(), id: null, rotulo: '', peso: '', preco: '', disponivel: true, padrao: false, sku: null })

export const rascunhoDasPorcoes = (porcoes) => (porcoes ?? []).map((p) => ({
  chave: p.id ?? chaveNova(), id: p.id ?? null, rotulo: p.rotulo ?? '', peso: p.peso ?? '', preco: textoDoPreco(p.preco),
  disponivel: p.disponivel !== false, padrao: p.padrao === true, sku: p.sku ?? null,
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
  }))
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
