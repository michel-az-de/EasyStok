import { useRef, useState } from 'react'
import { useAcoes, useAtendimento, useCatalogoOpcional } from './contextos'
import { lerSessao } from '../infra/api/sessao'
import { useAcessoModulos } from './acessoModulos'
import { motivoDoNovoPedido, novoPedidoDisponivel, rotuloDoNovoPedido } from './janelaDoPedido'

// #2871, #2921: "Novo pedido" na lista e na Ficha de Clientes, em Pedidos e na busca do Balcão. O cliente pediu pelo
// WhatsApp pessoal da dona; a equipe registra pelo Console. A API abre (ou reaproveita) a conversa do WhatsApp da loja com
// ele e a montagem abre numa janela SOBRE a tela de origem: nada de trocar de tela, hash ou seleção. O resumo do pedido é
// o primeiro contato com o cliente.
// Só no modo API e para quem atende conversas: sem a ação `abrirConversaDoCliente` (demonstração) o botão não aparece, e
// sem a permissão de atender (`acoes.atenderConversas`, a regra da rota `do-cliente`) a API responderia 403.
export function useNovoPedidoDoCliente({ aoAbrir } = {}) {
  const { abrirConversaDoCliente, abrirJanelaDoPedido } = useAcoes()
  const { permite, acoes } = useAcessoModulos()
  // Ler a janela refaz o render ao abrir e ao fechar (Registrar e Descartar também fecham): o rótulo acompanha o rascunho.
  useAtendimento()
  const disponivel = Boolean(abrirJanelaDoPedido) && novoPedidoDisponivel({ abrirConversaDoCliente, permite, acoes })
  // #3154: loja online desligada; quem mostra o botão pode desligá-lo com este motivo. O catálogo é opcional: telas montadas
  // só com estado e ações (provas de Cadastros) seguem sem motivo.
  const motivo = motivoDoNovoPedido(useCatalogoOpcional())
  const [abrindo, marcarAbrindo] = useState(null)
  const [erro, falhar] = useState(null)
  const emVoo = useRef(false)
  // #2930: `celular` vem do campo da escolha do cliente quando o cadastro está sem telefone.
  const iniciar = async (clienteId, { celular, nome } = {}) => {
    if (!disponivel || !clienteId || emVoo.current) return false
    emVoo.current = true
    marcarAbrindo(clienteId); falhar(null)
    try {
      const resumo = await abrirConversaDoCliente(clienteId, celular)
      abrirJanelaDoPedido(resumo, { clienteId, nome })
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
  const rotulo = (clienteId) => rotuloDoNovoPedido(lerSessao(), clienteId)
  return { disponivel, motivo, abrindo, erro, iniciar, rotulo, limparErro: () => falhar(null) }
}
