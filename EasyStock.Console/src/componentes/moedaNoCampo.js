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
//
// #2080: colado no formato inglês ("1,234.56", "R$ 1,234,567.89": vírgula no milhar e ponto nos
// centavos) o texto vira o nosso ("1234,56") antes de limpar. Antes a primeira vírgula valia como
// decimal e o campo cortava para "1,23" sem aviso.
const FORMATO_INGLES = /^\d{1,3}(,\d{3})+\.\d*$/
const soNumero = (texto) => {
  const limpo = String(texto ?? '').replace(/[^\d.,]/g, '')
  return FORMATO_INGLES.test(limpo) ? limpo.replaceAll(',', '').replace('.', ',') : limpo
}

export function textoDigitadoEmReais(texto) {
  const limpo = soNumero(texto)
  const virgula = limpo.indexOf(',')
  if (virgula === -1) return limpo
  const centavos = limpo.slice(virgula + 1).replace(/\D/g, '').slice(0, 2)
  return `${limpo.slice(0, virgula)},${centavos}`
}

// A digitação perdeu casas decimais ("1,500" ficou "1,50")? O campo avisa em vez de calar.
export function centavosCortados(texto) {
  const limpo = soNumero(texto)
  const virgula = limpo.indexOf(',')
  return virgula !== -1 && limpo.slice(virgula + 1).replace(/\D/g, '').length > 2
}

// Ao sair do campo, o valor volta escrito por inteiro ("100,00", "1.500,00"), sem o "R$" (que fica
// fora do campo). Nulo é campo vazio. Espelho de `textoDosCentavos` de `dominio/formato.js`
// (`componentes` não importa `dominio`); `prova-2080-reais.mjs` garante que escrevem igual.
export const textoDosCentavos = (centavos) => (centavos == null
  ? ''
  : (centavos / 100).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }))
