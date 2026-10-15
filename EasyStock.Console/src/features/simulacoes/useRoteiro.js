// Frente 7 · Menu de simulações (rodada 5, seção 7). "features/simulacoes/
// useRoteiro.js: roda os passos com setTimeout, respeita 'Pausar' e limpa ao
// desmontar" (seção 7, "Como funciona por dentro").
//
// A pausa é estado LOCAL deste hook, não do reducer: só quem lê é este
// mesmo hook, e o motivo de não subir para o estado global está anotado em
// `aplicacao/casos/simulacao.js`.
//
// Rodada 11 (registro 92): quem chama é `app/App.jsx`, não mais a gaveta.
// O roteiro precisa seguir rodando com a gaveta fechada, porque ela fecha
// sozinha ao iniciar o cenário para a conversa ficar à vista.
import { useCallback, useEffect, useRef, useState } from 'react'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'

export function useRoteiro() {
  const {
    montarRoteiro, executarPassoSimulado, deslocarRelogioSimulado, zerarRelogioSimulado, limparSimulacoes,
  } = useAcoes()
  const { agora } = useAtendimento()

  const [cenarioEmCurso, setCenarioEmCurso] = useState(null)
  const [pausado, setPausado] = useState(false)

  const filaRef = useRef([])
  const timersRef = useRef([])
  const pausadoRef = useRef(false)
  const agoraRef = useRef(agora)
  useEffect(() => { agoraRef.current = agora }, [agora])

  const limparTimers = () => {
    timersRef.current.forEach(clearTimeout)
    timersRef.current = []
  }

  useEffect(() => () => limparTimers(), [])

  // Espia o topo da fila sem tirar: só sai dela quando o passo REALMENTE
  // dispara. Pausar no meio da espera cancela o `setTimeout`, mas o passo
  // continua lá para a próxima chamada pegar de novo. A recursão do
  // `setTimeout` lê `agendarProximoRef.current` (atualizada no efeito logo
  // abaixo, nunca durante a renderização) em vez do nome da própria const,
  // que dispara aviso de "lida enquanto ainda inicializa".
  const agendarProximoRef = useRef(() => {})

  const agendarProximo = useCallback(() => {
    if (pausadoRef.current) return
    const passo = filaRef.current[0]
    if (!passo) {
      setCenarioEmCurso((atual) => (atual ? { ...atual, concluido: true } : atual))
      return
    }
    const t = setTimeout(() => {
      filaRef.current.shift()
      executarPassoSimulado(passo, agoraRef.current)
      agendarProximoRef.current()
    }, passo.esperaMs)
    timersRef.current.push(t)
  }, [executarPassoSimulado])

  useEffect(() => { agendarProximoRef.current = agendarProximo }, [agendarProximo])

  const iniciar = useCallback((cenario) => {
    limparTimers()
    pausadoRef.current = false
    setPausado(false)
    const passos = montarRoteiro(cenario.id, agora)
    // `aposMs` no roteiro é acumulado desde o início; aqui vira espera
    // relativa ao passo anterior, porque cada um só é agendado depois que o
    // de cima já rodou.
    let ultimo = 0
    filaRef.current = passos.map((passo) => {
      const esperaMs = Math.max(passo.aposMs - ultimo, 0)
      ultimo = passo.aposMs
      return { ...passo, esperaMs }
    })
    setCenarioEmCurso({ id: cenario.id, titulo: cenario.titulo, concluido: false })
    agendarProximo()
  }, [agora, montarRoteiro, agendarProximo])

  const alternarPausa = useCallback(() => {
    const proximo = !pausadoRef.current
    pausadoRef.current = proximo
    setPausado(proximo)
    if (proximo) limparTimers()
    else agendarProximo()
  }, [agendarProximo])

  const desfazer = useCallback(() => {
    limparTimers()
    filaRef.current = []
    pausadoRef.current = false
    setPausado(false)
    setCenarioEmCurso(null)
    limparSimulacoes()
  }, [limparSimulacoes])

  return {
    cenarioEmCurso, pausado, iniciar, alternarPausa, desfazer, deslocarRelogioSimulado, zerarRelogioSimulado,
  }
}
