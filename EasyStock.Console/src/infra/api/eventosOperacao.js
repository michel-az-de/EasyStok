import { API_BASE } from '../fonteDados'
import { garantirSessao } from './cliente'
import { criarLeitorSse } from './leitorSse'
import { criarVigiaDeSilencio } from './vigiaDeSilencio'

// SSE de operação (S18): `GET api/operacao/eventos` com o JWT no header, lido por
// `fetch` em stream. Sem replay na API: quem assina recarrega a lista a cada `ready`.
// Devolve a função que fecha a conexão. `aoCair` avisa quando a conexão terminou
// (erro, 401/403, o servidor fechou ou ela calou além do prazo da vigia, #1577);
// reconectar é decisão de quem chamou.
export function conectarEventosOperacao({ aoEvento, aoCair }) {
  const controle = new AbortController()
  const vigia = criarVigiaDeSilencio({ aoCalar: () => { controle.abort(); aoCair() } })

  ;(async () => {
    try {
      // Conta desde já: conexão que nem chega ao `ready` também é silêncio.
      vigia.sinal()
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
        // Qualquer pedaço é sinal de vida, inclusive o `: heartbeat` que o leitor de quadros descarta.
        vigia.sinal()
        ler(value)
      }
      if (!controle.signal.aborted) aoCair()
    } catch {
      if (!controle.signal.aborted) aoCair()
    } finally {
      vigia.parar()
    }
  })()

  return () => {
    vigia.parar()
    controle.abort()
  }
}
