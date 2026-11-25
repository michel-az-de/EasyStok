/* eslint-disable no-console */
// Prova da #3318: a barra da conversa aberta mostra o mesmo motivo de fila do cartão (`motivoDePrecisar`), também com o
// Assistente pausado. Em produção (25/11) o cartão dizia "Esperando você" e a barra só "Com <atendente>" e "Assistente pausado.".
import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { transformWithOxc } from 'vite'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { assert, caso, fim } from './prova-2944-base.mjs'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map()
for (const arquivo of [
  ...readdirSync(join(raiz, 'componentes')).filter(n => n.endsWith('.jsx')).map(n => join(raiz, 'componentes', n)),
  join(raiz, 'features/atendimento/BarraModo.jsx'),
]) {
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env.DEV', 'false')
  jsx.set(pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code)
}
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(spec + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})

const { resumoDoModo } = await import('../src/dominio/resumoDoModo.js')
const automatico = await import('../src/dominio/automatico.js')
const { motivoDePrecisar, PAUSA_POR_OUTRO } = automatico
const { AUTOMATICO_DESLIGADO } = await import('../src/dominio/motivoDeEscalada.js')
const { BarraModo } = await import('../src/features/atendimento/BarraModo.jsx')

const agora = new Date('2026-11-25T10:00:00-03:00').getTime()
const iso = (m) => new Date(agora - m * 60_000).toISOString()
const conversa = (mensagens, extra = {}) => ({
  id: 'x1', nome: 'Cliente', canal: 'WhatsApp', estado: 'Aberto', responsavel: null, passagem: null,
  cliente: { tags: [], notas: [] }, pedido: null, mensagens, ...extra,
})
// Cliente falou por último há 2 h e ninguém respondeu: o cartão sobe "Esperando você" (SLA estourado).
const esperando = conversa([
  { id: 'm0', dir: 'out', texto: 'Bom dia! Como posso ajudar?', em: iso(180) },
  { id: 'm1', dir: 'in', texto: 'Você me confirma o valor que eu faço o pix', em: iso(120) },
])
const comOutra = { ...esperando, responsavel: 'Thatiane' }
const barra = (c, pausado, motivo) => renderToStaticMarkup(createElement(BarraModo, {
  conversa: c, pausado, agora, motivo, acoes: {}, aoAssumir: () => {},
}))
const linhaVisivel = (html, texto) => new RegExp(`<p class="modoTexto"[^>]*>${texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}</p>`).test(html)

await caso('pausado por mim: a linha da barra é o motivo do cartão, no tom dele, e o rótulo fica', () => {
  const motivo = motivoDePrecisar(esperando, agora, true)
  assert.equal(motivo?.chave, 'esperando', 'o cartão diz "Esperando você"')
  const r = resumoDoModo(esperando, true, false, { agora, motivo })
  assert.equal(r.linha, motivo.texto)
  assert.equal(r.tom, motivo.tom)
  assert.equal(r.rotulo, 'Você respondendo')
  assert.equal(r.precisa, true)
  assert.ok(r.detalhe.startsWith(motivo.texto), 'o title e o leitor de tela começam pelo motivo')
})

await caso('pausado por outra atendente: "Com Thatiane" e a linha com o motivo, à vista no DOM', () => {
  const motivo = motivoDePrecisar(comOutra, agora, PAUSA_POR_OUTRO)
  const r = resumoDoModo(comOutra, PAUSA_POR_OUTRO, false, { agora, motivo })
  assert.equal(r.rotulo, 'Com Thatiane')
  assert.equal(r.linha, motivo.texto)
  assert.ok(linhaVisivel(barra(comOutra, PAUSA_POR_OUTRO, motivo), motivo.texto), 'a linha não pode ficar só para o leitor de tela (classe sr)')
})

await caso('cobrança vencida sai neutra na barra, como no cartão', () => {
  const motivo = { chave: 'cobranca', rotulo: 'Cobrança vencida', tom: 'perigo', texto: 'Cobrança do pedido 11 venceu' }
  assert.equal(resumoDoModo(esperando, true, false, { agora, motivo }).tom, 'neutro')
  assert.equal(automatico.tomDoMotivo(motivo), 'neutro')
})

await caso('ligado com motivo do cartão: "Precisa de você" e a mesma linha', () => {
  const pergunta = conversa([{ id: 'm1', dir: 'in', texto: 'Vocês entregam hoje?', em: iso(120) }])
  const motivo = motivoDePrecisar(pergunta, agora, false)
  assert.ok(motivo, 'o cartão sobe')
  const r = resumoDoModo(pergunta, false, false, { agora, motivo })
  assert.equal(r.rotulo, 'Precisa de você')
  assert.equal(r.linha, motivo.texto)
})

await caso('o motivo vence "Assistente desligado" na barra', () => {
  const desligado = { ...esperando, mensagens: [
    { id: 's0', dir: 'sistema', texto: `Passou para você: ${AUTOMATICO_DESLIGADO}`, em: iso(200) }, ...esperando.mensagens,
  ] }
  const motivo = motivoDePrecisar(desligado, agora, true)
  assert.ok(motivo, 'o cartão sobe')
  const html = barra(desligado, true, motivo)
  assert.ok(linhaVisivel(html, motivo.texto), html)
})

await caso('sem motivo nada muda', () => {
  const r = resumoDoModo(esperando, true, false, { agora, motivo: null })
  assert.equal(r.precisa, false)
  assert.notEqual(r.linha, motivoDePrecisar(esperando, agora, true).texto)
})

await caso('encerrada com motivo não muda', () => {
  const enc = { ...esperando, estado: 'Encerrado' }
  const motivo = { chave: 'esperando', tom: 'perigo', texto: 'x' }
  const r = resumoDoModo(enc, false, false, { agora, motivo })
  assert.notEqual(r.linha, 'x')
  assert.equal(r.precisa, false)
})

await caso('bloqueada e "passou para você" com motivo não mudam', () => {
  const motivo = { chave: 'esperando', tom: 'perigo', texto: 'x' }
  const bloq = resumoDoModo(esperando, true, true, { agora, motivo })
  assert.equal(bloq.precisa, false)
  assert.notEqual(bloq.linha, 'x')
  const passou = { ...esperando, passagem: { motivo: 'Cliente pediu a dona', em: iso(30) } }
  const comVoce = resumoDoModo(passou, false, false, { agora, motivo })
  assert.equal(comVoce.precisa, false)
  assert.notEqual(comVoce.linha, 'x')
})

fim()
