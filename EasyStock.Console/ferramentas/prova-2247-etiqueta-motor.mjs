/* eslint-disable no-console */
// Prova da #2247 (impressão v3, G1 item 7): o Console desenha a etiqueta de produção com o motor do
// app. Aqui ficam as regras puras: os campos que o Console entrega ao motor (os mesmos termos do
// app) e a leitura do que o motor devolveu, que recusa a etiqueta sem a declaração de alérgenos.
// A cópia do motor é conferida em tests/pwa/impressao-v3-motor-console.test.js.
//
//   node ferramentas/prova-2247-etiqueta-motor.mjs

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const dominio = await import('../src/dominio/etiquetaDeProducao.js')
const { camposDaEtiqueta, codigoDaEtiqueta, textoDosAlergenos, lerDesenho, PAPEL_DA_PRODUCAO } = dominio

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

const ETIQUETA = {
  id: 'e-1', sequencial: 1, codigo: 'LOT-261026-001-0001', nome: 'Lasanha Bolonhesa 1 kg', pesoG: 1000,
  alergenos: [], lote: 'LOT-261026-001', produzidoEm: '2026-10-27T02:30:00Z', validadeEm: '2026-10-31T23:06:08Z',
}

await confere('os campos são os do app: datas no fuso da loja, peso como o app escreve e o QR do código', () => {
  assert.deepEqual(camposDaEtiqueta({ etiqueta: ETIQUETA, empresa: 'Casa da Baba' }), {
    empresa: 'Casa da Baba', nome: 'Lasanha Bolonhesa 1 kg', validade: '31/10/2026', fabricacao: '26/10/2026',
    lote: 'LOT-261026-001', peso: 'PESO 1,00kg', nota: '', qr: 'LOT-261026-001-0001',
  })
  assert.equal(camposDaEtiqueta({ etiqueta: { ...ETIQUETA, pesoG: 350 }, empresa: '' }).peso, 'PESO 350g')
  assert.equal(camposDaEtiqueta({ etiqueta: { ...ETIQUETA, pesoG: null }, empresa: '' }).peso, '')
})

await confere('sem Code 128: o Console não tem a chave curta, sai só o QR, como no app (D4)', () => {
  assert.equal('barras' in camposDaEtiqueta({ etiqueta: ETIQUETA, empresa: '' }), false)
  assert.equal(PAPEL_DA_PRODUCAO, 'etiqueta-80x40')
})

await confere('os alérgenos da ficha vão na nota, em maiúsculas, como pede a RDC 26/2015', () => {
  assert.equal(textoDosAlergenos(['gluten', 'lactose', 'ovo']), 'ALÉRGICOS: CONTÉM GLÚTEN, LACTOSE E OVO.')
  assert.equal(textoDosAlergenos([]), null)
  assert.equal(camposDaEtiqueta({ etiqueta: { ...ETIQUETA, alergenos: ['soja'] }, empresa: '' }).nota, 'ALÉRGICOS: CONTÉM SOJA.')
})

await confere('o QR leva o código saneado, sem acento e só ASCII visível', () => {
  assert.equal(codigoDaEtiqueta('LOTE-ÇÃO-1\n'), 'LOTE-CAO-1')
  assert.equal(camposDaEtiqueta({ etiqueta: { ...ETIQUETA, codigo: 'Lá-1' }, empresa: '' }).qr, 'La-1')
})

const canvas = { width: 640, height: 320, toDataURL: () => 'data:image/png;base64,AAA' }

await confere('a prévia é o PNG do motor no tamanho do papel, em mm', () => {
  const campos = camposDaEtiqueta({ etiqueta: ETIQUETA, empresa: 'Casa' })
  assert.deepEqual(lerDesenho(campos, { canvas, recusa: null, omitidos: [], avisos: [] }, 8), {
    png: 'data:image/png;base64,AAA', larguraMm: 80, alturaMm: 40, avisos: [], recusa: null,
  })
})

await confere('recusa do motor chega com o motivo, sem imagem', () => {
  const campos = camposDaEtiqueta({ etiqueta: ETIQUETA, empresa: 'Casa' })
  const r = lerDesenho(campos, { canvas: null, recusa: 'a fonte da etiqueta não carregou', omitidos: [] }, 8)
  assert.equal(r.png, null)
  assert.match(r.recusa, /fonte da etiqueta/)
})

await confere('alérgeno que não cabe recusa a etiqueta: comida não sai sem a declaração', () => {
  const campos = camposDaEtiqueta({ etiqueta: { ...ETIQUETA, alergenos: ['gluten'] }, empresa: 'Casa' })
  const r = lerDesenho(campos, { canvas, recusa: null, omitidos: ['nota'], avisos: [] }, 8)
  assert.equal(r.png, null)
  assert.match(r.recusa, /alérgenos/)
  // Sem alérgenos, a nota vazia omitida não importa.
  const sem = lerDesenho(camposDaEtiqueta({ etiqueta: ETIQUETA, empresa: 'Casa' }), { canvas, recusa: null, omitidos: ['nota'], avisos: [] }, 8)
  assert.equal(sem.recusa, null)
})

await confere('a etiqueta da tela é a imagem do motor; a pasta não tem mais QR nem barras em SVG', () => {
  const jsx = readFileSync(new URL('../src/features/gestao/producao/etiqueta/EtiquetaDeProducao.jsx', import.meta.url), 'utf8')
  assert.match(jsx, /<img/)
  assert.doesNotMatch(jsx, /<svg|matrizDoQr|barrasDoCode128/)
  const css = readFileSync(new URL('../src/features/gestao/producao/etiqueta/etiqueta.module.css', import.meta.url), 'utf8')
  assert.match(css, /size:\s*80mm 40mm/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
