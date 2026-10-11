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
