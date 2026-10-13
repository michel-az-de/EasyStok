import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { ModalFrete } from '../../componentes/ModalFrete'
import { useFretesApi } from '../../aplicacao/useFretesApi'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { hashDoModulo } from '../../dominio/modulos'
import css from './comanda.module.css'

export function ConsultaTransporte({ semPedido = false }) {
  const { permite } = useAcessoModulos()
  return <div className={css.consultaTransporte}>
    <a href="https://web.lalamove.com/" target="_blank" rel="noopener noreferrer"
      aria-label="Consultar Lalamove (site externo, abre em nova aba)">Consultar Lalamove ↗</a>
    <p>Consulte o preço na Lalamove e combine o frete. {semPedido
      ? 'Com pedido e viagem criados, registre a corrida em Entregas > Registrar corrida.'
      : 'Depois de contratar, registre a corrida na viagem em Entregas > Registrar corrida.'}</p>
    {!semPedido && permite('entregas') && <a href={hashDoModulo('entregas')}>Abrir Entregas</a>}
  </div>
}

// A mesma decisão de frete do módulo Entregas, conservando o pedido da conversa.
export function BlocoFreteApi({ pedido, cliente, numero, endereco }) {
  const fretes = useFretesApi()
  return <Bloco titulo="Transporte e rastreio">
    <ConsultaTransporte />
    <p>A comparação integrada abaixo é uma demonstração, sem chamada de entregador externo.</p>
    <Botao onClick={() => fretes.abrir({ titulo: `Entrega de ${cliente}`, pedidoId: pedido.pedidoId, pedidos: [{
      id: pedido.pedidoId, numero, cliente, endereco,
    }] })}>Ver comparação demonstrativa</Botao>
    <ModalFrete controlador={fretes} />
  </Bloco>
}
