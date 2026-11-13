// Serve o dist/ por HTTP para as provas que abrem o Console montado (#2254).
// Desde que o build tem JS em assets/ (módulo ES), file:// não carrega mais o app.
import { preview } from 'vite'

export async function servirDist(raiz) {
  const servidor = await preview({ root: raiz, configFile: false, logLevel: 'silent', preview: { port: 0, host: '127.0.0.1' } })
  const url = servidor.resolvedUrls.local[0]
  return { url, fechar: () => new Promise((ok) => servidor.httpServer.close(ok)) }
}
