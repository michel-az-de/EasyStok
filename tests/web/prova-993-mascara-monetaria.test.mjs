// #993: prova do valor monetário do web antigo (EasyStock.Web), sem navegador.
// Roda o próprio locale.js e o próprio masks.js num contexto vm com DOM mínimo.
// Uso: node --test tests/web/prova-993-mascara-monetaria.test.mjs
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import vm from 'node:vm'

const raiz = fileURLToPath(new URL('../../EasyStock.Web/wwwroot/js/', import.meta.url))

function contexto() {
  const ouvintes = {}
  const document = {
    activeElement: null,
    addEventListener(tipo, fn) { (ouvintes[tipo] ??= []).push(fn) },
    getElementById() { return null },
    querySelectorAll() { return [] },
  }
  class Event { constructor(type) { this.type = type } }
  const ctx = { document, Event, setTimeout, console }
  ctx.window = ctx
  vm.createContext(ctx)
  return { ctx, ouvintes }
}

function campo(valor, extra = {}) {
  const ouvintes = {}
  return {
    tagName: 'INPUT', type: 'text', inputMode: 'decimal', dataset: {},
    value: valor, selectionStart: valor.length, selectionEnd: valor.length,
    selecionado: false,
    setSelectionRange(a, b) { this.selectionStart = a; this.selectionEnd = b },
    select() { this.selecionado = true; this.selectionStart = 0; this.selectionEnd = this.value.length },
    addEventListener(tipo, fn) { (ouvintes[tipo] ??= []).push(fn) },
    disparar(tipo) { (ouvintes[tipo] ?? []).forEach(fn => fn({ type: tipo, target: this })) },
    dispatchEvent() {},
    ...extra,
  }
}

function colar(alvo, texto, inicio, fim = inicio) {
  const { ctx, ouvintes } = contexto()
  vm.runInContext(readFileSync(raiz + 'locale.js', 'utf8'), ctx)
  alvo.selectionStart = inicio
  alvo.selectionEnd = fim
  const evento = { target: alvo, clipboardData: { getData: () => texto }, preventDefault() {} }
  ouvintes.paste.forEach(fn => fn(evento))
  return alvo.value
}

test('colar dígito sem separador no fim preserva o decimal existente', () => {
  assert.equal(colar(campo('10.50'), '0', 5), '10.500')
})

test('colar dígitos sem separador no meio preserva o decimal existente', () => {
  assert.equal(colar(campo('1234.56'), '99', 7), '1234.5699')
})

test('colar dígito no início preserva o decimal existente', () => {
  assert.equal(colar(campo('99.99'), '1', 0), '199.99')
})

test('colar valor com separador continua sendo o novo decimal', () => {
  assert.equal(colar(campo('14.29'), '7,77', 0, 5), '7.77')
  assert.equal(colar(campo(''), '1.234,56', 0), '1234.56')
})

test('campo de moeda pré-preenchido seleciona tudo ao receber foco', () => {
  const { ctx } = contexto()
  vm.runInContext(readFileSync(raiz + 'masks.js', 'utf8'), ctx)
  const alvo = campo('R$ 14,29', { dataset: { mask: 'money' } })
  ctx.window.EasyMasks.apply(alvo, 'money')
  alvo.disparar('focus')
  assert.equal(alvo.selecionado, true, 'digitar por cima concatenaria: R$ 14,29 + 777 = R$ 14.297,77')
})

test('só o mouseup do clique que focou é cancelado; o seguinte posiciona o cursor', () => {
  const { ctx } = contexto()
  vm.runInContext(readFileSync(raiz + 'masks.js', 'utf8'), ctx)
  const ouvintes = {}
  const alvo = campo('R$ 14,29', {
    dataset: { mask: 'money' },
    addEventListener(tipo, fn) { (ouvintes[tipo] ??= []).push(fn) },
  })
  ctx.window.EasyMasks.apply(alvo, 'money')
  const mouseup = () => { let cancelado = false; ouvintes.mouseup.forEach(fn => fn({ preventDefault() { cancelado = true } })); return cancelado }
  ouvintes.focus.forEach(fn => fn({}))
  assert.equal(mouseup(), true)
  assert.equal(mouseup(), false)
})

test('campo de moeda vazio não seleciona no foco', () => {
  const { ctx } = contexto()
  vm.runInContext(readFileSync(raiz + 'masks.js', 'utf8'), ctx)
  const alvo = campo('', { dataset: { mask: 'money' } })
  ctx.window.EasyMasks.apply(alvo, 'money')
  alvo.disparar('focus')
  assert.equal(alvo.selecionado, false)
})
