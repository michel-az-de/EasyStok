import { useCallback, useRef, useState } from 'react'

// Grava áudio do microfone (MediaRecorder). Puro wrapper de API de navegador,
// sem domínio: quem chama decide o que fazer com { dataUrl, duracaoMs } que
// `finalizar()` devolve (frente Anexos, rodada 7, pedido do dono 24/09/2026:
// "gravar com o microfone... cancelar ou enviar").
//
// estado: 'ocioso' | 'pedindo' | 'gravando' | 'erro'.
export function useGravadorAudio() {
  const [estado, setEstado] = useState('ocioso')
  const [duracaoMs, setDuracaoMs] = useState(0)
  const [erro, setErro] = useState(null)

  const streamRef = useRef(null)
  const gravadorRef = useRef(null)
  const pedacosRef = useRef([])
  const inicioRef = useRef(0)
  const timerRef = useRef(null)

  const pararTrilhas = () => {
    streamRef.current?.getTracks().forEach((trilha) => trilha.stop())
    streamRef.current = null
  }

  const limparTimer = () => {
    if (timerRef.current) clearInterval(timerRef.current)
    timerRef.current = null
  }

  const iniciar = useCallback(async () => {
    setErro(null)
    setEstado('pedindo')
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
      streamRef.current = stream
      const gravador = new MediaRecorder(stream)
      pedacosRef.current = []
      gravador.ondataavailable = (evento) => {
        if (evento.data.size > 0) pedacosRef.current.push(evento.data)
      }
      gravadorRef.current = gravador
      gravador.start()
      inicioRef.current = Date.now()
      setDuracaoMs(0)
      setEstado('gravando')
      timerRef.current = setInterval(() => setDuracaoMs(Date.now() - inicioRef.current), 200)
    } catch {
      pararTrilhas()
      setErro('Não consegui acessar o microfone. Verifique a permissão do navegador.')
      setEstado('erro')
    }
  }, [])

  const cancelar = useCallback(() => {
    limparTimer()
    if (gravadorRef.current && gravadorRef.current.state !== 'inactive') gravadorRef.current.stop()
    pararTrilhas()
    pedacosRef.current = []
    setEstado('ocioso')
    setDuracaoMs(0)
  }, [])

  // Devolve null quando não havia gravação em curso (clique duplo em
  // "Enviar", por exemplo); quem chama já sabe tratar esse caso como cancelar.
  const finalizar = useCallback(() => new Promise((resolve) => {
    limparTimer()
    const gravador = gravadorRef.current
    if (!gravador || gravador.state === 'inactive') { resolve(null); return }
    const duracaoFinal = Date.now() - inicioRef.current
    gravador.onstop = () => {
      const blob = new Blob(pedacosRef.current, { type: gravador.mimeType || 'audio/webm' })
      pararTrilhas()
      const leitor = new FileReader()
      leitor.onload = () => {
        setEstado('ocioso')
        setDuracaoMs(0)
        resolve({ dataUrl: leitor.result, duracaoMs: duracaoFinal })
      }
      leitor.readAsDataURL(blob)
    }
    gravador.stop()
  }), [])

  const limparErro = useCallback(() => { setErro(null); setEstado('ocioso') }, [])

  return {
    estado, duracaoMs, erro, iniciar, cancelar, finalizar, limparErro,
  }
}
