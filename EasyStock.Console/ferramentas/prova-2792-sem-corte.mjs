// Prova da #2792: nenhum campo do console corta o texto em silêncio e nenhum texto de tela usa travessão.
//
// - `maxLength` cortava o texto colado sem avisar (o motivo de um estorno ia pela metade). O limite agora é a prop
//   `limite` de CampoTexto e CampoArea, que mostra "Use até N caracteres. Agora tem M." e deixa o texto inteiro.
// - Travessão em texto de tela foge da regra de marca e o leitor de tela lê "travessão" no lugar do valor.
//
// Roda com: node ferramentas/prova-2792-sem-corte.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-2792-sem-corte.mjs

import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import assert from 'node:assert/strict'

const raiz = process.env.PROVA_SRC ? process.env.PROVA_SRC.replace(/[\\/]$/, '') : fileURLToPath(new URL('../src', import.meta.url))
const arquivos = (pasta) => readdirSync(pasta).flatMap((nome) => {
  const caminho = join(pasta, nome)
  return statSync(caminho).isDirectory() ? arquivos(caminho) : /\.(jsx?|mjs)$/.test(nome) && !/\.test\./.test(nome) ? [caminho] : []
})
const fontes = arquivos(raiz).map((caminho) => ({ caminho: relative(raiz, caminho).replaceAll('\\', '/'), texto: readFileSync(caminho, 'utf8') }))

// Tira comentários de linha e de bloco, para que a explicação de "por que sem maxLength" não conte.
const semComentario = (texto) => texto.replace(/\/\*[\s\S]*?\*\//g, (bloco) => bloco.replace(/[^\n]/g, '')).split(/\r?\n/).map((linha) => linha.replace(/(^|[^:'"`\\])\/\/.*$/, '$1')).join('\n')
const ocorrencias = (padrao, ignorar = () => false) => fontes.flatMap(({ caminho, texto }) =>
  semComentario(texto).split('\n').flatMap((linha, i) => padrao.test(linha) && !ignorar(linha) ? [`${caminho}:${i + 1}`] : []))

let ok = 0
const falhas = []
const caso = async (nome, fn) => {
  try { await fn(); ok++; console.log(`ok    ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA ${nome}\n      ${erro.message.split('\n')[0]}`) }
}

await caso('nenhum campo usa maxLength (o limite vira mensagem, o texto fica inteiro)', () => {
  const achados = ocorrencias(/\bmaxLength\s*[=:]/)
  assert.deepEqual(achados, [], `${achados.length} campo(s) com maxLength: ${achados.slice(0, 6).join(', ')}`)
})

await caso('nenhum texto de tela usa travessão', () => {
  // O separador de lista aceita o travessão que a pessoa digitar; isso é leitura, não texto de tela.
  // `infra/motor/` é cópia do motor de etiqueta da PWA (EasyStock.Api/wwwroot/pwa), conferida byte a byte pelo
  // teste de contrato: a etiqueta impressa muda lá, numa issue própria, não só na cópia.
  const achados = ocorrencias(/—/, (linha) => /const SEPARADOR = /.test(linha)).filter((onde) => !onde.startsWith('infra/motor/'))
  assert.deepEqual(achados, [], `${achados.length} travessão(ões): ${achados.slice(0, 6).join(', ')}`)
})

await caso('erroDoLimite diz o limite e o tamanho, e algumPassaDoLimite trava o envio', async () => {
  let modulo = {}
  try { modulo = await import(pathToFileURL(join(raiz, 'componentes/limiteDoCampo.js')).href) } catch { /* sem o módulo: falha abaixo */ }
  assert.equal(typeof modulo.erroDoLimite, 'function', 'componentes/limiteDoCampo.js não exporta erroDoLimite')
  assert.equal(modulo.erroDoLimite('abc', 3), null)
  assert.equal(modulo.erroDoLimite('abcd', 3), 'Use até 3 caracteres. Agora tem 4.')
  assert.equal(modulo.erroDoLimite('qualquer coisa', undefined), null)
  assert.equal(modulo.erroDoLimite(undefined, 3), null)
  assert.equal(modulo.algumPassaDoLimite(['ab', 3], ['abcd', 10]), false)
  assert.equal(modulo.algumPassaDoLimite(['ab', 3], ['abcd', 3]), true)
})

await caso('CampoTexto e CampoArea aceitam limite: aviso ligado ao campo e aria-invalid', () => {
  const campo = fontes.find((f) => f.caminho === 'componentes/Campo.jsx')?.texto ?? ''
  for (const nome of ['CampoTexto', 'CampoArea']) {
    const corpo = campo.slice(campo.indexOf(`export function ${nome}(`), campo.indexOf('</Envelope>', campo.indexOf(`export function ${nome}(`)))
    assert.match(corpo, /\blimite\b/, `${nome} não recebe limite`)
    assert.match(corpo, /erroDoLimite\(resto\.value, limite\)/, `${nome} não calcula o aviso do limite`)
    assert.match(corpo, /aria-invalid=\{erro \|\| passou \? true : undefined\}/, `${nome} não marca o campo acima do limite`)
  }
})

console.log(`\n${ok} ok, ${falhas.length} falharam`)
if (falhas.length) process.exit(1)
