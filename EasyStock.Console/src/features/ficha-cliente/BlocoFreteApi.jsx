import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { ModalFrete } from '../../componentes/ModalFrete'
import { useFretesApi, useLogisticaDeDemonstracao } from '../../aplicacao/useFretesApi'

// A mesma decisão de frete do módulo Entregas, conservando o pedido da conversa.
export function BlocoFreteApi({ pedido, cliente, numero, endereco }) {
  const fretes = useFretesApi()
  // #1761: na loja de verdade a comparação não existe; o aviso e o botão eram ruído na Ficha.
  // #1839: contratar o transporte é do módulo Entregas (viagem e corrida); o pedido do cliente não
  // repete o atalho do Lalamove. Na loja de verdade o bloco some; fica só a comparação da demonstração.
  const demonstracao = useLogisticaDeDemonstracao(pedido.pedidoId)
  if (!demonstracao) return null
  return <Bloco titulo="Transporte e rastreio">
    <p>A comparação integrada abaixo é uma demonstração, sem chamada de entregador externo.</p>
    <Botao onClick={() => fretes.abrir({ titulo: `Entrega de ${cliente}`, pedidoId: pedido.pedidoId, pedidos: [{
      id: pedido.pedidoId, numero, cliente, endereco,
    }] })}>Ver comparação demonstrativa</Botao>
    <ModalFrete controlador={fretes} />
  </Bloco>
}
