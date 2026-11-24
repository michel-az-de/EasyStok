// Larguras padrão das colunas do Atendimento por tamanho de tela (#1697). Puro, sem React nem
// localStorage: `useLarguras` guarda e lê, aqui só a regra.
//
// Medido na tela real: com 360 e 420 fixos, em 1280 a conversa ficava com 380 px, menor que o
// Balcão e a Ficha, e em 1920 a Ficha rolava 3,3 telas ao lado de 400 px vazios na conversa.

// A ficha é um resumo; a comanda abre no painel móvel. Busca e filtros
// avançados têm controles próprios, então não determinam a largura da lista.
// Cardápio em 460 (#1474): com foto, preço e o botão de enviar foto, 400 deixava o nome
// do prato em quatro linhas.
export const PADRAO = { balcao: 276, ficha: 316, cardapio: 460 }
const PADRAO_ANTERIOR = { balcao: 360, ficha: 420, cardapio: 460 }

const NOTEBOOK = 1400
// #3265 (C6) revê a tela larga da #1697, que dava a sobra à conversa: em 1920 ela ficava com 1176 px e o balão do
// cliente a ~1.130 px do da loja. A conversa para na largura de leitura e a sobra vai, metade para cada lado, ao
// Balcão e à Ficha até o máximo das alças (`Moldura.jsx`). Acima disso (~1980 px), o `max-width` da thread segura.
export const LEITURA = 880
export const MAXIMO = { balcao: 480, ficha: 520 }
// Trilho e as duas alças da moldura de três colunas (`dominio/paineis.js`: TRILHO + 2 * VAO; `.palcoTres` não tem
// padding). Hooks não importam do domínio (`verificar-camadas.mjs`); a `prova-1697` confere que é o mesmo número.
export const TRILHO_E_VAOS = 100

export function padraoPorTela(larguraDaTela) {
  if (larguraDaTela < NOTEBOOK) return { ...PADRAO, balcao: 260, ficha: 292 }
  const conversa = larguraDaTela - TRILHO_E_VAOS - PADRAO.balcao - PADRAO.ficha
  const sobra = Math.max(0, conversa - LEITURA)
  return {
    ...PADRAO,
    balcao: Math.min(MAXIMO.balcao, PADRAO.balcao + Math.floor(sobra / 2)),
    ficha: Math.min(MAXIMO.ficha, PADRAO.ficha + Math.ceil(sobra / 2)),
  }
}

// A versão atual guarda apenas escolhas explícitas, inclusive o valor de um padrão anterior.
export function escolhidasDoSalvo(salvo) {
  if (!salvo || typeof salvo !== 'object' || Array.isArray(salvo)) return {}
  return Object.fromEntries(Object.entries(salvo)
    .filter(([chave, valor]) => chave in PADRAO && Number.isFinite(valor)))
}

// A comparação com o padrão antigo só vale na migração. Na versão nova, até
// 360/420 podem ser escolhas explícitas e precisam sobreviver ao recarregamento.
export const migrarLargurasAntigas = (salvo) => Object.fromEntries(Object.entries(escolhidasDoSalvo(salvo))
  .filter(([chave, valor]) => valor !== PADRAO_ANTERIOR[chave]))

// Com o cardápio aberto do lado do Balcão, a coluna abre espaço para ele em vez de deixar o
// painel por cima do começo das mensagens. 440 é a largura mínima do painel (`PainelCardapio`)
// e 12 a alça entre o Balcão e a conversa.
export const balcaoComCardapio = ({ balcao, cardapio }) => Math.max(balcao, Math.max(cardapio, 440) - 12)

// #1828: do lado do Resumo, o mesmo. Por cima, o amplo cobria 156 px da conversa em 1440 (medido).
export const fichaComCardapio = ({ ficha, cardapio }) => Math.max(ficha, Math.max(cardapio, 440) - 12)

export const largurasDaTela = (escolhidas, larguraDaTela) => ({ ...padraoPorTela(larguraDaTela), ...escolhidas })

// #3265 (revisão da PR #3266): `null` é restaurar. A escolha sai e a coluna volta a acompanhar a tela; gravar o
// padrão da tela de agora levava o 450/490 de 1920 para o notebook. Arrastar, mesmo até o padrão, é escolha.
export function escolher(escolhidas, chave, valor) {
  const novo = { ...escolhidas }
  if (valor == null) delete novo[chave]
  else novo[chave] = valor
  return novo
}
