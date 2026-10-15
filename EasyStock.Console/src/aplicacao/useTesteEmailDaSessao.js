import { useEffect, useState } from 'react'
import { consultarTesteEmail, solicitarTesteEmail } from '../infra/api/testeNotificacaoApi'

const estaPendente = consulta => !consulta || consulta.eventoStatus === 'Pendente'
  || consulta.mensagens.some(m => m.status === 'Pendente' || m.status === 'EmEnvio')

export function useTesteEmailDaSessao() {
  const [motivo, setMotivo] = useState('')
  const [eventoId, setEventoId] = useState(null)
  const [consulta, setConsulta] = useState(null)
  const [enviando, setEnviando] = useState(false)
  const [consultando, setConsultando] = useState(false)
  const [erroEnvio, setErroEnvio] = useState(null)
  const [erroConsulta, setErroConsulta] = useState(null)
  const [tentativa, setTentativa] = useState(0)

  useEffect(() => {
    if (!eventoId) return
    let vivo = true
    let timer
    let rodadas = 0
    const consultar = async () => {
      try {
        const resultado = await consultarTesteEmail(eventoId)
        if (!vivo) return
        if (!Array.isArray(resultado?.mensagens)) throw new Error('O resultado do teste não foi recebido.')
        setConsulta(resultado)
        if (estaPendente(resultado) && ++rodadas < 15) timer = setTimeout(consultar, 2000)
        else setConsultando(false)
      } catch (erro) {
        if (vivo) { setErroConsulta(erro.message); setConsultando(false) }
      }
    }
    consultar()
    return () => { vivo = false; clearTimeout(timer) }
  }, [eventoId, tentativa])

  const enviar = async e => {
    e.preventDefault()
    if (enviando || consultando || motivo.trim().length < 10) return
    setEnviando(true)
    setErroEnvio(null)
    try {
      const solicitado = await solicitarTesteEmail(motivo.trim())
      if (!solicitado?.eventoId) throw new Error('A solicitação do teste não foi recebida.')
      setConsulta(null)
      setErroConsulta(null)
      setConsultando(true)
      setEventoId(solicitado.eventoId)
    } catch (erro) { setErroEnvio(erro.message) }
    finally { setEnviando(false) }
  }

  const consultarNovamente = () => {
    setErroConsulta(null)
    setConsultando(true)
    setTentativa(atual => atual + 1)
  }
  return { motivo, setMotivo, eventoId, consulta, enviando, consultando, erroEnvio, erroConsulta,
    enviar, consultarNovamente, aguardando: Boolean(eventoId && estaPendente(consulta)) }
}
