// #2792: variáveis de um modelo de e-mail/SMS. O servidor renderiza com Scriban, então a grafia é {{ nome }}
// (não as {nome} do chat). O catálogo vem de GET /api/notificacoes/variaveis-catalogo, um item por variável
// do tipo de evento: { nomeVariavel, tipo, descricao, exemplo }.
export const variaveisDoModelo = (catalogo) => (Array.isArray(catalogo) ? catalogo : [])
  .filter((v) => typeof v?.nomeVariavel === 'string' && v.nomeVariavel.trim())
  .map((v) => ({ nome: v.nomeVariavel.trim(), descricao: String(v.descricao ?? '').trim() }))

export const dicaDasVariaveis = (catalogo) => {
  const lista = variaveisDoModelo(catalogo)
  if (lista.length === 0) return undefined
  return `Você pode usar: ${lista.map((v) => `{{ ${v.nome} }}${v.descricao ? ` ${v.descricao}` : ''}`).join(' · ')}`
}
