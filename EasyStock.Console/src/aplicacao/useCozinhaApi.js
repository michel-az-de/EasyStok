import { useCallback, useEffect, useRef, useState } from 'react'
import {
  confirmarImpressao, listarImpressoesPendentes, listarPedidosKds, mudarStatusKds, reimprimirCanhoto,
} from '../infra/api/kdsApi'
import { conectarEventosOperacao } from '../infra/api/eventosOperacao'
import { transicaoDaFila } from '../dominio/kds'

// Cozinha no modo API (F05). A fila vem do KDS sobre Pedido (S19) e recarrega a
// cada evento de pedido do SSE de operação (S18). Com o SSE caído, a tela não
// para: busca a fila a cada 15 s e tenta reconectar a cada 10 s.
//
// Issue #1446: a cada leitura, compara com a anterior (`transicaoDaFila`) e marca
// quem entrou, quem mudou de coluna e quem saiu, para a tela animar; e lê a fila de
// impressão da API (S20), que a aba da Cozinha consome como a gaveta do protótipo.
// O canhoto em si é o PDF de 80 mm do protótipo, montado na tela (`features/cozinha`).
const ESPERA_RECARGA_MS = 300
const POLLING_SEM_SSE_MS = 15000
const RECONECTAR_MS = 10000
const RECARREGA_COM = (evento) => evento === 'ready' || evento.startsWith('pedido.') || evento.startsWith('impressao.')

// Quanto o destaque de "chegou" e "mudou de coluna" fica no cartão, e quanto o cartão que
// saiu da fila (entregue, cancelado) fica na tela para a animação de saída.
const MS_DESTAQUE = 1800
const MS_SAIDA = 520

const comId = (conjunto, id, liga) => {
  const novo = new Set(conjunto)
  if (liga) novo.add(id)
  else novo.delete(id)
  return novo
}

// Geração da leitura (#1474): cada troca de dia abre uma geração nova; a resposta que chega de
// uma geração velha (Hoje respondendo depois de Amanhã) é descartada em vez de sobrescrever.
export function criarGeracao() {
  let atual = 0
  return {
    nova: () => { atual += 1; return atual },
    agora: () => atual,
    vale: (geracao) => geracao === atual,
  }
}

// `data` (YYYY-MM-DD ou null para hoje, #1474): o dia da fila. Trocar o dia relê e não anima
// a troca como se os pedidos tivessem entrado ou saído.
export function useCozinhaApi({ data = null } = {}) {
  const [geracao] = useState(criarGeracao)
  const [pedidos, setPedidos] = useState(null)
  const [erro, setErro] = useState(null)
  const [aoVivo, setAoVivo] = useState(false)
  const [movendo, setMovendo] = useState(() => new Set())
  const [novos, setNovos] = useState(() => new Set())
  const [mudaram, setMudaram] = useState(() => new Set())
  const [saindo, setSaindo] = useState([])
  const [impressoes, setImpressoes] = useState([])
  const vivoRef = useRef(true)
  const anteriorRef = useRef(null)
  const timersRef = useRef(new Set())

  const depois = useCallback((ms, fazer) => {
    const t = setTimeout(() => {
      timersRef.current.delete(t)
      if (vivoRef.current) fazer()
    }, ms)
    timersRef.current.add(t)
  }, [])

  const animar = useCallback(({ novos: entraram, mudaram: andaram, sairam }) => {
    if (entraram.length > 0) {
      setNovos((s) => new Set([...s, ...entraram]))
      depois(MS_DESTAQUE, () => setNovos((s) => new Set([...s].filter((id) => !entraram.includes(id)))))
    }
    if (andaram.length > 0) {
      setMudaram((s) => new Set([...s, ...andaram]))
      depois(MS_DESTAQUE, () => setMudaram((s) => new Set([...s].filter((id) => !andaram.includes(id)))))
    }
    if (sairam.length > 0) {
      const ids = sairam.map((p) => p.id)
      setSaindo((atual) => [...atual.filter((p) => !ids.includes(p.id)), ...sairam])
      depois(MS_SAIDA, () => setSaindo((atual) => atual.filter((p) => !ids.includes(p.id))))
    }
  }, [depois])

  const recarregarImpressoes = useCallback(() => listarImpressoesPendentes()
    .then((lista) => { if (vivoRef.current) setImpressoes(lista ?? []) })
    // A fila de impressão é extra: sem ela (perfil sem acesso, API fora) a cozinha segue.
    .catch(() => {}), [])

  const recarregar = useCallback(() => {
    recarregarImpressoes()
    const minha = geracao.agora()
    const vale = () => vivoRef.current && geracao.vale(minha)
    return listarPedidosKds(data)
      .then((lista) => {
        if (!vale()) return
        const atual = lista ?? []
        animar(transicaoDaFila(anteriorRef.current, atual))
        anteriorRef.current = atual
        setPedidos(atual)
        setErro(null)
      })
      .catch((e) => { if (vale()) setErro(`A fila não carregou: ${e.message}`) })
  }, [animar, recarregarImpressoes, data, geracao])

  useEffect(() => {
    vivoRef.current = true
    geracao.nova()
    anteriorRef.current = null
    const timers = timersRef.current
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
      timers.forEach(clearTimeout)
      timers.clear()
    }
  }, [recarregar, geracao])

  // Um toque (ou um soltar): a chamada vai, o cartão fica "movendo" até a API responder; a
  // lista nova chega pela resposta e pelo `pedido.mudou_status` do SSE.
  const avancar = useCallback((id, status) => {
    setMovendo((s) => comId(s, id, true))
    return mudarStatusKds(id, status)
      .then(() => recarregar())
      .catch((e) => setErro(`O pedido não mudou: ${e.message}`))
      .finally(() => setMovendo((s) => comId(s, id, false)))
  }, [recarregar])

  const reimprimir = useCallback((id) => reimprimirCanhoto(id)
    .then(() => recarregarImpressoes())
    .catch((e) => setErro(`Não voltou para a fila de impressão: ${e.message}`)), [recarregarImpressoes])

  // O canhoto saiu pela aba: tira da gaveta na hora e confirma na API (idempotente).
  const confirmar = useCallback((impressaoId) => {
    setImpressoes((lista) => lista.filter((i) => i.id !== impressaoId))
    return confirmarImpressao(impressaoId)
      .catch((e) => setErro(`O canhoto saiu, mas a fila não soube: ${e.message}`))
      .finally(() => recarregarImpressoes())
  }, [recarregarImpressoes])

  const limparErro = useCallback(() => setErro(null), [])

  return {
    pedidos, erro, aoVivo, movendo, novos, mudaram, saindo, impressoes,
    avancar, reimprimir, confirmarImpressao: confirmar, avisar: setErro, limparErro,
  }
}
