import { useId } from 'react'
import { impressaoDoPedido } from '../../../aplicacao/impressos'
import { Botao } from '../../../componentes/Botao'
import { ImprimirPedido } from '../../../componentes/ImprimirPedido'
import { Modal } from '../../../componentes/Modal'
import { Pilula } from '../../../componentes/Pilula'
import { telefoneNaTela } from '../../../dominio/cadastros'
import { dataHora, moeda, plural } from '../../../dominio/formato'
import {
  linhaDoTempoDoPedido, rotuloDaSituacao, rotuloDoCanal, rotuloDoMetodo, situacaoDoPagamento, situacaoPedeAcao, tituloDoPedido,
} from '../../../dominio/pedidosCadastro'
import css from './pedidos.module.css'

// #2478: detalhe de um pedido, sem alterá-lo (imprimir só registra a impressão) (`GET api/pedidos/{id}`). Mudar situação, cobrar e cancelar seguem
// na comanda da conversa, onde estão o contexto e as travas; daqui a pessoa vai para lá com "Abrir conversa".
// Usado pela tela Pedidos de Cadastros e pela Busca na Casa (o App injeta, sem import entre features). A Busca
// abre fora do `AtendimentoProvider`, então o detalhe não lê contexto: quem sabe da conversa passa `abrirConversa`.
export function DetalhePedido({ dados, aoFechar, abrirConversa }) {
  const id = useId()
  const { pedido, itens = [], eventos = [], pagamentos = [] } = dados
  const pagamento = situacaoDoPagamento(pedido)
  const linha = linhaDoTempoDoPedido(eventos)
  const telefone = telefoneNaTela(pedido.clienteTelefone)
  const canal = rotuloDoCanal(pedido.origem)
  const rodape = <>
    {abrirConversa && <Botao variante="primario" icone="conversa" onClick={() => { aoFechar(); abrirConversa() }}>Abrir conversa</Botao>}
    <ImprimirPedido {...impressaoDoPedido(pedido.id, null, pedido.clienteNome)} />
    <Botao variante="texto" onClick={aoFechar}>Fechar</Botao>
  </>

  return <Modal titulo={tituloDoPedido(pedido)} descricao={[pedido.clienteNome || 'Cliente sem nome', canal].filter(Boolean).join(' · ')} aoFechar={aoFechar} largura="lg" rodape={rodape}>
    <div className={css.detalhe}>
      <dl className={css.resumo}>
        <div><dt>Situação</dt><dd>{situacaoPedeAcao(pedido.status) ? <Pilula tom="aviso">{rotuloDaSituacao(pedido.status)}</Pilula> : rotuloDaSituacao(pedido.status)}</dd></div>
        <div><dt>Cliente</dt><dd>{pedido.clienteNome || 'Cliente sem nome'}{telefone && <small>{telefone}</small>}{pedido.clienteApt && <small>Apto {pedido.clienteApt}</small>}</dd></div>
        <div><dt>Feito em</dt><dd>{pedido.criadoEm ? dataHora(pedido.criadoEm) : 'Data não informada'}{canal && <small>{canal}</small>}</dd></div>
        {pedido.agendadoParaEm && <div><dt>Para</dt><dd>{dataHora(pedido.agendadoParaEm)}</dd></div>}
        {pedido.entreguEm && <div><dt>Entregue em</dt><dd>{dataHora(pedido.entreguEm)}</dd></div>}
        {pedido.canceladoEm && <div><dt>Cancelado em</dt><dd>{dataHora(pedido.canceladoEm)}</dd></div>}
      </dl>

      <section aria-labelledby={`${id}-itens`}>
        <h3 id={`${id}-itens`}>Itens <small>({plural(itens.length, 'item', 'itens')})</small></h3>
        {itens.length === 0 ? <p className={css.apoio}>Este pedido não tem itens.</p> : <table className={css.itens}>
          <thead><tr><th scope="col">Qtd.</th><th scope="col">Item</th><th scope="col" className={css.valor}>Subtotal</th></tr></thead>
          <tbody>{itens.map((item) => <tr key={item.id}>
            <td>{String(item.quantidade).replace('.', ',')}{item.unidade && item.unidade !== 'un' ? ` ${item.unidade}` : ''}</td>
            <td>{item.emoji ? <span aria-hidden="true">{item.emoji} </span> : null}{item.nome}<small>{moeda(item.precoUnitario)} cada</small>{item.observacao && <small>Obs.: {item.observacao}</small>}</td>
            <td className={css.valor}>{moeda(item.subtotal)}</td>
          </tr>)}</tbody>
        </table>}
        <dl className={css.totais}>
          <div><dt>Total</dt><dd>{moeda(pedido.total)}</dd></div>
          <div><dt>Recebido</dt><dd>{moeda(pedido.totalPago ?? 0)}</dd></div>
          {pagamento?.falta > 0 && <div className={css.falta}><dt>Falta receber</dt><dd>{moeda(pagamento.falta)}</dd></div>}
          {pagamento?.sobra > 0 && <div className={css.falta}><dt>{pedido.status === 'cancelado' ? 'Recebido no pedido cancelado' : 'Recebido a mais'}</dt><dd>{moeda(pagamento.sobra)}</dd></div>}
          {pagamento && !pagamento.falta && !pagamento.sobra && <div className={css.pago}><dt>Pagamento</dt><dd>Pago</dd></div>}
        </dl>
      </section>

      <section aria-labelledby={`${id}-pagamentos`}>
        <h3 id={`${id}-pagamentos`}>Pagamentos</h3>
        {pagamentos.length === 0 ? <p className={css.apoio}>Nenhum pagamento registrado.</p> : <ul className={css.lista}>
          {pagamentos.map((p, i) => <li key={`${p.pagoEm}:${i}`}>
            <span><strong>{rotuloDoMetodo(p.metodo)}</strong><small>{dataHora(p.pagoEm)}{p.registradoPorNome ? ` · ${p.registradoPorNome}` : ''}{p.observacao ? ` · ${p.observacao}` : ''}</small></span>
            <strong className={css.valor}>{moeda(p.valor)}</strong>
          </li>)}
        </ul>}
      </section>

      {pedido.observacoes && <section aria-labelledby={`${id}-observacoes`}>
        <h3 id={`${id}-observacoes`}>Observações</h3>
        <p className={css.observacoes}>{pedido.observacoes}</p>
      </section>}

      <details className={css.historico}>
        <summary>Histórico ({plural(linha.length, 'registro', 'registros')})</summary>
        {linha.length === 0 ? <p className={css.apoio}>Sem registros para este pedido.</p> : <ol>
          {linha.map((e, i) => <li key={`${e.quando}:${i}`}><span>{e.texto}</span><small>{dataHora(e.quando)}{e.quem ? ` · ${e.quem}` : ''}</small></li>)}
        </ol>}
      </details>
    </div>
  </Modal>
}
