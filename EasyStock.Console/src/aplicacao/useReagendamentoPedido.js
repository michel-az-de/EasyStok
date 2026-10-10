import { useEffect, useRef, useState } from 'react'
import { listarJanelasPedido, trocarJanelaPedido } from '../infra/api/comandaApi'

export function useReagendamentoPedido(pedidoId, aoSalvar = null, aoAlterado = null) {
  const [data, setData] = useState('')
  const [leitura, setLeitura] = useState(null)
  const [escolhida, setEscolhida] = useState('')
  const [avisar, setAvisar] = useState(false)
  const [erro, setErro] = useState(null)
  const [aviso, setAviso] = useState(null)
  const [enviando, setEnviando] = useState(false)
  const [revisao, setRevisao] = useState(0)
  const emVoo = useRef(false)

  useEffect(() => {
    let vivo = true
    setLeitura(null)
    listarJanelasPedido(pedidoId, data).then(
      (valor) => { if (vivo) setLeitura(valor) },
      (e) => { if (vivo) setErro(`Janelas não carregaram: ${e.message}`) },
    )
    return () => { vivo = false }
  }, [pedidoId, data, revisao])

  const salvar = async () => {
    if (emVoo.current || !escolhida) return
    emVoo.current = true
    setEnviando(true)
    setErro(null)
    setAviso(null)
    try {
      const resultado = await (aoSalvar ? aoSalvar(escolhida, avisar) : trocarJanelaPedido(pedidoId, escolhida, avisar))
      if (resultado?.erro) { setErro(resultado.erro); return }
      setAviso(!resultado.alterado ? 'Este agendamento já está confirmado.'
        : resultado.avisoEnfileirado ? 'Agendamento alterado. Aviso por WhatsApp na fila de envio.'
          : avisar ? 'Agendamento alterado. O aviso não entrou na fila; avise o cliente manualmente.'
            : 'Agendamento alterado. Avise o cliente se necessário.')
      setEscolhida('')
      setRevisao((v) => v + 1)
      // Uma falha de recarga não desfaz a troca já confirmada pela API.
      if (aoAlterado) await Promise.resolve(aoAlterado()).catch(() => setErro('Agendamento salvo. A lista não pôde ser atualizada.'))
    } catch (e) {
      setErro(`Não foi possível confirmar a troca: ${e.message}`)
    } finally {
      emVoo.current = false
      setEnviando(false)
    }
  }

  return { data, mudarData: (valor) => { setData(valor); setEscolhida(''); setErro(null) },
    leitura, escolhida, escolher: setEscolhida, avisar, mudarAviso: setAvisar, erro, aviso, enviando, salvar,
    recarregar: () => { setErro(null); setRevisao((v) => v + 1) } }
}
