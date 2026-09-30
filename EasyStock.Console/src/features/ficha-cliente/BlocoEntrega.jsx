import { Bloco } from '../../componentes/Bloco'
import { ListaDados } from '../../componentes/ListaDados'
import { numeroParaEntregador, registroDaEntrega, textoDoEntregador } from '../../dominio/despacho'
import { horaCurta } from '../../dominio/formato'
import { numeroCurto } from '../../dominio/pedido'
import { entregadorResolvido } from '../../dominio/viagem'
import css from './ficha.module.css'

// Issue #17 (feedback da Thatiane): o pedido diz quem levou, com veículo,
// placa e empresa, e a hora que saiu e chegou. É o que ela procura quando um
// cliente reclama e três "José" saíram no mesmo dia. Nasce no "Embalado",
// quando o número do pedido passa a importar para quem vem buscar.
const PASSOS_COM_ENTREGA = new Set(['embalado', 'entrega', 'entregue'])

export function BlocoEntrega({ conversa }) {
  const pedido = conversa.pedido
  if (!pedido || !PASSOS_COM_ENTREGA.has(pedido.estado)) return null
  const registro = registroDaEntrega(conversa)
  const quem = registro?.entregador ?? entregadorResolvido(pedido)

  return (
    <Bloco titulo="Entrega">
      <ListaDados
        itens={[
          { rotulo: 'Pedido para o entregador', valor: `${numeroParaEntregador(pedido)} (comanda ${numeroCurto(pedido.numero)} é interna)` },
          {
            rotulo: pedido.estado === 'entregue' ? 'Quem levou' : 'Quem leva',
            valor: textoDoEntregador(quem) ?? 'A definir no despacho',
          },
          { rotulo: 'Saiu', valor: registro?.saiuEm ? horaCurta(registro.saiuEm) : null },
          { rotulo: 'Entregue', valor: registro?.entregueEm ? horaCurta(registro.entregueEm) : null },
        ]}
      />
      {!quem && pedido.estado === 'embalado' && (
        <p className={css.corpoBloco}>Quem leva é perguntado no despacho, com veículo e placa.</p>
      )}
    </Bloco>
  )
}
