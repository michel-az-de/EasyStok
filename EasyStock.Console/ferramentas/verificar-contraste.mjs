import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'

const raiz = resolve(import.meta.dirname, '../src')
const css = readFileSync(resolve(raiz, 'estilos/tokens.css'), 'utf8')
const tokens = Object.fromEntries([...css.matchAll(/--([\w-]+):\s*([^;]+);/g)].map((m) => [m[1], m[2]]))
function cor(nome, tema) {
  const valor = tokens[nome]
  assert.ok(valor, `Token ausente: ${nome}`)
  const referencia = /^var\(--([\w-]+)\)$/.exec(valor)
  if (referencia) return cor(referencia[1], tema)
  const cores = valor.match(/#[\da-f]{6}/gi)
  assert.ok(cores?.length, `Token sem cor RGB verificável: ${nome}`)
  return cores[tema] ?? cores[0]
}
function luminancia(hex) {
  return [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map((v) => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4)
    .reduce((s, v, i) => s + v * [.2126, .7152, .0722][i], 0)
}
let pares = 0
function conferir(frente, fundo, minimo = 4.5) {
  for (const tema of [0, 1]) {
    const [a, b] = [luminancia(cor(frente, tema)), luminancia(cor(fundo, tema))].sort((x, y) => y - x)
    const contraste = (a + .05) / (b + .05)
    assert.ok(contraste >= minimo, `${frente}/${fundo}, ${tema ? 'escuro' : 'claro'}: ${contraste.toFixed(2)} < ${minimo}`)
    pares++
  }
}
const superficies = ['fundo', 'papel', 'elevado', 'creme', 'marca-claro', 'comanda']
for (const texto of ['tinta', 'tinta-2', 'tinta-3', 'marca', 'sucesso', 'alerta', 'erro', 'info', 'c-whatsapp', 'c-instagram', 'c-messenger', 'c-site', 'c-email', 'c-sms']) {
  for (const fundo of superficies) conferir(texto, fundo)
}
for (const estado of ['sucesso', 'alerta', 'erro', 'info', 'entregue']) conferir(estado, `${estado}-pastel`)
for (const pastel of ['sucesso-pastel', 'alerta-pastel', 'erro-pastel', 'info-pastel']) {
  for (const texto of ['tinta', 'tinta-2', 'tinta-3', 'marca-forte']) conferir(texto, pastel)
}
for (const acao of ['acao', 'acao-forte']) conferir('sobre-acao', acao)
conferir('sobre-erro', 'erro')
for (const fundo of ['avatar-fundo', 'canal-whatsapp-fundo', 'canal-instagram-fundo', 'canal-site-fundo', 'canal-neutro-fundo', 'acao-sucesso']) conferir('branco', fundo)
for (const foco of ['marca', 'linha-forte', 'linha-media']) for (const fundo of superficies) conferir(foco, fundo, 3)

// Impede que um componente reintroduza uma cor ou um alias aposentado.
for (const arquivo of readdirSync(raiz, { recursive: true }).filter((f) => /\.(css|jsx)$/.test(f) && !f.endsWith('tokens.css'))) {
  const fonte = readFileSync(resolve(raiz, arquivo), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/[^\n]*/g, '')
  assert.ok(!/#[\da-f]{3,8}\b/i.test(fonte), `Cor fora dos tokens: ${arquivo}`)
  assert.ok(!/--(?:ragu|semola|ok|atencao|parado|aviso|perigo|t-\d+)\b/.test(fonte), `Alias aposentado: ${arquivo}`)
}
console.log(`Design system: ${pares} pares de contraste aprovados nos dois temas; cores e aliases conferidos.`)
