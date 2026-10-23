// #1595: moeda entra pelos centavos, como em caixa registradora. O valor fica alinhado à
// direita, então o clique quase sempre cai antes do texto; com o cursor ali a tecla entrava na
// frente dos zeros e "5000" virava R$ 50.000,00 no lugar de R$ 50,00.

const soDigitos = (texto) => texto.replace(/\D/g, '')

// Os dígitos do campo depois de uma mudança, dado o texto mascarado de antes e o de agora.
// Tecla que entrou fora do fim (sem apagar nada) conta como se tivesse entrado no fim: a
// posição do cursor deixa de mudar o valor. Apagar e digitar por cima de um trecho selecionado
// seguem como o navegador fez.
export function digitosDeMoeda(anterior, novo) {
  let prefixo = 0
  while (prefixo < anterior.length && prefixo < novo.length && anterior[prefixo] === novo[prefixo]) prefixo += 1
  let sufixo = 0
  const maximo = Math.min(anterior.length, novo.length) - prefixo
  while (sufixo < maximo && anterior[anterior.length - 1 - sufixo] === novo[novo.length - 1 - sufixo]) sufixo += 1
  const apagado = anterior.slice(prefixo, anterior.length - sufixo)
  const entrou = novo.slice(prefixo, novo.length - sufixo)
  if (!apagado && entrou && sufixo > 0) return soDigitos(anterior) + soDigitos(entrou)
  return soDigitos(novo)
}

// Para onde o cursor vai: solto fora do fim, volta para o fim (devolve a posição), que é onde
// a tecla conta. Trecho selecionado (Tab, Ctrl+A, duplo clique) fica como está. Devolve null
// quando não há o que mexer.
export function cursorDeMoeda({ inicio, fim, tamanho }) {
  return inicio === fim && inicio !== tamanho ? tamanho : null
}

// J2 (#1990): no caixa o dinheiro se digita em reais, como se conta a gaveta: "100" é R$ 100,00 e a
// vírgula separa os centavos. O campo guarda o que foi digitado; quem lê o valor é a tela (ela
// injeta `lerMoeda`, do domínio). Aqui só a limpeza da digitação: dígitos e pontos de milhar, uma
// vírgula e no máximo dois centavos.
export function textoDigitadoEmReais(texto) {
  const limpo = String(texto ?? '').replace(/[^\d.,]/g, '')
  const virgula = limpo.indexOf(',')
  if (virgula === -1) return limpo
  const centavos = limpo.slice(virgula + 1).replace(/\D/g, '').slice(0, 2)
  return `${limpo.slice(0, virgula)},${centavos}`
}

// Ao sair do campo, o valor volta escrito por inteiro ("100,00", "1.500,00"), sem o "R$" (que fica
// fora do campo). Nulo é campo vazio.
export const textoDosCentavos = (centavos) => (centavos == null
  ? ''
  : (centavos / 100).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }))
