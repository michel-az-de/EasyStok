// Geometria do impresso da API medida antes do diálogo (#1756). A bobina não tem altura e
// `@page{size:80mm auto}` é inválido (o navegador cai no A4): a API manda `size:auto`, a largura
// fica no `.im`, e aqui o comprimento medido vira o `@page` da cópia que vai imprimir. O HTML
// guardado no histórico não muda, então a reimpressão do original passa pela mesma medida.

const PX_POR_MM = 96 / 25.4
// Tolerância de arredondamento do navegador ao converter mm em px.
const FOLGA_PX = 1
// [largura, altura] em mm; bobina sem altura.
const FORMATOS = { 's-58': [58], 's-80': [80], 's-1015': [100, 150], 's-a5': [148, 210], 's-a4': [210, 297] }

const formatoDa = (classes) => {
  const classe = classes.find((c) => c in FORMATOS)
  if (!classe) throw new Error('O impresso não informa um formato de papel conhecido.')
  return FORMATOS[classe]
}

// Recebe a medida de cada via (`.im`) em px CSS e devolve o `@page` a acrescentar, ou null quando
// o papel tem tamanho fixo. Lança com o motivo quando o conteúdo não cabe no papel.
export function paginaDoImpresso(folhas) {
  if (!folhas.length) return null
  const [largura, altura] = formatoDa(folhas[0].classes)
  for (const folha of folhas) {
    if (folha.transborda || Math.abs(folha.largura - largura * PX_POR_MM) > FOLGA_PX) {
      throw new Error('O conteúdo ultrapassa a largura do papel. Escolha um papel maior ou revise os dados antes de imprimir.')
    }
    if (altura && largura === 100 && folha.altura > altura * PX_POR_MM + FOLGA_PX) {
      throw new Error('O conteúdo não cabe em uma etiqueta de 10×15 cm. Escolha A4, A5 ou bobina para este documento.')
    }
  }
  if (altura) return null
  // Um milímetro a mais para o corte não comer a última linha.
  const comprimento = Math.ceil(Math.max(...folhas.map((f) => f.altura)) / PX_POR_MM) + 1
  return `@page{size:${largura}mm ${comprimento}mm;margin:0}`
}

// Mede as vias no documento já carregado (fontes e imagens prontas) e fixa o `@page` da bobina.
export function prepararGeometriaImpresso(documento) {
  const folhas = [...(documento?.querySelectorAll?.('.im') ?? [])].map((folha) => {
    const caixa = folha.getBoundingClientRect()
    const transborda = folha.scrollWidth > folha.clientWidth + FOLGA_PX || [...folha.querySelectorAll('*')].some((filho) => {
      const limite = filho.getBoundingClientRect()
      return limite.width > 0 && (limite.left < caixa.left - FOLGA_PX || limite.right > caixa.right + FOLGA_PX)
    })
    return { classes: [...folha.classList], largura: caixa.width, altura: Math.max(caixa.height, folha.scrollHeight), transborda }
  })
  const pagina = paginaDoImpresso(folhas)
  if (!pagina) return
  const estilo = documento.createElement('style')
  estilo.textContent = pagina
  documento.head.appendChild(estilo)
}
