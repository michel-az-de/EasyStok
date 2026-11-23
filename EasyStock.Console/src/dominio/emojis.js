// #3210: catálogo de emoji por categoria para o seletor do Composer. Regras puras; a tela só desenha.
// Escrito à mão, sem dependência: o nome (em português, minúsculo) é o que a busca procura, então leva palavras úteis.
import { EMOJIS_DA_LOJA, buscarEmojis } from './acoesNaMensagem'

const e = (emoji, nome) => ({ emoji, nome })

// `simbolo` é o emoji que representa a categoria na faixa de abas.
export const CATEGORIAS_DE_EMOJI = [
  { id: 'usados', nome: 'Mais usados', simbolo: '⭐', emojis: EMOJIS_DA_LOJA },
  {
    id: 'carinhas', nome: 'Carinhas', simbolo: '😀', emojis: [
      e('😀', 'sorriso aberto feliz'), e('😃', 'sorriso olhos grandes'), e('😄', 'alegre sorrindo'), e('😁', 'dentes sorriso radiante'),
      e('😆', 'rindo olhos fechados'), e('😂', 'chorando de rir risada'), e('🤣', 'rolando de rir gargalhada'), e('🙂', 'sorriso leve'),
      e('😍', 'olhos de coração apaixonado'), e('🥰', 'sorriso com corações carinho'), e('😘', 'beijo mandando beijo'), e('😎', 'óculos escuros legal'),
      e('🤔', 'pensando dúvida'), e('😐', 'sem expressão neutro'), e('😴', 'dormindo sono'), e('😢', 'chorando triste'),
      e('😭', 'chorando muito'), e('😡', 'bravo raiva'), e('😱', 'assustado grito'), e('😮', 'boca aberta surpresa'),
      e('🤗', 'abraço carinhoso'), e('🥳', 'festa comemorando'), e('🤩', 'olhos de estrela maravilhado'), e('😇', 'anjo inocente'),
      e('🙃', 'de cabeça para baixo ironia'), e('😬', 'constrangido careta'), e('🥺', 'olhos pedindo por favor'), e('😌', 'aliviado tranquilo'),
    ],
  },
  {
    id: 'gestos', nome: 'Gestos', simbolo: '👍', emojis: [
      e('👍', 'joinha positivo'), e('👎', 'negativo desaprovar'), e('👌', 'ok perfeito'), e('✌️', 'paz e amor vitória'),
      e('🤞', 'dedos cruzados sorte'), e('🤝', 'aperto de mão acordo'), e('👏', 'palmas aplauso parabéns'), e('🙌', 'mãos para cima comemorar'),
      e('🙏', 'mãos juntas obrigado por favor'), e('💪', 'braço forte força'), e('👋', 'mão acenando tchau oi'), e('🤙', 'me liga telefone'),
      e('☝️', 'dedo para cima atenção'), e('👆', 'apontando para cima'), e('👇', 'apontando para baixo'), e('👉', 'apontando para a direita'),
      e('👈', 'apontando para a esquerda'), e('🖐️', 'mão aberta pare'), e('✋', 'mão levantada espere'), e('🤌', 'dedos juntos italiano'),
      e('🫶', 'mãos de coração carinho'), e('🤟', 'te amo gesto'), e('✍️', 'escrevendo assinar'), e('🫡', 'continência saudação'),
      e('👀', 'olhos olhando'), e('🧠', 'cérebro ideia'),
    ],
  },
  {
    id: 'comida', nome: 'Comida e bebida', simbolo: '🍕', emojis: [
      e('🍕', 'pizza fatia'), e('🍔', 'hambúrguer lanche'), e('🍟', 'batata frita'), e('🌭', 'cachorro quente'),
      e('🌮', 'taco mexicano'), e('🥪', 'sanduíche'), e('🥗', 'salada verde'), e('🍲', 'panela ensopado'),
      e('🍜', 'macarrão sopa tigela'), e('🍣', 'sushi japonês'), e('🍤', 'camarão frito'), e('🍚', 'arroz'),
      e('🍞', 'pão fatiado'), e('🥐', 'croissant padaria'), e('🥚', 'ovo'), e('🥩', 'carne bife'),
      e('🍖', 'carne osso churrasco'), e('🍅', 'tomate molho'), e('🍎', 'maçã fruta'), e('🍌', 'banana fruta'),
      e('🍓', 'morango fruta'), e('🍇', 'uva fruta'), e('🍫', 'chocolate barra'), e('🍩', 'rosquinha donut'),
      e('🍪', 'biscoito bolacha'), e('🎂', 'bolo de aniversário'), e('🍦', 'sorvete'), e('☕', 'café quente xícara'),
      e('🍺', 'cerveja chope'), e('🍷', 'vinho taça'), e('🥤', 'copo refrigerante suco'), e('💧', 'água gota'),
    ],
  },
  {
    id: 'objetos', nome: 'Objetos e símbolos', simbolo: '💡', emojis: [
      e('💡', 'lâmpada ideia'), e('📱', 'celular telefone'), e('💻', 'computador notebook'), e('📞', 'telefone ligação'),
      e('📧', 'e-mail correio'), e('📝', 'bloco de notas anotação'), e('📄', 'documento papel'), e('📅', 'calendário data'),
      e('🧾', 'recibo nota fiscal'), e('💵', 'dinheiro nota cédula'), e('🪙', 'moeda'), e('🛒', 'carrinho de compras'),
      e('🛍️', 'sacolas de compras'), e('🎁', 'presente embrulho'), e('🔑', 'chave'), e('🔒', 'cadeado trancado'),
      e('🔔', 'sino notificação'), e('📷', 'câmera foto'), e('🎵', 'nota musical música'), e('⚠️', 'atenção aviso alerta'),
      e('❌', 'erro cancelado x'), e('❗', 'exclamação importante'), e('❓', 'interrogação pergunta'), e('💯', 'cem pontos nota máxima'),
      e('💙', 'coração azul amor'), e('💚', 'coração verde amor'), e('💛', 'coração amarelo amor'), e('🧡', 'coração laranja amor'),
      e('💔', 'coração partido'), e('➕', 'mais somar'), e('➡️', 'seta para a direita'), e('🔁', 'repetir setas'),
    ],
  },
  {
    id: 'natureza', nome: 'Natureza', simbolo: '🌿', emojis: [
      e('🐶', 'cachorro cão'), e('🐱', 'gato'), e('🐭', 'rato'), e('🐰', 'coelho'),
      e('🦊', 'raposa'), e('🐻', 'urso'), e('🐼', 'panda'), e('🦁', 'leão'),
      e('🐮', 'vaca'), e('🐷', 'porco'), e('🐸', 'sapo'), e('🐵', 'macaco'),
      e('🐔', 'galinha'), e('🐧', 'pinguim'), e('🐦', 'passarinho pássaro'), e('🐝', 'abelha'),
      e('🦋', 'borboleta'), e('🐢', 'tartaruga'), e('🐟', 'peixe'), e('🐬', 'golfinho'),
      e('🌸', 'flor de cerejeira'), e('🌹', 'rosa flor'), e('🌻', 'girassol'), e('🌷', 'tulipa flor'),
      e('🌳', 'árvore'), e('🌿', 'folha erva planta'), e('🍀', 'trevo sorte'), e('🌞', 'sol com rosto'),
      e('🌙', 'lua noite'), e('🌈', 'arco-íris'), e('☁️', 'nuvem'), e('🌧️', 'chuva'),
    ],
  },
  {
    id: 'viagem', nome: 'Viagem e lugares', simbolo: '🚗', emojis: [
      e('🚗', 'carro'), e('🚕', 'táxi'), e('🚌', 'ônibus'), e('🚚', 'caminhão de entrega'),
      e('🏍️', 'moto motocicleta'), e('🚲', 'bicicleta'), e('🚴', 'ciclista'), e('✈️', 'avião viagem'),
      e('🚀', 'foguete'), e('🚢', 'navio'), e('🚆', 'trem'), e('🚦', 'semáforo sinal'),
      e('⛽', 'posto de gasolina combustível'), e('🏠', 'casa'), e('🏢', 'prédio edifício'), e('🏪', 'loja mercadinho'),
      e('🏬', 'shopping loja de departamentos'), e('🏥', 'hospital'), e('🏫', 'escola'), e('⛪', 'igreja'),
      e('🗺️', 'mapa'), e('🧭', 'bússola direção'), e('🏖️', 'praia'), e('⛰️', 'montanha'),
      e('🌆', 'cidade ao entardecer'), e('🌃', 'cidade à noite'), e('🧳', 'mala viagem'), e('🎫', 'ingresso bilhete'),
      e('🅿️', 'estacionamento'), e('🚏', 'ponto de ônibus'), e('🛣️', 'estrada rodovia'), e('📌', 'alfinete local marcado'),
    ],
  },
]

// A busca olha todas as categorias, com as regras do `buscarEmojis` (sem acento, `:` na frente), e não repete o emoji
// que mora em duas categorias. Sem termo, devolve todos, uma vez cada.
export function buscarEmTodasAsCategorias(termo) {
  const vistos = new Set()
  const todos = CATEGORIAS_DE_EMOJI.flatMap((c) => c.emojis).filter((x) => !vistos.has(x.emoji) && vistos.add(x.emoji))
  return buscarEmojis(termo, todos)
}
