import { useEffect, useRef, useState } from 'react'
import { dataIsoNoFuso, somarDiasIso } from '../dominio/formato'
import { listarAgendaEncomendas } from '../infra/api/entregasApi'
import { conectarEventosOperacao } from '../infra/api/eventosOperacao'

export function useAgendaEncomendasApi() {
  const [hoje] = useState(() => dataIsoNoFuso(Date.now()))
  const [inicio, setInicio] = useState(hoje)
  const [versao, setVersao] = useState(0)
  const [agenda, setAgenda] = useState(null)
  const [erro, setErro] = useState(null)
  const periodoAnterior = useRef(null)
  const fim = somarDiasIso(inicio, 6)

  useEffect(() => {
    let vivo = true
    const periodo = `${inicio}:${fim}`
    if (periodoAnterior.current !== periodo) setAgenda(null)
    periodoAnterior.current = periodo
    setErro(null)
    listarAgendaEncomendas(inicio, fim)
      .then((resultado) => { if (vivo) setAgenda(resultado) })
      .catch((e) => { if (vivo) setErro(`A agenda não carregou: ${e.message}`) })
    return () => { vivo = false }
  }, [inicio, fim, versao])

  useEffect(() => {
    const intervalo = setInterval(() => setVersao((v) => v + 1), 15000)
    const fechar = conectarEventosOperacao({
      aoEvento: ({ evento }) => { if (evento.startsWith('pedido.')) setVersao((v) => v + 1) },
      aoCair: () => {},
    })
    return () => { clearInterval(intervalo); fechar() }
  }, [])

  return { hoje, inicio, fim, agenda, erro,
    voltar: () => setInicio((dia) => dia <= hoje ? hoje : [hoje, somarDiasIso(dia, -7)].sort().at(-1)),
    avancar: () => setInicio((dia) => somarDiasIso(dia, 7)),
    mudarInicio: setInicio,
    atualizar: () => setVersao((v) => v + 1) }
}
