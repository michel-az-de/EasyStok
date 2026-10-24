import { useCallback, useEffect, useRef, useState } from 'react'
import {
  confirmarImpressao, listarImpressoesPendentes, listarPedidosKds, mudarStatusKds, reimprimirCanhoto,
} from '../infra/api/kdsApi'
import { conectarEventosOperacao } from '../infra/api/eventosOperacao'
import { CONFERENCIA_AO_VIVO_MS, POLLING_SEM_SSE_MS, RECONECTAR_MS } from './recargaAoVivo'
import { transicaoDaFila } from '../dominio/kds'
import { pedeConfirmacaoDoSaldo } from '../dominio/pagamento'
import { criarRecargaDeAcao } from './recargaDeAcao'
import { criarEnvioComDesfazer, enviarPendentesAoSair } from './envioComDesfazer'

// Cozinha no modo API (F05). A fila vem do KDS sobre Pedido (S19) e recarrega a
// cada evento de pedido do SSE de operação (S18). Com o SSE caído, a tela não
// para: busca a fila a cada 15 s e tenta reconectar a cada 10 s. Com ele vivo, confere a fila a cada 30 s.
//
// Issue #1446: a cada leitura, compara com a anterior (`transicaoDaFila`) e marca
// quem entrou, quem mudou de coluna e quem saiu, para a tela animar; e lê a fila de
// impressão da API (S20), que a aba da Cozinha consome como a gaveta do protótipo.
// O canhoto em si é o PDF de 80 mm do protótipo, montado na tela (`features/cozinha`).
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
  const [saldoPendente, setSaldoPendente] = useState(null) // { id, status, valor }
  const vivoRef = useRef(true)
  const anteriorRef = useRef(null)
  const timersRef = useRef(new Set())
  const recargaRef = useRef(null)
  // #1686 (item 5): o toque agenda a mudança e o cartão mostra "Desfazer" antes de ela sair.
  const [adiados, setAdiados] = useState(() => new Map())
  const [envio] = useState(() => criarEnvioComDesfazer({
    aoMudar: (lista) => setAdiados(new Map(lista.map((a) => [a.chave, { status: a.dados.status, ate: a.ate }]))),
  }))
  useEffect(() => {
    const soltar = enviarPendentesAoSair(envio)
    return () => { soltar(); envio.descarregar() }
  }, [envio])

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
    setPedidos(null)
    setErro(null)
    setSaindo([])
    setNovos(new Set())
    setMudaram(new Set())
    const timers = timersRef.current
    let fechar = () => {}
    let reconexao = null
    let polling = null
    let noAr = false
    // #1577: o evento da própria ação já relê a fila; a ação não relê de novo na resposta.
    const recarga = criarRecargaDeAcao({ recarregar: () => recarregar(), aoVivo: () => noAr })
    recargaRef.current = recarga

    const conectar = () => {
      fechar = conectarEventosOperacao({
        aoEvento: ({ evento }) => {
          if (evento === 'ready') {
            noAr = true
            setAoVivo(true)
            // #2005 (console-20): SSE vivo e mudo não avisa o que não vira evento; a fila é conferida a cada 30 s.
            clearInterval(polling)
            polling = setInterval(recarregar, CONFERENCIA_AO_VIVO_MS)
          }
          if (RECARREGA_COM(evento)) recarga.aoEvento()
        },
        aoCair: () => {
          if (!vivoRef.current) return
          noAr = false
          setAoVivo(false)
          clearInterval(polling)
          polling = setInterval(recarregar, POLLING_SEM_SSE_MS)
          reconexao = setTimeout(conectar, RECONECTAR_MS)
        },
      })
    }

    recarregar()
    conectar()
    return () => {
      vivoRef.current = false
      fechar()
      recarga.desligar()
      clearTimeout(reconexao)
      clearInterval(polling)
      timers.forEach(clearTimeout)
      timers.clear()
    }
  }, [recarregar, geracao])

  // Um toque (ou um soltar): a chamada vai e o cartão fica "movendo" até a fila nova chegar, pelo
  // `pedido.mudou_status` do SSE; sem ele (caído, ou mudo por 2 s), pela releitura da própria ação.
  // #1776 (item 19): "Marcar entregue" com saldo a receber não é erro: a tela pergunta e reenvia confirmado.
  const enviarStatus = useCallback((id, status, confirmarSaldoAReceber = false) => {
    setMovendo((s) => comId(s, id, true))
    setSaldoPendente(null)
    const recarga = recargaRef.current
    const marca = recarga.marcar()
    return mudarStatusKds(id, status, { confirmarSaldoAReceber })
      .then(() => recarga.depoisDaAcao(marca))
      .catch((e) => (pedeConfirmacaoDoSaldo(e)
        ? setSaldoPendente({ id, status, valor: Number(e.dados?.valor) || 0 })
        : setErro(`O pedido não mudou: ${e.message}`)))
      .finally(() => setMovendo((s) => comId(s, id, false)))
  }, [])
  // #1686 (item 5): o avanço espera alguns segundos com "Desfazer" à vista; a confirmação do saldo a
  // receber já é o segundo toque de uma decisão e vai na hora.
  const avancar = useCallback((id, status, confirmarSaldoAReceber = false) => {
    if (confirmarSaldoAReceber) return enviarStatus(id, status, true)
    setSaldoPendente(null)
    return envio.agendar(id, () => enviarStatus(id, status, false), { status })
  }, [envio, enviarStatus])
  const desfazerAvanco = useCallback((id) => envio.desfazer(id), [envio])

  const reimprimir = useCallback((id) => reimprimirCanhoto(id)
    .then(() => recarregarImpressoes())
    .catch((e) => setErro(`Não voltou para a fila de impressão: ${e.message}`)), [recarregarImpressoes])

  // O canhoto saiu pela aba: tira da gaveta na hora e confirma na API (idempotente).
  const confirmar = useCallback((impressaoId) => {
    return confirmarImpressao(impressaoId)
      .then(() => setImpressoes((lista) => lista.filter((i) => i.id !== impressaoId)))
      .catch((e) => { setErro(`O canhoto saiu, mas a fila não soube: ${e.message}`); throw e })
      .finally(() => recarregarImpressoes())
  }, [recarregarImpressoes])

  const limparErro = useCallback(() => setErro(null), [])

  return {
    pedidos, erro, aoVivo, movendo, novos, mudaram, saindo, impressoes, saldoPendente, adiados, desfazerAvanco,
    descartarSaldoPendente: () => setSaldoPendente(null),
    avancar, recarregar, reimprimir, confirmarImpressao: confirmar, avisar: setErro, limparErro,
  }
}
