// Executa CampoFoto e seu hook reais, com JSX transformado pelo Vite já usado no projeto.
// Os hooks React e FileReader são controlados para provar falha e nova seleção sem navegador.
import assert from 'node:assert/strict'
import { test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { transformWithOxc } from 'vite'

const painel = new URL('../src/features/anexos/PainelGaleria.jsx', import.meta.url)
const fonte = readFileSync(painel, 'utf8') + '\nexport { CampoFoto }\n'
const compilado = (await transformWithOxc(fonte, painel.pathname, { jsx: { runtime: 'automatic' } })).code
let host
globalThis.__galeria2021 = {
  useState(inicial) {
    const atual = host, indice = atual.indice++
    if (!(indice in atual.estados)) atual.estados[indice] = inicial
    return [atual.estados[indice], (valor) => { atual.estados[indice] = valor }]
  },
  useCallback: (fn) => fn,
  useRef: (current) => ({ current }),
}
const substitutos = {
  '../../componentes/Botao': ['Botao'], '../../componentes/Campo': ['CampoArea', 'CampoTexto'],
  '../../componentes/Icone': ['Icone'], '../../componentes/Modal': ['Modal'], '../../componentes/Vazio': ['Vazio'],
  '../../aplicacao/contextos': ['useAcoes', 'useAtendimento', 'useCatalogo'],
  '../../dominio/canal': ['aceitaFormato', 'canalDaConversa', 'motivoDeFormato'],
  '../../dominio/janela': ['permissaoDeEscrita'],
}
registerHooks({
  resolve(nome, contexto, proximo) {
    if (nome === 'react' && (contexto.parentURL === painel.href || contexto.parentURL?.endsWith('/hooks/useArquivoComoDataUrl.js'))) {
      return { url: 'data:text/javascript,export const {useState,useRef,useCallback}=globalThis.__galeria2021', shortCircuit: true }
    }
    if (contexto.parentURL === painel.href && substitutos[nome]) {
      return { url: 'data:text/javascript,' + substitutos[nome].map((n) => `export const ${n}=()=>null;`).join(''), shortCircuit: true }
    }
    if (nome.endsWith('.module.css')) return { url: 'data:text/javascript,export default {}', shortCircuit: true }
    if (nome.startsWith('.') && !/\.[a-z]+$/i.test(nome)) return proximo(nome + '.js', contexto)
    return proximo(nome, contexto)
  },
  load(url, contexto, proximo) {
    if (url === painel.href) return { format: 'module', shortCircuit: true, source: compilado }
    return proximo(url, contexto)
  },
})
const { CampoFoto } = await import(painel.href)
function elementos(no) {
  if (!no || typeof no !== 'object') return []
  if (Array.isArray(no)) return no.flatMap(elementos)
  return [no, ...elementos(no.props?.children)]
}
function montar() {
  const atual = { estados: [], indice: 0, escolhidas: [], leitores: [] }
  globalThis.FileReader = class {
    constructor() { atual.leitores.push(this) }
    readAsDataURL() {}
  }
  atual.renderizar = () => {
    host = atual; atual.indice = 0
    return elementos(CampoFoto({ foto: atual.escolhidas.at(-1), aoEscolher: (url) => atual.escolhidas.push(url) }))
  }
  atual.campo = () => atual.renderizar().find((no) => no.type === 'input')
  atual.alerta = () => atual.renderizar().find((no) => no.props?.role === 'alert')
  atual.escolher = (arquivo = { type: 'image/png', size: 100 }) => atual.campo().props.onChange({ target: { files: [arquivo], value: 'foto.png' } })
  return atual
}

for (const evento of ['onerror', 'onabort']) {
  test(`galeria: ${evento} é anunciado e próxima seleção permite escolher a foto`, async () => {
    const atual = montar()
    const leitura = atual.escolher()
    const semRejeicao = assert.doesNotReject(leitura)
    atual.leitores[0].error = new DOMException('Falha ao ler', 'NotReadableError')
    atual.leitores[0][evento]()
    await semRejeicao
    assert.match(atual.alerta()?.props.children ?? '', /ler.*foto/i)
    assert.equal(atual.campo().props['aria-invalid'], true)
    assert.deepEqual(atual.escolhidas, [])
    const novaLeitura = atual.escolher()
    assert.equal(atual.alerta(), undefined, 'nova seleção limpa o erro anterior imediatamente')
    atual.leitores[1].result = 'data:image/png;base64,YQ=='
    atual.leitores[1].onload()
    await novaLeitura
    assert.deepEqual(atual.escolhidas, ['data:image/png;base64,YQ=='])
    assert.equal(atual.campo().props['aria-invalid'], false)
  })
}
test('galeria preserva a recusa de PDF e imagem acima de 8 MB sem tentar ler', async () => {
  const atual = montar()
  await atual.escolher({ type: 'application/pdf', size: 100 })
  await atual.escolher({ type: 'image/png', size: 8 * 1024 * 1024 + 1 })
  assert.equal(atual.leitores.length, 0)
  assert.deepEqual(atual.escolhidas, [])
})
