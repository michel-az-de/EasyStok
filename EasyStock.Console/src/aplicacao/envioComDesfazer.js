// #1686 (item 5): Cozinha e Entregas avisam o cliente no mesmo toque que muda a etapa, e a API não
// tem volta de etapa. O toque agenda o envio e a tela mostra "Desfazer" por alguns segundos;
// desfazer cancela antes de qualquer coisa sair. Sem desfazer, o envio sai sozinho no fim do prazo.
//
// `agendar` devolve a promessa do envio (o que `enviar` devolver) ou `{ desfeito: true }`: quem
// esperava a resposta da API (a pergunta do saldo a receber, por exemplo) continua esperando igual.
// Sair da tela com envio pendente (`descarregar`) manda na hora: ela já decidiu, só não esperou.
export const SEGUNDOS_PARA_DESFAZER_ETAPA = 5

const RELOGIO = { agendar: (fn, ms) => setTimeout(fn, ms), cancelar: (t) => clearTimeout(t), agora: () => Date.now() }

export function criarEnvioComDesfazer({ prazoMs = SEGUNDOS_PARA_DESFAZER_ETAPA * 1000, aoMudar = null, relogio = RELOGIO } = {}) {
  const pendentes = new Map()
  const lista = () => [...pendentes.entries()].map(([chave, p]) => ({ chave, ate: p.ate, dados: p.dados }))
  const avisar = () => aoMudar?.(lista())

  function sair(chave) {
    const p = pendentes.get(chave)
    if (!p) return null
    relogio.cancelar(p.timer)
    pendentes.delete(chave)
    return p
  }

  return {
    agendar(chave, enviar, dados = null) {
      const existente = pendentes.get(chave)
      if (existente) return existente.promessa
      let resolver
      const promessa = new Promise((r) => { resolver = r })
      const timer = relogio.agendar(() => {
        const p = sair(chave)
        avisar()
        if (p) p.resolver(Promise.resolve().then(enviar))
      }, prazoMs)
      pendentes.set(chave, { timer, enviar, dados, resolver, promessa, ate: relogio.agora() + prazoMs })
      avisar()
      return promessa
    },
    desfazer(chave) {
      const p = sair(chave)
      if (!p) return false
      avisar()
      p.resolver({ desfeito: true })
      return true
    },
    descarregar() {
      const todos = [...pendentes.keys()].map(sair).filter(Boolean)
      if (todos.length) avisar()
      for (const p of todos) p.resolver(Promise.resolve().then(p.enviar))
    },
    pendentes: lista,
  }
}

// #2005 (estoque-10): o pendente vive na memória da aba. Com a aba fechada, recarregada, em crash ou
// suspensa pelo tablet o cleanup do React não roda e o "Pronto"/"Saiu"/"Entregue" que a cozinheira tocou
// nunca chegava ao EasyStok (sem baixa de estoque, sem aviso ao cliente). `pagehide` cobre fechar e
// recarregar; `visibilitychange` para escondida cobre o tablet que suspende (no celular o `pagehide` nem sempre
// vem). A requisição sai com `keepalive` (kdsApi, entregasApi), que sobrevive à aba. Devolve a função que desliga.
export function enviarPendentesAoSair(envio, { janela = globalThis.window, documento = globalThis.document } = {}) {
  if (!janela || !documento) return () => {}
  const sair = () => envio.descarregar()
  const aoMudarVisibilidade = () => { if (documento.visibilityState === 'hidden') sair() }
  janela.addEventListener('pagehide', sair)
  documento.addEventListener('visibilitychange', aoMudarVisibilidade)
  return () => {
    janela.removeEventListener('pagehide', sair)
    documento.removeEventListener('visibilitychange', aoMudarVisibilidade)
  }
}

// Entregas: a saída da viagem e a entrega de cada parada avisam o cliente.
export const chaveDaSaida = (viagemId) => `sair:${viagemId}`
export const chaveDaEntrega = (viagemId, pedidoId) => `entregue:${viagemId}:${pedidoId}`

// Segundos inteiros que faltam, para o rótulo "Desfazer · 4".
export const segundosAte = (ate, agora) => Math.max(0, Math.ceil((ate - agora) / 1000))
