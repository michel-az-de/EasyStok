/* eslint-disable no-console */
// Prova da impressão em PDF (rodada 11, issue #9): importa as MESMAS funções
// que os botões "Baixar PDF" e "Imprimir" chamam (`pdfDoCanhoto` e
// `pdfDaRota`, dominio/impressao.js), lê o arquivo gerado e confere o tamanho
// do papel no MediaBox, a estrutura do PDF e o conteúdo vindo do estado.
//
//   node ferramentas/prova-r11-impressao.mjs
//
// Mesmo gancho de resolução de `prova-contadores-canal.mjs`: o domínio importa
// sem extensão, como o Vite aceita.

import { registerHooks } from 'node:module'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    try {
      return proximo(especificador, contexto)
    } catch (erro) {
      if (especificador.startsWith('.') && !/\.m?jsx?$/.test(especificador)) {
        return proximo(especificador + '.js', contexto)
      }
      throw erro
    }
  },
})

const { pdfDoCanhoto, pdfDaRota } = await import('../src/dominio/impressao.js')
const { larguraDoTexto, mm } = await import('../src/dominio/pdf.js')
const { itensDetalhados } = await import('../src/dominio/pedido.js')
const catalogo = await import('../src/infra/catalogo.js')

const CARDAPIO = catalogo.CARDAPIO
const LINHAS = catalogo.LINHAS_PRODUTO
const JANELAS = catalogo.JANELAS_ENTREGA
const CONSTANTES = {
  minutosTrecho: catalogo.MINUTOS_TRECHO,
  minutosChegadaEntregador: catalogo.MINUTOS_CHEGADA_ENTREGADOR,
  minutosConferir: catalogo.MINUTOS_CONFERIR,
}
const AGORA = new Date(catalogo.INSTANTE_INICIAL).getTime()

let passos = 0
const ok = (texto) => { passos += 1; console.log(`ok ${passos} - ${texto}`) }

// Leitura do PDF como texto (o gerador escreve só ASCII).
const comoTexto = (bytes) => String.fromCharCode(...bytes)
const mediaBoxes = (texto) => [...texto.matchAll(/\/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]/g)]
  .map((m) => ({ largura: Number(m[1]), altura: Number(m[2]) }))

// Estrutura: cada entrada do xref aponta para o começo do objeto certo e o
// startxref aponta para o xref. É o que um leitor de PDF percorre ao abrir.
function conferirEstrutura(bytes, nome) {
  const texto = comoTexto(bytes)
  assert.ok(texto.startsWith('%PDF-1.4\n'), `${nome}: cabeçalho %PDF`)
  assert.ok(texto.trimEnd().endsWith('%%EOF'), `${nome}: fim %%EOF`)
  const inicioXref = Number(texto.match(/startxref\n(\d+)\n%%EOF/)[1])
  assert.equal(texto.slice(inicioXref, inicioXref + 4), 'xref', `${nome}: startxref aponta o xref`)
  const [, total] = texto.slice(inicioXref).match(/xref\n0 (\d+)\n/)
  const entradas = [...texto.slice(inicioXref).matchAll(/(\d{10}) 00000 n /g)].map((m) => Number(m[1]))
  assert.equal(entradas.length, Number(total) - 1, `${nome}: xref com todos os objetos`)
  entradas.forEach((deslocamento, indice) => {
    assert.ok(texto.startsWith(`${indice + 1} 0 obj`, deslocamento), `${nome}: objeto ${indice + 1} no deslocamento do xref`)
  })
  // Cada fluxo tem exatamente o /Length declarado.
  for (const m of texto.matchAll(/<< \/Length (\d+) >>\nstream\n/g)) {
    const inicio = m.index + m[0].length
    assert.equal(texto.slice(inicio + Number(m[1]), inicio + Number(m[1]) + 10), '\nendstream', `${nome}: /Length do fluxo`)
  }
  return texto
}

// Todo texto desenhado cabe dentro da margem da página (nada vaza do papel).
function conferirMargens(documento, margem, nome) {
  for (const pagina of documento.paginas) {
    for (const d of pagina.desenhos.filter((x) => x.tipo === 'texto')) {
      const fim = d.x + larguraDoTexto(d.texto, d.tamanho, d.fonte === 'negrito')
      assert.ok(d.x >= margem - 0.01 && fim <= pagina.largura - margem + 0.01, `${nome}: "${d.texto}" dentro da margem (${fim.toFixed(1)} de ${(pagina.largura - margem).toFixed(1)})`)
      assert.ok(d.y > 0 && d.y < pagina.altura, `${nome}: "${d.texto}" dentro da altura`)
    }
  }
}
const textos = (documento) => documento.paginas.flatMap((p) => p.desenhos.filter((d) => d.tipo === 'texto').map((d) => d.texto))

// ---------------------------------------------------------------- canhoto
const pedido = (numero, itens, janela = 'j1') => ({
  numero, janela, estado: 'pago', itens, cobranca: { criadaEm: '2026-09-22T11:02:00-03:00' },
})
const canhotoCurto = pdfDoCanhoto({
  pedido: pedido('2026-0184', [{ sku: 'LAS-CLA', qtd: 2, obs: 'Molho à parte' }]),
  itens: itensDetalhados(pedido('2026-0184', [{ sku: 'LAS-CLA', qtd: 2, obs: 'Molho à parte' }]), CARDAPIO),
  linhas: LINHAS, nomeCliente: 'José Moretti', endereco: 'Rua Girassol, 412, apto 71, Vila Madalena', faixa: '11h30 às 12h30',
})
const itensLongos = [
  { sku: 'LAS-CLA', qtd: 2, obs: 'Sem pimenta, pouco sal e massa bem al dente, a cliente pediu duas vezes' },
  { sku: 'RAV-LIM', qtd: 1, obs: '' },
  { sku: 'RAV-ABO', qtd: 3, obs: 'Molho à parte' },
  { sku: 'TOR-COS', qtd: 1, obs: 'Sem queijo' },
]
const pedidoLongo = pedido('2026-0190', itensLongos, 'j2')
const canhotoLongo = pdfDoCanhoto({
  pedido: pedidoLongo, itens: itensDetalhados(pedidoLongo, CARDAPIO), linhas: LINHAS,
  nomeCliente: 'Maria Aparecida dos Santos Figueiredo', endereco: 'Avenida Pompeia, 1870, bloco B, apartamento 1204, Perdizes, 05022-001', faixa: '12h30 às 13h30',
})

for (const [nome, canhoto] of [['canhoto curto', canhotoCurto], ['canhoto longo', canhotoLongo]]) {
  const texto = conferirEstrutura(canhoto.bytes, nome)
  const caixas = mediaBoxes(texto)
  assert.equal(caixas.length, 1, `${nome}: uma página só, a bobina não quebra folha`)
  assert.equal(caixas[0].largura, 226.77, `${nome}: largura 80 mm = 226,77 pt`)
  assert.ok(Math.abs(caixas[0].largura / (72 / 25.4) - 80) < 0.01, `${nome}: 80 mm`)
  conferirMargens(canhoto, mm(4), nome)
  assert.ok(!textos(canhoto).some((t) => /R\$/.test(t)), `${nome}: sem preço (RN-33, não é cupom fiscal)`)
  ok(`${nome}: PDF válido, MediaBox ${caixas[0].largura} x ${caixas[0].altura} pt = 80 x ${(caixas[0].altura / (72 / 25.4)).toFixed(1)} mm, texto dentro da margem, sem preço`)
}
const alturaCurta = mediaBoxes(comoTexto(canhotoCurto.bytes))[0].altura
const alturaLonga = mediaBoxes(comoTexto(canhotoLongo.bytes))[0].altura
assert.ok(alturaLonga > alturaCurta + mm(20), 'altura acompanha o conteúdo')
ok(`altura acompanha o conteúdo: ${(alturaCurta / mm(1)).toFixed(1)} mm com 1 item, ${(alturaLonga / mm(1)).toFixed(1)} mm com 4 itens`)

// Conteúdo do estado (RN-33, US-036): número, cliente, janela, endereço, itens
// agrupados por linha com porção e observação.
const doLongo = textos(canhotoLongo).join('\n')
for (const esperado of ['CANHOTO DE PEDIDO, NÃO É CUPOM FISCAL', 'Nº 0190', 'Janela 12h30 às 13h30', 'Enviada', 'PARA SERVIR', 'PREPARAR EM CASA',
  'Lasanha clássica', 'Ravióli de limão siciliano', 'Tortéi de costela', '800 g', '500 g', 'Molho à parte', 'Sem queijo', '2×', '3×']) {
  assert.ok(doLongo.includes(esperado), `canhoto traz "${esperado}"`)
}
assert.ok(doLongo.indexOf('PARA SERVIR') < doLongo.indexOf('PREPARAR EM CASA'), 'linha servir antes de casa')
assert.ok(doLongo.includes('Maria Aparecida') && doLongo.includes('Perdizes'), 'nome e endereço longos quebram sem se perder')
assert.equal(canhotoLongo.nomeArquivo, 'canhoto-0190.pdf')
ok('canhoto traz número, cliente, janela, endereço, itens por linha com porção e observação; arquivo canhoto-0190.pdf')

// Acento em WinAnsi: "NÃO" vira \303 no fluxo, nunca some nem vira lixo UTF-8.
assert.ok(comoTexto(canhotoLongo.bytes).includes('(CANHOTO DE PEDIDO, N\\303O \\311 CUPOM FISCAL) Tj'), 'acento em WinAnsi')
assert.ok(comoTexto(canhotoLongo.bytes).includes('/Encoding /WinAnsiEncoding'), 'fontes com WinAnsiEncoding')
ok('acentos do português codificados em WinAnsi (Ã = \\303, É = \\311)')

// ------------------------------------------------------------------- rota
const conversaDaParada = (i) => ({
  id: `c${i}`,
  nome: `Cliente ${String(i).padStart(2, '0')}`,
  cliente: { endereco: `Rua das Flores, ${100 + i}, Vila Madalena`, telefone: `(11) 9${String(i).padStart(4, '0')}-0000` },
  pedido: pedido(`2026-02${String(i).padStart(2, '0')}`, [{ sku: 'LAS-CLA', qtd: 1, obs: '' }, { sku: 'RAV-LIM', qtd: 2, obs: 'Molho à parte' }], i % 2 ? 'j1' : 'j2'),
})
const viagem = (n) => ({ id: 'v1', modo: 'propria', chamado: null, paradas: Array.from({ length: n }, (_, i) => conversaDaParada(i + 1)) })
const dadosDaRota = (n) => ({
  viagem: viagem(n), janelas: JANELAS, agora: AGORA, constantes: CONSTANTES,
  enderecoDaCasa: catalogo.ENDERECO_DA_CASA, cardapio: CARDAPIO,
})

const rotaCurta = pdfDaRota(dadosDaRota(3))
const rotaLonga = pdfDaRota(dadosDaRota(14))
for (const [nome, rota] of [['rota de 3 paradas', rotaCurta], ['rota de 14 paradas', rotaLonga]]) {
  const texto = conferirEstrutura(rota.bytes, nome)
  const caixas = mediaBoxes(texto)
  assert.ok(caixas.length >= 1)
  for (const caixa of caixas) {
    assert.equal(caixa.largura, 595.28, `${nome}: largura A4 = 595,28 pt`)
    assert.equal(caixa.altura, 841.89, `${nome}: altura A4 = 841,89 pt`)
  }
  conferirMargens(rota, mm(18), nome)
  const lidos = textos(rota).join('\n')
  const posicoes = rota === rotaCurta ? [1, 2, 3] : Array.from({ length: 14 }, (_, i) => i + 1)
  const ordem = posicoes.map((i) => lidos.indexOf(`Cliente ${String(i).padStart(2, '0')}`))
  assert.ok(ordem.every((p) => p >= 0), `${nome}: todas as paradas`)
  assert.ok(ordem.every((p, i) => i === 0 || p > ordem[i - 1]), `${nome}: na ordem da viagem`)
  assert.ok(lidos.includes(`Página 1 de ${caixas.length}`) && lidos.includes(`Página ${caixas.length} de ${caixas.length}`), `${nome}: rodapé paginado`)
  ok(`${nome}: ${caixas.length} página(s), todas com MediaBox 595.28 x 841.89 pt = A4 (210 x 297 mm), paradas na ordem, rodapé "Página N de ${caixas.length}"`)
}
assert.ok(mediaBoxes(comoTexto(rotaLonga.bytes)).length > 1, 'rota longa quebra página')
const daRota = textos(rotaCurta).join('\n')
for (const esperado of ['Rota da viagem', '3 paradas', 'Saída prevista', 'Eu mesma levo', 'Sai de Rua Girassol, 200',
  'Rua das Flores, 101, Vila Madalena', 'Janela 11h30 às 12h30', 'Pedido 0201', 'Tel. (11) 90001-0000', 'Chegada',
  '1× Lasanha clássica', '2× Ravióli de limão siciliano (Molho à parte)']) {
  assert.ok(daRota.includes(esperado), `rota traz "${esperado}"`)
}
// A parada nunca parte entre páginas: nome e endereço da mesma parada na mesma folha.
for (const pagina of rotaLonga.paginas) {
  const nela = pagina.desenhos.filter((d) => d.tipo === 'texto').map((d) => d.texto).join('\n')
  for (const m of nela.matchAll(/Cliente (\d\d)/g)) {
    assert.ok(nela.includes(`Rua das Flores, ${100 + Number(m[1])},`), `parada ${m[1]} inteira na mesma página`)
  }
}
assert.match(rotaCurta.nomeArquivo, /^rota-2026-09-22-v1\.pdf$/)
ok('rota traz saída, quem leva, endereço, janela, pedido, telefone, chegada e itens; parada nunca parte entre páginas; arquivo rota-2026-09-22-v1.pdf')

// ------------------------------------------------------ causa do "não funciona"
// Nenhuma folha de estilo pode esconder `body *` na impressão: era isso que
// deixava toda impressão do app em branco (ficha.module.css, registro 93).
const css = []
const varrer = (dir) => readdirSync(dir).forEach((nome) => {
  const caminho = join(dir, nome)
  if (statSync(caminho).isDirectory()) varrer(caminho)
  else if (caminho.endsWith('.css')) css.push([caminho, readFileSync(caminho, 'utf8')])
})
varrer(new URL('../src', import.meta.url).pathname.replace(/^\/(\w:)/, '$1'))
const culpados = css.filter(([, texto]) => /body\s+\*\s*\{[^}]*visibility:\s*hidden/.test(texto)).map(([c]) => c)
assert.deepEqual(culpados, [], 'nenhum CSS esconde body * na impressão')
ok(`nenhum dos ${css.length} CSS esconde "body *" na impressão (causa da impressão em branco)`)

console.log(`\n${passos} passos verdes.`)
