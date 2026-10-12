// Igualdade por conteúdo para dado que veio de JSON (objeto simples, lista e primitivo). Qualquer outra coisa
// (Date, Map, Set, classe) só é igual a si mesma: na dúvida o chamador trata como mudança.
const simples = (v) => Object.getPrototypeOf(v) === Object.prototype

export function mesmoConteudo(a, b) {
  if (Object.is(a, b)) return true
  if (typeof a !== 'object' || typeof b !== 'object' || a === null || b === null) return false
  if (Array.isArray(a)) {
    return Array.isArray(b) && a.length === b.length && a.every((item, i) => mesmoConteudo(item, b[i]))
  }
  if (Array.isArray(b) || !simples(a) || !simples(b)) return false
  const chaves = Object.keys(a)
  return chaves.length === Object.keys(b).length
    && chaves.every((chave) => Object.hasOwn(b, chave) && mesmoConteudo(a[chave], b[chave]))
}

// Comparação de props para `memo` (#1577): cada prop é a mesma ou, nas chaves de `porConteudo`, tem o mesmo
// conteúdo. Serve ao dado que a sincronização relê como objeto novo sem ter mudado (mensagem da conversa).
export function mesmasProps(antes, depois, porConteudo = []) {
  const chaves = Object.keys(antes)
  return chaves.length === Object.keys(depois).length && chaves.every((chave) => Object.hasOwn(depois, chave)
    && (Object.is(antes[chave], depois[chave]) || (porConteudo.includes(chave) && mesmoConteudo(antes[chave], depois[chave]))))
}
