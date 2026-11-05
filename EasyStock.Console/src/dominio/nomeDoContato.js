// #2232 (banca UX, L10): contato salvo sem nome chega com o telefone cru no lugar do nome
// ("555434619500"). Na tela ele aparece com a máscara do Brasil, "+55 (54) 3461-9500". Nome de
// verdade passa intacto.

import { mascaraTelefone } from './formato'

export function nomeDoContato(nome) {
  const texto = String(nome ?? '').trim()
  if (!/^\+?[\d\s().-]+$/.test(texto)) return texto
  const digitos = texto.replace(/\D/g, '')
  if (/^55\d{10,11}$/.test(digitos)) return `+55 ${mascaraTelefone(digitos.slice(2))}`
  // Com "+" o país vem escrito: fora do 55, o número estrangeiro fica como veio.
  if (texto.startsWith('+')) return texto
  if (/^\d{10,11}$/.test(digitos)) return mascaraTelefone(digitos)
  return texto
}
