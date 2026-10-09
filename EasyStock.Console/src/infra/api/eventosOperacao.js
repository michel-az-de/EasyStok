import { API_BASE } from '../fonteDados'
import { garantirSessao } from './cliente'
import { criarLeitorSse } from './leitorSse'

// SSE de operação (S18): `GET api/operacao/eventos` com o JWT no header, lido por
// `fetch` em stream. Sem replay na API: quem assina recarrega a lista a cada `ready`.
// Devolve a função que fecha a conexão. `aoCair` avisa quando a conexão terminou
// (erro, 401/403 ou o servidor fechou); reconectar é decisão de quem chamou.
export function conectarEventosOperacao({ aoEvento, aoCair }) {
  const controle = new AbortController()

  ;(async () => {
    try {
      const sessao = await garantirSessao()
      if (controle.signal.aborted) return
      const resposta = await fetch(`${API_BASE}/api/operacao/eventos`, {
        headers: { Accept: 'text/event-stream', ...(sessao ? { Authorization: `Bearer ${sessao.token}` } : {}) },
        signal: controle.signal,
      })
      if (!resposta.ok || !resposta.body) throw new Error(`HTTP ${resposta.status}`)
      const ler = criarLeitorSse(aoEvento)
      const leitor = resposta.body.pipeThrough(new TextDecoderStream()).getReader()
      for (;;) {
        const { value, done } = await leitor.read()
        if (done) break
        ler(value)
      }
      if (!controle.signal.aborted) aoCair()
    } catch {
      if (!controle.signal.aborted) aoCair()
    }
  })()

  return () => controle.abort()
}
