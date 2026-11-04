// Painéis reposicionáveis do Balcão (#1806, revisão de 14/10): o Cardápio de hoje e o Assistente
// trocam de lado, mudam de largura e ampliam sem cobrir a conversa. Puro: a preferência chega
// por parâmetro e sai normalizada; quem guarda é `aplicacao/preferenciaDosPaineis.js`.
//
// Geometria medida no CSS da moldura (`app/moldura.module.css` e o trilho de `componentes/Trilho.module.css`),
// no layout de três colunas:
//   trilho de 100 px | Balcão | alça 12 | conversa (mínimo 320) | alça 12 | Resumo | folga 12
// A conversa nunca fica com menos de CONVERSA_MINIMA visível com um painel encaixado: 320 é o
// `min-width` de `.palcoTres > .centro`, a mesma leitura mínima que a moldura já garante.
// `ferramentas/prova-1806-paineis.mjs` confere esses números no próprio CSS.

export const CONVERSA_MINIMA = 320
// #1870/#1872: a largura do trilho do Balcão e dos módulos mora aqui e só aqui. O token CSS
// `--largura-trilho` é gerado dela na partida (`tokensDaGeometria`, aplicado por `main.jsx`);
// `tokens.css` deriva o item (`--largura-item-trilho`) do token.
export const TRILHO = 76
export const VAO = 12
// Folga do assistente até a borda da tela (`--e-4`).
export const MARGEM_ASSISTENTE = 16

// Cardápio: 440 é o mínimo do #1474 (nome, porção, preço e foto sem espremer o nome); 460 o padrão.
export const CARDAPIO = { min: 440, max: 600, padrao: 460 }
// Assistente: 432 era a largura fixa; 320 ainda cabe cartão e campo de pergunta.
export const ASSISTENTE = { min: 320, max: 560, padrao: 432 }

export const LADOS_CARDAPIO = ['esquerda', 'direita']
export const LADOS_ASSISTENTE = ['esquerda', 'direita', 'flutuante']
export const MODOS_CARDAPIO = ['compacto', 'amplo']

// Tokens CSS que nascem da geometria acima, nome e valor, prontos para `style.setProperty`.
export const tokensDaGeometria = () => ({ '--largura-trilho': `${TRILHO}px` })

export const limitar = (valor, min, max) => Math.min(max, Math.max(min, valor))

export const ladoValido = (lado, validos, padrao) => (validos.includes(lado) ? lado : padrao)

const objeto = (valor) => (valor && typeof valor === 'object' && !Array.isArray(valor) ? valor : {})
const numero = (valor) => (Number.isFinite(valor) ? valor : null)

export function normalizarCardapio(salvo) {
  const { lado, modo } = objeto(salvo)
  return {
    lado: ladoValido(lado, LADOS_CARDAPIO, 'esquerda'),
    modo: ladoValido(modo, MODOS_CARDAPIO, 'compacto'),
  }
}

export function normalizarAssistente(salvo) {
  const { lado, largura, x, y } = objeto(salvo)
  return {
    lado: ladoValido(lado, LADOS_ASSISTENTE, 'direita'),
    largura: numero(largura) === null ? ASSISTENTE.padrao : limitar(largura, ASSISTENTE.min, ASSISTENTE.max),
    x: numero(x),
    y: numero(y),
  }
}

// Onde a conversa começa e termina na tela, sem painel nenhum.
const conversaSemPainel = ({ tela, balcao, ficha }) => ({
  inicio: TRILHO + balcao + VAO,
  fim: tela - VAO - ficha - VAO,
})

// Largura da conversa que fica à vista com o painel aberto. `empurra`: o cardápio alarga a coluna
// do lado em que abre em vez de cobri-la, o Balcão à esquerda e o Resumo à direita (#1828;
// `balcaoComCardapio` e `fichaComCardapio`, hooks/larguras.js).
export function conversaVisivel({ tela, balcao, ficha, lado, largura, margem = 0, empurra = false }) {
  const { inicio, fim } = conversaSemPainel({ tela, balcao, ficha })
  if (lado === 'direita' && empurra) return tela - VAO - Math.max(ficha, largura - VAO) - VAO - inicio
  if (lado === 'direita') return Math.min(fim, tela - margem - largura) - inicio
  if (empurra) return fim - (TRILHO + Math.max(balcao, largura - VAO) + VAO)
  return fim - Math.max(inicio, TRILHO + margem + largura)
}

// Maior largura do painel que ainda deixa CONVERSA_MINIMA da conversa à vista, dentro da faixa
// do painel. Sem as três colunas (tablet), o painel só precisa caber na tela com as margens.
export function larguraMaxima({ tela, balcao = 0, ficha = 0, colunas = true, lado, margem = 0, empurra = false, faixa }) {
  if (!colunas) return limitar(tela - 2 * margem, faixa.min, faixa.max)
  const { inicio, fim } = conversaSemPainel({ tela, balcao, ficha })
  let maxima
  if (lado === 'direita' && empurra) maxima = tela - VAO - inicio - CONVERSA_MINIMA
  else if (lado === 'direita') maxima = tela - margem - inicio - CONVERSA_MINIMA
  else if (empurra) maxima = fim - CONVERSA_MINIMA - TRILHO
  else maxima = fim - CONVERSA_MINIMA - TRILHO - margem
  return limitar(maxima, faixa.min, faixa.max)
}

// Compacto: a largura arrastada, dentro dos limites da tela. Amplo: a máxima que a tela permite.
export function larguraDoCardapio({ modo, guardada, maxima }) {
  if (modo === 'amplo') return maxima
  return limitar(numero(guardada) ?? CARDAPIO.padrao, CARDAPIO.min, Math.max(CARDAPIO.min, maxima))
}

// Assistente solto: sempre inteiro dentro da tela. Sem posição guardada, nasce no canto
// inferior direito, onde o balão sempre abriu. Mais alto que a tela, cola no topo.
export function posicaoDentroDaTela({ x, y, largura, altura, tela, alturaTela, margem = 0 }) {
  const xPadrao = tela - largura - margem
  const yPadrao = alturaTela - altura - margem
  return {
    x: Math.max(0, Math.min(numero(x) ?? xPadrao, tela - largura)),
    y: Math.max(0, Math.min(numero(y) ?? yPadrao, alturaTela - altura)),
  }
}
