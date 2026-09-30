import { useCallback, useEffect, useRef, useState } from 'react'
import { listarPedidosKds, mudarStatusKds, obterCanhotoHtml, reimprimirCanhoto } from '../infra/api/kdsApi'
import { conectarEventosOperacao } from '../infra/api/eventosOperacao'

// Cozinha no modo API (F05). A fila vem do KDS sobre Pedido (S19) e recarrega a
// cada evento de pedido do SSE de operação (S18). Com o SSE caído, a tela não
// para: busca a fila a cada 15 s e tenta reconectar a cada 10 s.
const ESPERA_RECARGA_MS = 300
const POLLING_SEM_SSE_MS = 15000
const RECONECTAR_MS = 10000
const RECARREGA_COM = (evento) => evento === 'ready' || evento.startsWith('pedido.')

export function useCozinhaApi() {
  const [pedidos, setPedidos] = useState(null)
  const [erro, setErro] = useState(null)
  const [aoVivo, setAoVivo] = useState(false)
  const [movendo, setMovendo] = useState(() => new Set())
  const vivoRef = useRef(true)

  const recarregar = useCallback(() => listarPedidosKds()
    .then((lista) => {
      if (!vivoRef.current) return
      setPedidos(lista ?? [])
      setErro(null)
    })
    .catch((e) => { if (vivoRef.current) setErro(`A fila não carregou: ${e.message}`) }), [])

  useEffect(() => {
    vivoRef.current = true
    let fechar = () => {}
    let espera = null
    let reconexao = null
    let polling = null

    const agendarRecarga = () => {
      clearTimeout(espera)
      espera = setTimeout(recarregar, ESPERA_RECARGA_MS)
    }
    const conectar = () => {
      fechar = conectarEventosOperacao({
        aoEvento: ({ evento }) => {
          if (evento === 'ready') {
            setAoVivo(true)
            clearInterval(polling)
            polling = null
          }
          if (RECARREGA_COM(evento)) agendarRecarga()
        },
        aoCair: () => {
          if (!vivoRef.current) return
          setAoVivo(false)
          if (!polling) polling = setInterval(recarregar, POLLING_SEM_SSE_MS)
          reconexao = setTimeout(conectar, RECONECTAR_MS)
        },
      })
    }

    recarregar()
    conectar()
    return () => {
      vivoRef.current = false
      fechar()
      clearTimeout(espera)
      clearTimeout(reconexao)
      clearInterval(polling)
    }
  }, [recarregar])

  // Um toque: a chamada vai, o cartão fica "movendo" até a API responder; a
  // lista nova chega pela resposta e pelo `pedido.mudou_status` do SSE.
  const avancar = useCallback((id, status) => {
    setMovendo((s) => new Set(s).add(id))
    mudarStatusKds(id, status)
      .then(() => recarregar())
      .catch((e) => setErro(`O pedido não mudou: ${e.message}`))
      .finally(() => setMovendo((s) => { const n = new Set(s); n.delete(id); return n }))
  }, [recarregar])

  // S20: abre o canhoto HTML numa janela e chama a impressão do navegador.
  const imprimirCanhoto = useCallback((id) => {
    const janela = window.open('', '_blank')
    obterCanhotoHtml(id)
      .then((html) => {
        if (!janela) return setErro('O navegador bloqueou a janela do canhoto.')
        janela.document.open()
        janela.document.write(html)
        janela.document.close()
        janela.focus()
        janela.print()
      })
      .catch((e) => {
        janela?.close()
        setErro(e.message)
      })
  }, [])

  const reimprimir = useCallback((id) => {
    reimprimirCanhoto(id).catch((e) => setErro(`Não voltou para a fila de impressão: ${e.message}`))
  }, [])

  return { pedidos, erro, aoVivo, movendo, avancar, imprimirCanhoto, reimprimir, limparErro: () => setErro(null) }
}
