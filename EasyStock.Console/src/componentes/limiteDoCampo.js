// #2792: a frase única do limite de um campo, no lugar do `maxLength` que cortava em silêncio. Mora em
// `componentes` porque é texto de tela que o próprio Campo mostra (componentes não importam de dominio).
// null quando cabe ou quando não há limite. Conta o texto como está no campo, o mesmo tamanho que a pessoa vê.
export function erroDoLimite(texto, limite) {
  const tamanho = String(texto ?? '').length
  return limite && tamanho > limite ? `Use até ${limite} caracteres. Agora tem ${tamanho}.` : null
}

// Para travar o envio: true quando algum par [texto, limite] passa do limite.
export const algumPassaDoLimite = (...pares) => pares.some(([texto, limite]) => erroDoLimite(texto, limite) !== null)
