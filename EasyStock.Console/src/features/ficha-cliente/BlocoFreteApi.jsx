import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { ModalFrete } from '../../componentes/ModalFrete'
import { useFretesApi } from '../../aplicacao/useFretesApi'

// A mesma decisão de frete do módulo Entregas, conservando o pedido da conversa.
export function BlocoFreteApi({ pedido, cliente, numero, endereco }) {
  const fretes = useFretesApi()
  return <Bloco titulo="Transporte e rastreio">
    <p>Compare o transporte deste pedido e acompanhe a entrega.</p>
    <Botao onClick={() => fretes.abrir({ titulo: `Entrega de ${cliente}`, pedidoId: pedido.pedidoId, pedidos: [{
      id: pedido.pedidoId, numero, cliente, endereco,
    }] })}>Cotar ou acompanhar entrega</Botao>
    <ModalFrete controlador={fretes} />
  </Bloco>
}
