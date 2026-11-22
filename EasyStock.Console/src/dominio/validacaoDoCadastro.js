// #2258 (auditoria de UX, T12, T14 e T16): regras do cadastro rápido do cliente, uma mensagem por
// campo, para a tela mostrar o erro no próprio campo em vez de deixar o botão mudo.

import { LIMITE_DO_NOME_DO_CLIENTE, partesDoTelefone } from './cadastros.js'

// #2734 (A11): o mesmo limite (e a mesma frase) de Cadastros: a coluna do servidor tem 150.
export const LIMITE_DO_NOME = LIMITE_DO_NOME_DO_CLIENTE

const digitos = (valor) => String(valor ?? '').replace(/\D/g, '')

export function erroDoNome(nome) {
  const texto = String(nome ?? '').trim()
  if (!texto) return 'Escreva o nome'
  return texto.length > LIMITE_DO_NOME ? `Use até ${LIMITE_DO_NOME} caracteres. Agora tem ${texto.length}.` : null
}

// Telefone do Brasil com DDD: 10 (fixo) ou 11 (celular) dígitos, escrito com ou sem o DDI 55.
// "(31) 99999-999" tem 10 dígitos como um fixo, mas o hífen depois de 5 dígitos mostra que é um celular
// sem o último número; o formato digitado desempata.
const CELULAR_SEM_O_ULTIMO = /\D\d{5}-\d{1,3}\s*$/

const TELEFONE_LONGO = 'Telefone com mais de 11 dígitos. Use só o DDD e o número.'

// #2734 (A01, A02): a leitura é a mesma da máscara (`partesDoTelefone`), e a regra é a da rota que grava.
// Padrão (Ficha do Atendimento): a conversa grava pelo E.164 do Brasil (TelefoneE164.cs: +55, DDD que não começa
// com zero, 8 ou 9 dígitos); número de outro país volta com 400, então a tela recusa antes e diz onde ele entra.
// `aceitaDeFora` (telefone extra de Cadastros): a rota aceita de 7 a 15 dígitos (Telefone.cs), com o "+" do país.
export function erroDoTelefone(telefone, { aceitaDeFora = false } = {}) {
  const { digitos: todos, deFora, nacional } = partesDoTelefone(telefone)
  if (todos.length === 0) return 'Informe o telefone com DDD'
  if (deFora && aceitaDeFora) return todos.length < 7 ? 'Telefone incompleto' : todos.length > 15 ? 'Telefone com mais de 15 dígitos. Confira o número.' : null
  // "+5" ainda pode virar "+55": só recusa como de fora quando o DDI já não é o do Brasil.
  if (deFora) return '55'.startsWith(todos) ? 'Telefone incompleto' : 'Aqui só entra telefone do Brasil, com DDD. O de outro país entra em Cadastros.'
  if (nacional.length > 11) return aceitaDeFora ? `${TELEFONE_LONGO} Para outro país, comece com + e o código do país.` : TELEFONE_LONGO
  if (nacional.length < 10) return 'Telefone incompleto'
  if (nacional.length === 10 && CELULAR_SEM_O_ULTIMO.test(String(telefone))) return 'Telefone incompleto'
  return !aceitaDeFora && nacional.startsWith('0') ? 'O DDD não começa com zero. Confira o número.' : null
}

// #3155: a regra do Novo pedido (`NormalizadorTelefone.EhCelularBr`): celular do Brasil, DDD, 9 e depois 6 a 9 (Anatel).
// Número que o cadastro aceita (`erroDoTelefoneExtra`: fixo, 0800, de outro país) e que não casa avisa sem bloquear:
// segue cadastrável, mas o pedido pelo WhatsApp recusa depois. Número inválido já tem o erro, sem aviso por cima.
const CELULAR_BR = /^[1-9][0-9]9[6-9]\d{7}$/
export const AVISO_NAO_CELULAR = 'Este número não recebe Novo pedido: o pedido pelo WhatsApp só aceita celular do Brasil com DDD.'
export function avisoDeCelularBr(telefone) {
  const { digitos, deFora, nacional } = partesDoTelefone(telefone)
  if (!digitos.length || erroDoTelefone(telefone, { aceitaDeFora: true })) return null
  return !deFora && CELULAR_BR.test(nacional) ? null : AVISO_NAO_CELULAR
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
