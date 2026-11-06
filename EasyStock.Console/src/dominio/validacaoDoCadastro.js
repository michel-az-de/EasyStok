// #2258 (auditoria de UX, T12, T14 e T16): regras do cadastro rápido do cliente, uma mensagem por
// campo, para a tela mostrar o erro no próprio campo em vez de deixar o botão mudo.

export const LIMITE_DO_NOME = 80

const digitos = (valor) => String(valor ?? '').replace(/\D/g, '')

export function erroDoNome(nome) {
  const texto = String(nome ?? '').trim()
  if (!texto) return 'Escreva o nome'
  return texto.length > LIMITE_DO_NOME ? `Use até ${LIMITE_DO_NOME} caracteres` : null
}

// Telefone com DDD: 10 (fixo) ou 11 (celular) dígitos.
// "(31) 99999-999" tem 10 dígitos como um fixo, mas o hífen depois de 5 dígitos mostra que é um celular
// sem o último número; o formato digitado desempata.
const CELULAR_SEM_O_ULTIMO = /\D\d{5}-\d{1,3}\s*$/

export function erroDoTelefone(telefone) {
  const quantos = digitos(telefone).length
  if (quantos === 0) return 'Informe o telefone com DDD'
  if (quantos < 10) return 'Telefone incompleto'
  return quantos === 10 && CELULAR_SEM_O_ULTIMO.test(String(telefone)) ? 'Telefone incompleto' : null
}

// O CEP é opcional; preenchido, tem 8 dígitos.
export function erroDoCep(cep) {
  const quantos = digitos(cep).length
  return quantos > 0 && quantos < 8 ? 'CEP precisa de 8 dígitos' : null
}

const REGRAS = { nome: erroDoNome, telefone: erroDoTelefone, cep: erroDoCep }
// Ordem em que os campos aparecem na tela: o primeiro com erro é o que recebe o foco.
const ORDEM = ['nome', 'telefone', 'cep']

// `campos` limita a conferência ao que está na tela (cliente que já tem telefone não mostra o campo).
export function validarCadastro({ campos = ORDEM, ...valores }) {
  const erros = {}
  for (const campo of ORDEM) {
    if (!campos.includes(campo) || !(campo in valores)) continue
    const erro = REGRAS[campo](valores[campo])
    if (erro) erros[campo] = erro
  }
  return { erros, primeiro: ORDEM.find((campo) => campo in erros) ?? null }
}
