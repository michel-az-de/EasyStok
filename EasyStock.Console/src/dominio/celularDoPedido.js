// #2930: o celular que a equipe digita no Novo pedido de cliente sem telefone. A API confere de novo e grava no cadastro;
// aqui só se evita a ida e volta com o número que nunca passaria (fixo, sem DDD).
export function erroDoCelular(texto) {
  let digitos = String(texto ?? '').replace(/\D/g, '')
  if (digitos.length > 11 && digitos.startsWith('55')) digitos = digitos.slice(2)
  if (!digitos) return 'Digite o celular com DDD.'
  if (digitos.length < 10) return 'Falta o DDD: digite o celular com DDD, como (11) 98888-7777.'
  // #3155: depois do 9 vem 6 a 9 (Anatel), a mesma regra da API (`NormalizadorTelefone.EhCelularBr`).
  if (digitos.length !== 11 || digitos[2] !== '9' || !/[6-9]/.test(digitos[3])) return 'Este número não é de celular. O pedido segue pelo WhatsApp: digite um celular com DDD.'
  return null
}
