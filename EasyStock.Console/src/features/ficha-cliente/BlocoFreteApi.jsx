import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { ModalFrete } from '../../componentes/ModalFrete'
import { useFretesApi, useLogisticaDeDemonstracao } from '../../aplicacao/useFretesApi'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { congelado } from '../../dominio/congelados'
import { hashDoModulo } from '../../dominio/modulos'
import css from './comanda.module.css'

export function ConsultaTransporte({ semPedido = false }) {
  const { permite } = useAcessoModulos()
  // #1891: o link externo da Lalamove está congelado; a entrega é combinada com o entregador próprio.
  if (congelado('lalamove')) return null
  return <div className={css.consultaTransporte}>
    <a href="https://web.lalamove.com/" target="_blank" rel="noopener noreferrer"
      aria-label="Consultar Lalamove (site externo, abre em nova aba)">Consultar Lalamove ↗</a>
    <p>{semPedido
      ? 'Cotação e contratação externas. Após criar o pedido e a viagem, use Entregas > Registrar corrida.'
      : 'Consulte o preço e combine o frete. Após contratar, registre a corrida na viagem em Entregas > Registrar corrida.'}</p>
    {!semPedido && permite('entregas') && <a href={hashDoModulo('entregas')}>Abrir Entregas</a>}
  </div>
}

// A mesma decisão de frete do módulo Entregas, conservando o pedido da conversa.
export function BlocoFreteApi({ pedido, cliente, numero, endereco }) {
  const fretes = useFretesApi()
  // #1761: na loja de verdade a comparação não existe; o aviso e o botão eram ruído na Ficha.
  // #1839: contratar o transporte é do módulo Entregas (viagem e corrida); o pedido do cliente não
  // repete o atalho do Lalamove. Na loja de verdade o bloco some; fica só a comparação da demonstração.
  const demonstracao = useLogisticaDeDemonstracao(pedido.pedidoId)
  if (congelado('lalamove') || !demonstracao) return null
  return <Bloco titulo="Transporte e rastreio">
    <p>A comparação integrada abaixo é uma demonstração, sem chamada de entregador externo.</p>
    <Botao onClick={() => fretes.abrir({ titulo: `Entrega de ${cliente}`, pedidoId: pedido.pedidoId, pedidos: [{
      id: pedido.pedidoId, numero, cliente, endereco,
    }] })}>Ver comparação demonstrativa</Botao>
    <ModalFrete controlador={fretes} />
  </Bloco>
}
