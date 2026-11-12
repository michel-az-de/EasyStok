import { useEffect, useRef, useState } from 'react'
import { listarComandasCliente, confirmarComandaCliente, salvarComandaCliente, combinarFreteComanda, encaminharEntregaComanda, confirmarPedidoComanda } from '../infra/api/comandasClienteApi'
import { conectarEventosOperacao } from '../infra/api/eventosOperacao'
import { criarEsperaDeReconexao } from './recargaAoVivo'
export { idDaJanela, janelaDoId } from '../infra/api/comandaApi'

const RECONECTAR_COMANDAS_MS = 5000

export function useComandasCliente(conversaId, ativo) {
  const [dados, setDados] = useState([])
  const [erro, setErro] = useState('')
  const [erroLeitura, setErroLeitura] = useState('')
  const [ocupado, setOcupado] = useState(false)
  const [carregando, setCarregando] = useState(true)
  const trava = useRef(false)
  // #2379: reler depois da ação não reabre o SSE; o efeito expõe a leitura por aqui.
  const lerAgora = useRef(null)
  useEffect(() => {
    if (!ativo || !conversaId) { setDados([]); return undefined }
    setCarregando(true)
    let vivo = true
    let lendo = false
    let novamente = false
    let fechar = null
    let reconectar = null
    const esperaDeReconexao = criarEsperaDeReconexao({ base: RECONECTAR_COMANDAS_MS })
    async function carregar() {
      if (lendo) { novamente = true; return }
      lendo = true
      try { const resposta = await listarComandasCliente(conversaId); if (vivo) { setDados(resposta ?? []); setErroLeitura('') } }
      catch (e) { if (vivo) setErroLeitura(e.message) }
      finally { lendo = false; if (vivo) setCarregando(false); if (novamente && vivo) { novamente = false; carregar() } }
    }
    function conectar() {
      fechar = conectarEventosOperacao({ aoEvento: evento => {
        if (evento.evento === 'ready') esperaDeReconexao.zerar()
        if (evento.evento === 'ready' || evento.evento?.startsWith('conversa.') || evento.evento?.startsWith('pedido.')) carregar()
      }, aoCair: () => { if (vivo) reconectar = setTimeout(conectar, esperaDeReconexao.proxima()) } })
    }
    lerAgora.current = carregar
    carregar(); conectar()
    const intervalo = setInterval(carregar, 15000)
    window.addEventListener('focus', carregar)
    return () => { vivo = false; lerAgora.current = null; fechar?.(); clearInterval(intervalo); clearTimeout(reconectar); window.removeEventListener('focus', carregar) }
  }, [conversaId, ativo])

  async function executar(acao) {
    if (trava.current) return false
    trava.current = true
    setOcupado(true); setErro('')
    try { await acao(); lerAgora.current?.(); return true }
    catch (e) { setErro(e.message); lerAgora.current?.(); return false }
    finally { trava.current = false; setOcupado(false) }
  }
  return { dados, erro: erro || erroLeitura, ocupado, carregando,
    atualizar: () => { setErro(''); lerAgora.current?.() },
    encaminharEntrega: (comanda) => executar(() => encaminharEntregaComanda(conversaId, comanda.linkId, comanda.revisao)),
    confirmarPedido: (comanda) => executar(() => confirmarPedidoComanda(conversaId, comanda.linkId, comanda.revisao)),
    combinarFrete: (comanda, valor) => executar(() => combinarFreteComanda(conversaId, comanda.linkId, comanda.revisao, valor)),
    confirmar: (linkId, aceitar) => executar(() => confirmarComandaCliente(conversaId, linkId, aceitar)),
    salvar: (comanda, rascunho) => executar(() => salvarComandaCliente(conversaId, comanda.linkId, comanda.revisao, rascunho)) }
}
