// #2258 (auditoria de UX, T8): iniciais do avatar, a da primeira e a da última palavra ("Visitante
// do site" é VS). Só palavra que começa com letra conta; telefone no lugar do nome ("553199999999")
// não tem letra e devolve vazio, e quem desenha cai no ícone de pessoa em vez de um "?". Palavra
// curta ("Jo", "Ed", "B") também vira inicial.

const PARTICULAS = new Set(['da', 'de', 'do', 'das', 'dos', 'e'])

export function iniciaisDoNome(nome) {
  const palavras = String(nome ?? '').split(/\s+/).filter((palavra) => /^\p{L}/u.test(palavra))
  const principais = palavras.filter((palavra, i) => i === 0 || !PARTICULAS.has(palavra.toLowerCase()))
  const extremos = principais.length > 1 ? [principais[0], principais.at(-1)] : principais
  return extremos.map((palavra) => palavra[0].toUpperCase()).join('')
}
