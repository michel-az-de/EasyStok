// Larguras padrão das colunas do Atendimento por tamanho de tela (#1697). Puro, sem React nem
// localStorage: `useLarguras` guarda e lê, aqui só a regra.
//
// Medido na tela real: com 360 e 420 fixos, em 1280 a conversa ficava com 380 px, menor que o
// Balcão e a Ficha, e em 1920 a Ficha rolava 3,3 telas ao lado de 400 px vazios na conversa.

// Ficha em 420 (rodada 4): 320 espremia o nome do prato a 0 px de largura com
// a alça de arrastar e o contador na mesma linha (achado do corte na coluna).
// Balcão em 360 (rodada 5, seção 1): os chips de canal cabem numa linha só a
// partir de 336 px úteis.
// Cardápio em 460 (#1474): com foto, preço e o botão de enviar foto, 400 deixava o nome
// do prato em quatro linhas.
export const PADRAO = { balcao: 360, ficha: 420, cardapio: 460 }

const NOTEBOOK = 1400
const TELA_LARGA = 1700

export function padraoPorTela(larguraDaTela) {
  if (larguraDaTela < NOTEBOOK) return { ...PADRAO, balcao: 320, ficha: 360 }
  if (larguraDaTela >= TELA_LARGA) return { ...PADRAO, ficha: 520 }
  return { ...PADRAO }
}

// Só o que a pessoa arrastou. Até esta rodada o padrão era gravado no primeiro uso, sem ninguém
// arrastar nada: valor salvo igual ao padrão antigo não é escolha.
export function escolhidasDoSalvo(salvo) {
  if (!salvo || typeof salvo !== 'object' || Array.isArray(salvo)) return {}
  return Object.fromEntries(Object.entries(salvo)
    .filter(([chave, valor]) => chave in PADRAO && Number.isFinite(valor) && valor !== PADRAO[chave]))
}

// Com o cardápio aberto do lado do Balcão, a coluna abre espaço para ele em vez de deixar o
// painel por cima do começo das mensagens. 440 é a largura mínima do painel (`PainelCardapio`)
// e 12 a alça entre o Balcão e a conversa.
export const balcaoComCardapio = ({ balcao, cardapio }) => Math.max(balcao, Math.max(cardapio, 440) - 12)

export const largurasDaTela = (escolhidas, larguraDaTela) => ({ ...padraoPorTela(larguraDaTela), ...escolhidas })
