// Prova da #2792 (frente F2), estática: lê o código-fonte, não abre navegador.
//  - Ficha do cliente: "Valor total" e "Costuma pedir" têm estado de carregando enquanto o dossiê não chega.
//  - Respostas prontas: Atalho (40) e Texto (4096) usam `limite`, entram no `pronto` e o atalho gerado é checado.
//  - Notas do cliente: sem o que abrir, o cabeçalho é título (h3), não botão inerte com aria-disabled.
//
// Roda com: node ferramentas/prova-2792-ficha-respostas.mjs
// Contra o master (Red): PROVA_SRC=<pasta src> node ferramentas/prova-2792-ficha-respostas.mjs

import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

// O src importa sem extensão, como o Vite aceita (mesmo hook da prova-1441).
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const raiz = process.env.PROVA_SRC ? process.env.PROVA_SRC.replace(/[\/]$/, '') : fileURLToPath(new URL('../src', import.meta.url))
const ler = (caminho) => readFileSync(join(raiz, caminho), 'utf8')
const semComentario = (texto) => texto.replace(/\/\*[\s\S]*?\*\//g, '').split(/\r?\n/).map((l) => l.replace(/(^|[^:'"`\\])\/\/.*$/, '$1')).join('\n')

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok    ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA ${nome}\n      ${erro.message.split('\n')[0]}`) }
}

const ficha = semComentario(ler('features/gestao/cadastros/FichaCliente.jsx'))
const editor = semComentario(ler('features/gestao/cadastros/EditorCadastro.jsx'))
const respostas = semComentario(ler('features/gestao/respostas/AbaRespostas.jsx'))
const notas = semComentario(ler('features/ficha-cliente/NotasDoCliente.jsx'))
let dominioRespostas = {}
try { dominioRespostas = await import(pathToFileURL(join(raiz, 'dominio/respostas.js')).href) } catch { /* falha no caso */ }

caso('o atalho gerado do título já cabe em 40, cortado em palavra inteira, e o curto não muda', () => {
  const { gerarAtalho, ATALHO_MAXIMO } = dominioRespostas
  assert.equal(ATALHO_MAXIMO, 40)
  assert.equal(gerarAtalho('Enviar cardápio'), '/enviar-cardapio')
  assert.equal(gerarAtalho('palavra '.repeat(8).trim()), '/palavra-palavra-palavra-palavra-palavra')
  assert.equal(gerarAtalho('a'.repeat(120)), '/' + 'a'.repeat(39), 'palavra única longa corta no teto')
  for (const titulo of ['x'.repeat(39), 'um dois tres quatro cinco seis sete oito nove dez onze', 'b'.repeat(40) + ' c']) {
    assert.ok(gerarAtalho(titulo).length <= 40, titulo)
  }
  assert.equal(gerarAtalho('/' + 'abc '.repeat(9) + 'abcde'), '/abc-abc-abc-abc-abc-abc-abc-abc-abc', 'palavra que termina no teto fica inteira')
})

caso('FichaCliente recebe carregandoRelacionamento e mostra "Carregando" no lugar de Valor total e Costuma pedir', () => {
  assert.match(ficha, /carregandoRelacionamento/, 'a ficha não recebe o estado de carregando do dossiê')
  assert.match(ficha, /carregandoRelacionamento[\s\S]{0,200}Carregando valor total/, 'sem a legenda de carregando')
})

caso('EditorCadastro só marca carregando até o primeiro dossiê (em releitura mantém o valor anterior)', () => {
  assert.match(editor, /carregandoRelacionamento=\{relacionamento\.carregando && !relacionamento\.dados\}/)
})

caso('Atalho e Texto usam limite 40 e 4096 e o Título migra para a mesma primitiva', () => {
  // O teto do atalho mora no domínio, junto do gerarAtalho que o respeita.
  assert.match(respostas, /import \{ ATALHO_MAXIMO, [^}]*\} from '[./]*dominio\/respostas'/)
  assert.match(respostas, /TEXTO_MAXIMO = 4096/)
  assert.match(respostas, /rotulo="Título"[^>]*limite=\{TITULO_MAXIMO\}/)
  assert.match(respostas, /rotulo="Atalho"[\s\S]{0,200}limite=\{ATALHO_MAXIMO\}/)
  assert.match(respostas, /rotulo="Texto"[^>]*limite=\{TEXTO_MAXIMO\}/)
})

caso('o salvar trava pelos três limites, e o botão diz o motivo', () => {
  assert.match(respostas, /algumPassaDoLimite\(\[titulo, TITULO_MAXIMO\], \[atalho, ATALHO_MAXIMO\], \[texto, TEXTO_MAXIMO\]\)/)
  assert.match(respostas, /pronto = [^\n]*!passaDoLimite/)
  assert.match(respostas, /motivo=\{/)
})

caso('a dica de variáveis do Texto continua à vista', () => {
  // #3010: a dica virou selos legíveis, logo abaixo do campo.
  assert.match(respostas, /rotulo="Texto"[\s\S]{0,200}<SelosDeVariaveis variaveis=\{VARIAVEIS_DA_API\} \/>/)
})

caso('cabeçalho Notas sem o que abrir é título (h3), sem aria-disabled nem botão inerte', () => {
  assert.doesNotMatch(notas, /'aria-disabled': true/, 'ainda há botão inerte com aria-disabled')
  assert.match(notas, /<h3 data-voltar-notas=\{conversaId\}/, 'o cabeçalho sem o que abrir não é h3')
  assert.match(notas, /podeAlternar\s*\?\s*<button[^>]*data-voltar-notas[\s\S]{0,200}aria-expanded=\{abertas\}/, 'com o que abrir, deve seguir botão com aria-expanded')
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
process.exit(falhas.length ? 1 : 0)
