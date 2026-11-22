// #2930: o celular que a equipe digita no Novo pedido de cliente sem telefone. A API confere de novo e grava no cadastro;
// aqui só se evita a ida e volta com o número que nunca passaria (fixo, sem DDD).
import { CELULAR_BR } from './validacaoDoCadastro.js'

export function erroDoCelular(texto) {
  let digitos = String(texto ?? '').replace(/\D/g, '')
  if (digitos.length > 11 && digitos.startsWith('55')) digitos = digitos.slice(2)
  if (!digitos) return 'Digite o celular com DDD.'
  if (digitos.length < 10) return 'Falta o DDD: digite o celular com DDD, como (11) 98888-7777.'
  // #3155: a mesma regra da API (`NormalizadorTelefone.EhCelularBr`): DDD de 1 a 9, 9 e depois 6 a 9 (Anatel).
  if (!CELULAR_BR.test(digitos)) return 'Este número não é de celular. O pedido segue pelo WhatsApp: digite um celular com DDD.'
  return null
}
