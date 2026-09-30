// Sincronia de estado entre a janela do Balcão e a janela de Entregas
// (rodada 5, seção 6). A janela do Balcão é a DONA do estado: quem abre
// `#/entregas` numa janela própria (`window.open`) só enxerga um espelho, e
// toda ação que essa janela dispara viaja de volta para o Balcão aplicar de
// verdade. Um estado só evita duas verdades.
//
// `BroadcastChannel` existe em todo navegador moderno; sem ele (aba isolada
// pelo navegador) a janela de Entregas fica sem estado e mostra a própria
// faixa de aviso (seção 6) — nunca finge um estado que não chegou.

const NOME_DO_CANAL = 'cdb-estado-v1'
const ATRASO_DE_ENVIO_MS = 100

const canalDisponivel = () => typeof window !== 'undefined' && 'BroadcastChannel' in window

// Modo "principal" (a janela do Balcão, em `AtendimentoProvider.jsx`): ouve
// `pedir-estado` e `acao`, manda `estado`. `estadoAtual` é lido na hora de
// responder, nunca guardado em closure velha; `aplicarAcao` é o `despachar`
// do reducer.
export function conectarPrincipal({ estadoAtual, aplicarAcao }) {
  if (!canalDisponivel()) return { mandarEstado() {}, fechar() {} }
  const canal = new BroadcastChannel(NOME_DO_CANAL)
  let pendente = null

  function mandarEstado() {
    if (pendente) return
    // Agrupado em 100 ms (seção 6): várias ações seguidas mandam um estado
    // só, não um por despacho.
    pendente = setTimeout(() => {
      pendente = null
      canal.postMessage({ tipo: 'estado', estado: estadoAtual() })
    }, ATRASO_DE_ENVIO_MS)
  }

  canal.onmessage = (evento) => {
    const mensagem = evento.data ?? {}
    if (mensagem.tipo === 'pedir-estado') mandarEstado()
    if (mensagem.tipo === 'acao') aplicarAcao(mensagem.acao)
  }

  return {
    mandarEstado,
    fechar() {
      if (pendente) clearTimeout(pendente)
      canal.close()
    },
  }
}

// Modo "espelho" (a janela de Entregas, `TelaEntregas.jsx`): pede o estado ao
// abrir, recebe `estado` toda vez que o Balcão muda, e manda `acao` de volta
// para o Balcão aplicar; `selecionar` é o caso especial "Abrir conversa", que
// também foca a janela de origem.
export function conectarEspelho({ aoReceberEstado }) {
  if (!canalDisponivel()) {
    return { despachar() {}, selecionar() {}, fechar() {} }
  }
  const canal = new BroadcastChannel(NOME_DO_CANAL)
  canal.onmessage = (evento) => {
    if (evento.data?.tipo === 'estado') aoReceberEstado(evento.data.estado)
  }
  canal.postMessage({ tipo: 'pedir-estado' })

  return {
    despachar(acao) {
      canal.postMessage({ tipo: 'acao', acao })
    },
    selecionar(id) {
      canal.postMessage({ tipo: 'selecionar', id })
      window.opener?.focus()
    },
    fechar() {
      canal.close()
    },
  }
}
