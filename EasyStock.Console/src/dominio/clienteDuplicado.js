// #2495 (S5, B6): aviso de cliente duplicado. A busca da API acha por "contém"; aqui só conta quem tem os
// mesmos dígitos. Telefone com 12 ou 13 dígitos começando em 55 perde o código do Brasil para comparar.
const MINIMO = { telefone: 8, documento: 11 }

export function digitosParaComparar(campo, valor) {
  const digitos = String(valor ?? '').replace(/\D/g, '')
  return campo === 'telefone' && (digitos.length === 12 || digitos.length === 13) && digitos.startsWith('55') ? digitos.slice(2) : digitos
}

export function termoDeDuplicado(campo, valor) {
  const digitos = digitosParaComparar(campo, valor)
  return MINIMO[campo] && digitos.length >= MINIMO[campo] ? digitos : null
}

export function duplicadosDoCliente(campo, valor, itens, idProprio) {
  const alvo = termoDeDuplicado(campo, valor)
  if (!alvo) return []
  return (itens ?? []).filter(c => c.id !== idProprio && digitosParaComparar(campo, c[campo]) === alvo)
}

export const avisoDeDuplicado = (campo, cliente) => `Já existe ${cliente?.nome?.trim() || 'um cadastro sem nome'} com este ${campo}.`
