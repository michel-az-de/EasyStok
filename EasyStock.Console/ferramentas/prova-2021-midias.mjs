// Regressões de leitura de mídia da #2021. Executa os hooks reais; apenas hooks React
// e APIs de mídia do navegador são substituídos, sem microfone nem envio externo.
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { test } from 'node:test'

let hospedeiro
const hooks = {
  useState(inicial) {
    const host = hospedeiro, indice = host.estados.length
    host.estados.push(inicial)
    return [inicial, (valor) => { host.estados[indice] = valor }]
  },
  useRef: (current) => ({ current }),
  useCallback: (fn) => fn,
  useEffect(fn) { hospedeiro.limpezas.push(fn()) },
}
globalThis.__prova2021Midias = hooks
registerHooks({
  resolve(nome, contexto, proximo) {
    if (nome === 'react' && /\/hooks\/use(?:GravadorAudio|ArquivoComoDataUrl)\.js$/.test(contexto.parentURL ?? '')) {
      const codigo = 'const h=globalThis.__prova2021Midias; export const useState=h.useState,useRef=h.useRef,useCallback=h.useCallback,useEffect=h.useEffect;'
      return { url: 'data:text/javascript,' + encodeURIComponent(codigo), shortCircuit: true }
    }
    return proximo(nome, contexto)
  },
})
const { useGravadorAudio } = await import('../src/hooks/useGravadorAudio.js')
const { useArquivoComoDataUrl } = await import('../src/hooks/useArquivoComoDataUrl.js')

function montar(hook) {
  const host = { estados: [], limpezas: [] }
  hospedeiro = host
  host.hook = hook()
  hospedeiro = null
  host.desmontar = () => host.limpezas.forEach((limpar) => limpar?.())
  return host
}
let leitores, trilhasParadas, erroSincrono
class Leitor {
  constructor() { leitores.push(this) }
  readAsDataURL() { if (erroSincrono) throw new Error('Arquivo indisponível') }
  concluir() { this.result = 'data:audio/webm;base64,YQ=='; this.onload?.() }
  falhar() { this.error = new DOMException('Falha de leitura', 'NotReadableError'); this.onerror?.() }
  abortar() { this.onabort?.() }
}
class Gravador {
  constructor() { this.state = 'inactive'; this.mimeType = 'audio/webm' }
  start() { this.state = 'recording' }
  stop() { this.state = 'inactive'; queueMicrotask(() => this.onstop?.()) }
}
function preparar() {
  leitores = []; trilhasParadas = 0; erroSincrono = false
  globalThis.FileReader = Leitor
  globalThis.MediaRecorder = Gravador
  Object.defineProperty(globalThis, 'navigator', { configurable: true, value: {
    mediaDevices: { getUserMedia: async () => ({ getTracks: () => [{ stop() { trilhasParadas++ } }] }) },
  } })
}
async function resultadoLimitado(promessa) {
  let prazo
  try {
    return await Promise.race([
      promessa.then((valor) => ({ tipo: 'sucesso', valor }), (erro) => ({ tipo: 'erro', erro })),
      new Promise((resolve) => { prazo = setTimeout(() => resolve({ tipo: 'pendente' }), 50) }),
    ])
  } finally { clearTimeout(prazo) }
}

for (const evento of ['falhar', 'abortar']) {
  await test(`áudio: ${evento} a leitura encerra o envio e solta o microfone`, async () => {
    preparar()
    const host = montar(useGravadorAudio)
    try {
      await host.hook.iniciar()
      const resultado = resultadoLimitado(host.hook.finalizar())
      await Promise.resolve()
      leitores[0][evento]()
      assert.equal((await resultado).tipo, 'erro', 'o envio precisa falhar, nunca ficar pendente')
      assert.equal(host.estados[0], 'ocioso')
      assert.equal(host.estados[1], 0)
      assert.equal(trilhasParadas, 1)
    } finally { host.desmontar() }
  })
}
await test('áudio: conversão bem-sucedida conserva o resultado e libera o microfone', async () => {
  preparar()
  const host = montar(useGravadorAudio)
  try {
    await host.hook.iniciar()
    const resultado = resultadoLimitado(host.hook.finalizar())
    await Promise.resolve()
    leitores[0].concluir()
    const final = await resultado
    assert.equal(final.tipo, 'sucesso')
    assert.equal(final.valor.dataUrl, leitores[0].result)
    assert.ok(final.valor.duracaoMs >= 0)
    assert.equal(host.estados[0], 'ocioso')
    assert.equal(trilhasParadas, 1)
  } finally { host.desmontar() }
})
for (const evento of ['falhar', 'abortar']) {
  await test(`arquivo: ${evento} a leitura rejeita e encerra carregando`, async () => {
    preparar()
    const host = montar(useArquivoComoDataUrl)
    const resultado = resultadoLimitado(host.hook.ler(new Blob(['foto'])))
    leitores[0][evento]()
    assert.equal((await resultado).tipo, 'erro')
    assert.equal(host.estados[0], false)
  })
}
await test('arquivo: exceção ao começar a leitura encerra carregando', async () => {
  preparar(); erroSincrono = true
  const host = montar(useArquivoComoDataUrl)
  const resultado = await resultadoLimitado(host.hook.ler(new Blob(['foto'])))
  assert.equal(resultado.tipo, 'erro')
  assert.equal(host.estados[0], false)
})
await test('arquivo: leitura bem-sucedida conserva a data URL', async () => {
  preparar()
  const host = montar(useArquivoComoDataUrl)
  const resultado = resultadoLimitado(host.hook.ler(new Blob(['foto'])))
  assert.equal(host.estados[0], true)
  leitores[0].concluir()
  assert.deepEqual(await resultado, { tipo: 'sucesso', valor: leitores[0].result })
  assert.equal(host.estados[0], false)
})
