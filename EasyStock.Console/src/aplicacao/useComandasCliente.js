import { useEffect, useState } from 'react'
import { listarComandasCliente, confirmarComandaCliente, salvarComandaCliente } from '../infra/api/comandasClienteApi'
import { conectarEventosOperacao } from '../infra/api/eventosOperacao'

export function useComandasCliente(conversaId, ativo) {
  const [dados, setDados] = useState([])
  const [erro, setErro] = useState('')
  const [ocupado, setOcupado] = useState(false)
  const [versao, setVersao] = useState(0)
  useEffect(() => {
    if (!ativo || !conversaId) { setDados([]); return undefined }
    let vivo = true
    let lendo = false
    let novamente = false
    let fechar = null
    let reconectar = null
    async function carregar() {
      if (lendo) { novamente = true; return }
      lendo = true
      try { const resposta = await listarComandasCliente(conversaId); if (vivo) { setDados(resposta ?? []); setErro('') } }
      catch (e) { if (vivo) setErro(e.message) }
      finally { lendo = false; if (novamente && vivo) { novamente = false; carregar() } }
    }
    function conectar() {
      fechar = conectarEventosOperacao({ aoEvento: evento => {
        if (evento.evento === 'ready' || evento.evento?.startsWith('conversa.') || evento.evento?.startsWith('pedido.')) carregar()
      }, aoCair: () => { if (vivo) reconectar = setTimeout(conectar, 5000) } })
    }
    carregar(); conectar()
    const intervalo = setInterval(carregar, 15000)
    window.addEventListener('focus', carregar)
    return () => { vivo = false; fechar?.(); clearInterval(intervalo); clearTimeout(reconectar); window.removeEventListener('focus', carregar) }
  }, [conversaId, ativo, versao])

  async function executar(acao) {
    if (ocupado) return
    setOcupado(true); setErro('')
    try { await acao(); setVersao(v => v + 1) }
    catch (e) { setErro(e.message) }
    finally { setOcupado(false) }
  }
  return { dados, erro, ocupado,
    confirmar: (linkId, aceitar) => executar(() => confirmarComandaCliente(conversaId, linkId, aceitar)),
    salvar: (comanda, rascunho) => executar(() => salvarComandaCliente(conversaId, comanda.linkId, comanda.revisao, rascunho)) }
}
