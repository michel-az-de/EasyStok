import { useRef, useState } from 'react'
import { useAcoes } from './contextos'
import { useAcessoModulos } from './acessoModulos'
import { anunciarAberturaDeConversa } from './aberturaDeConversa'
import { hashDoModulo } from '../dominio/modulos'

// #2871: "Novo pedido" na lista e na Ficha de Clientes, em Pedidos e na busca do Balcão. O cliente pediu pelo WhatsApp
// pessoal da dona; a equipe registra pelo Console. A API abre (ou reaproveita) a conversa do WhatsApp da loja com ele,
// o Balcão a seleciona e o cardápio da comanda abre nela. O resumo do pedido é o primeiro contato com o cliente.
// Só no modo API e para quem atende conversas: sem a ação `abrirConversaDoCliente` (demonstração) o botão não aparece, e
// sem a permissão de atender (`acoes.atenderConversas`, a regra da rota `do-cliente`) a API responderia 403.
export function useNovoPedidoDoCliente({ aoAbrir } = {}) {
  const { abrirConversaDoCliente, selecionar } = useAcoes()
  const { permite, acoes } = useAcessoModulos()
  const disponivel = Boolean(abrirConversaDoCliente) && permite('atendimento') && acoes?.atenderConversas === true
  const [abrindo, marcarAbrindo] = useState(null)
  const [erro, falhar] = useState(null)
  const emVoo = useRef(false)
  const iniciar = async (clienteId) => {
    if (!disponivel || !clienteId || emVoo.current) return false
    emVoo.current = true
    marcarAbrindo(clienteId); falhar(null)
    try {
      const resumo = await abrirConversaDoCliente(clienteId)
      selecionar(resumo.id, resumo)
      window.location.hash = hashDoModulo('atendimento', 'balcao')
      anunciarAberturaDeConversa(resumo.id, { novoPedido: true })
      aoAbrir?.(resumo.id)
      return true
    } catch (e) {
      falhar(e.message || 'Não consegui abrir o pedido deste cliente. Tente de novo.')
      return false
    } finally {
      emVoo.current = false
      marcarAbrindo(null)
    }
  }
  return { disponivel, abrindo, erro, iniciar, limparErro: () => falhar(null) }
}
