// Auxiliar das provas de bundle (#2391). Desde a #2254 o build não é mais um html único: o código sai em
// `assets/*.js`. O Rollup ainda emite como arquivo solto o chunk que só um `import()` morto alcançaria
// (a demonstração, no modo API), sem que nada o referencie. O que importa para "vai para a produção" é o que
// o navegador pode carregar: o html mais os chunks alcançados a partir dele, por referência no código.
import { build } from 'vite'

/** Compila em memória (`write: false`) e devolve o texto de tudo que o navegador consegue alcançar. */
export async function compilarAlcancavel({ raiz, api, minify = false }) {
  const anterior = process.env.VITE_FONTE_DADOS
  if (api) process.env.VITE_FONTE_DADOS = 'api'
  else delete process.env.VITE_FONTE_DADOS
  try {
    const saida = await build({ root: raiz, logLevel: 'silent', build: { write: false, minify, reportCompressedSize: false } })
    const itens = (Array.isArray(saida) ? saida[0] : saida).output
    const texto = (item) => String(item.code ?? item.source ?? '')
    const nome = (item) => item.fileName.split('/').pop()
    const alcancados = new Set(itens.filter((item) => item.fileName === 'index.html'))
    const fila = [...alcancados]
    while (fila.length > 0) {
      const atual = texto(fila.pop())
      for (const item of itens) {
        if (item.type === 'chunk' && !alcancados.has(item) && atual.includes(nome(item))) {
          alcancados.add(item)
          fila.push(item)
        }
      }
    }
    return [...alcancados].map(texto).join('\n')
  } finally {
    if (anterior === undefined) delete process.env.VITE_FONTE_DADOS
    else process.env.VITE_FONTE_DADOS = anterior
  }
}
