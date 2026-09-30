// Gerador de PDF mínimo (rodada 11, issue #9). Puro, sem React e sem
// biblioteca: recebe páginas já desenhadas (texto e linha) e devolve os bytes
// do arquivo. Só o que o canhoto e a rota usam: Helvetica das 14 fontes
// padrão do PDF (nenhuma fonte embutida, todo leitor tem), acentos do
// português pela WinAnsiEncoding, texto preto em fundo branco.
//
// Por que não jsPDF: medido na rodada 11, só importar a biblioteca levou o
// build de arquivo único de 901 kB para 1.683 kB (+782 kB, o dobro do app),
// porque o singlefile embute também html2canvas, dompurify e canvg, que ela
// carrega sob demanda. Aqui são poucas dezenas de linhas (registro 93).

// Pontos por milímetro (o PDF mede em 1/72 de polegada).
export const PT_POR_MM = 72 / 25.4
export const mm = (valor) => valor * PT_POR_MM

// Tamanhos de página usados pela impressão, em pontos.
export const LARGURA_BOBINA = mm(80)
export const A4 = { largura: mm(210), altura: mm(297) }

// Larguras das letras da Helvetica e da Helvetica-Bold (métricas AFM da
// Adobe, em milésimos do corpo), do espaço (32) ao til (126). É o que deixa
// quebrar a linha no lugar certo sem medir no navegador.
const LARGURAS_REGULAR = [
  278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
  556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
  1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
  667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
  333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
  556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
]
const LARGURAS_NEGRITO = [
  278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
  556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
  975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
  667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
  333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
  611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
]

// Fora da tabela ASCII (acento, ×, ·): mede pela letra sem acento; o que não
// tiver base conhecida conta como um "n", largura média da fonte.
function larguraDaLetra(letra, negrito) {
  const tabela = negrito ? LARGURAS_NEGRITO : LARGURAS_REGULAR
  const codigo = letra.charCodeAt(0)
  if (codigo >= 32 && codigo <= 126) return tabela[codigo - 32]
  const base = letra.normalize('NFD').charCodeAt(0)
  if (base >= 32 && base <= 126) return tabela[base - 32]
  return tabela['n'.charCodeAt(0) - 32]
}

export function larguraDoTexto(texto, tamanho, negrito = false) {
  let soma = 0
  for (const letra of String(texto)) soma += larguraDaLetra(letra, negrito)
  return (soma * tamanho) / 1000
}

// Quebra por palavra dentro da largura; palavra maior que a linha inteira
// (link, código) é cortada por letra para nunca vazar do papel.
export function quebrarLinhas(texto, larguraMax, tamanho, negrito = false) {
  const linhas = []
  for (const paragrafo of String(texto ?? '').split('\n')) {
    let atual = ''
    for (const palavra of paragrafo.split(/\s+/).filter(Boolean)) {
      const tentativa = atual ? `${atual} ${palavra}` : palavra
      if (larguraDoTexto(tentativa, tamanho, negrito) <= larguraMax) { atual = tentativa; continue }
      if (atual) linhas.push(atual)
      atual = ''
      let pedaco = ''
      for (const letra of palavra) {
        if (larguraDoTexto(pedaco + letra, tamanho, negrito) > larguraMax && pedaco) {
          linhas.push(pedaco)
          pedaco = ''
        }
        pedaco += letra
      }
      atual = pedaco
    }
    linhas.push(atual)
  }
  return linhas
}

// WinAnsiEncoding (cp1252): Latin-1 cai no mesmo byte; os poucos sinais
// tipográficos fora dele têm posição própria. O resto vira "?", nunca some.
// Chave pelo código Unicode (euro, aspas curvas, reticências, marcador e os
// dois traços longos), para o sinal não precisar aparecer escrito aqui.
const WIN_ANSI_EXTRA = Object.fromEntries([
  [0x20ac, 0x80], [0x201a, 0x82], [0x201e, 0x84], [0x2026, 0x85], [0x2018, 0x91], [0x2019, 0x92],
  [0x201c, 0x93], [0x201d, 0x94], [0x2022, 0x95], [0x2013, 0x96], [0x2014, 0x97],
].map(([unicode, byte]) => [String.fromCharCode(unicode), byte]))

// String literal do PDF com tudo fora do ASCII em octal: o arquivo inteiro
// fica ASCII, e o deslocamento de cada objeto no xref é o próprio tamanho do
// texto, sem conta de bytes à parte.
function literal(texto) {
  let saida = ''
  for (const letra of String(texto)) {
    let codigo = letra.charCodeAt(0)
    if (codigo > 126) codigo = WIN_ANSI_EXTRA[letra] ?? (codigo <= 0xff && codigo >= 0xa0 ? codigo : 63)
    if (codigo < 32) codigo = 32
    if (codigo === 40 || codigo === 41 || codigo === 92) saida += '\\' + String.fromCharCode(codigo)
    else if (codigo > 126) saida += '\\' + codigo.toString(8).padStart(3, '0')
    else saida += String.fromCharCode(codigo)
  }
  return `(${saida})`
}

const numero = (valor) => (Math.round(valor * 100) / 100).toString()

const FONTES = { regular: 'F1', negrito: 'F2', italico: 'F3' }
const NOMES_DAS_FONTES = { F1: 'Helvetica', F2: 'Helvetica-Bold', F3: 'Helvetica-Oblique' }

// Desenho de uma página, com `y` medido do TOPO (como na tela); a troca para
// a origem de baixo do PDF acontece só aqui dentro.
function conteudoDaPagina(pagina) {
  const partes = []
  for (const desenho of pagina.desenhos) {
    const y = pagina.altura - desenho.y
    const cinza = numero(desenho.cinza ?? 0)
    if (desenho.tipo === 'texto') {
      const fonte = FONTES[desenho.fonte ?? 'regular']
      partes.push(`BT ${cinza} g /${fonte} ${numero(desenho.tamanho)} Tf ${numero(desenho.x)} ${numero(y)} Td ${literal(desenho.texto)} Tj ET`)
    } else if (desenho.tipo === 'linha') {
      const traco = desenho.tracejada ? '[2 2] 0 d' : '[] 0 d'
      const y2 = pagina.altura - desenho.y2
      partes.push(`${cinza} G ${numero(desenho.espessura ?? 0.5)} w ${traco} ${numero(desenho.x)} ${numero(y)} m ${numero(desenho.x2)} ${numero(y2)} l S`)
    }
  }
  return partes.join('\n')
}

// Monta o arquivo: catálogo, árvore de páginas, três fontes e, por página, o
// objeto da página e o fluxo de desenho. Cada página tem o próprio MediaBox:
// é ele que diz ao leitor e à impressora o tamanho do papel (80 mm de largura
// no canhoto, A4 na rota).
export function montarPdf({ titulo, paginas }) {
  const objetos = []
  const novo = (corpo) => { objetos.push(corpo); return objetos.length }

  const catalogo = novo(null)
  const arvore = novo(null)
  const fontes = Object.entries(NOMES_DAS_FONTES).map(([apelido, nome]) => [
    apelido,
    novo(`<< /Type /Font /Subtype /Type1 /BaseFont /${nome} /Encoding /WinAnsiEncoding >>`),
  ])
  const recursos = `<< /Font << ${fontes.map(([apelido, id]) => `/${apelido} ${id} 0 R`).join(' ')} >> >>`

  const idsDasPaginas = paginas.map((pagina) => {
    const fluxo = conteudoDaPagina(pagina)
    const conteudo = novo(`<< /Length ${fluxo.length} >>\nstream\n${fluxo}\nendstream`)
    return novo(
      `<< /Type /Page /Parent ${arvore} 0 R /MediaBox [0 0 ${numero(pagina.largura)} ${numero(pagina.altura)}] `
      + `/Resources ${recursos} /Contents ${conteudo} 0 R >>`,
    )
  })
  objetos[catalogo - 1] = `<< /Type /Catalog /Pages ${arvore} 0 R >>`
  objetos[arvore - 1] = `<< /Type /Pages /Kids [${idsDasPaginas.map((id) => `${id} 0 R`).join(' ')}] /Count ${idsDasPaginas.length} >>`
  const info = novo(`<< /Title ${literal(titulo ?? '')} /Producer (Casa da Baba) >>`)

  let arquivo = '%PDF-1.4\n'
  const deslocamentos = objetos.map((corpo, indice) => {
    const inicio = arquivo.length
    arquivo += `${indice + 1} 0 obj\n${corpo}\nendobj\n`
    return inicio
  })
  const inicioDoXref = arquivo.length
  arquivo += `xref\n0 ${objetos.length + 1}\n0000000000 65535 f \n`
  arquivo += deslocamentos.map((d) => `${String(d).padStart(10, '0')} 00000 n \n`).join('')
  arquivo += `trailer\n<< /Size ${objetos.length + 1} /Root ${catalogo} 0 R /Info ${info} 0 R >>\n`
  arquivo += `startxref\n${inicioDoXref}\n%%EOF\n`

  const bytes = new Uint8Array(arquivo.length)
  for (let i = 0; i < arquivo.length; i += 1) bytes[i] = arquivo.charCodeAt(i)
  return bytes
}
