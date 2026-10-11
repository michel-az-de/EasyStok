import { Botao } from '../../componentes/Botao'
import { moeda } from '../../dominio/formato'
import { useComandasCliente } from '../../aplicacao/useComandasCliente'
import css from './comanda.module.css'

export function ComandaDoCliente({ conversaId, ativo }) {
  const { dados, erro, ocupado, confirmar, salvar } = useComandasCliente(conversaId, ativo)
  const visiveis = dados.filter(c => c.podeEditar || ['alteracao_aguardando_confirmacao', 'estorno_pendente'].includes(c.envio?.situacao)
    || c.linkId === dados[0]?.linkId && c.envio)
  if (!visiveis.length && !erro) return null
  function ajustar(comanda, indice, delta) {
    const itens = comanda.rascunho.itens.map((i, n) => n === indice ? { ...i, qtd: i.qtd + delta } : i).filter(i => i.qtd > 0)
    salvar(comanda, { ...comanda.rascunho, itens })
  }
  return <section className={css.clienteCompartilhado} aria-label="Comanda compartilhada com o cliente">
    <b>Comanda do cliente</b>
    {erro && <p role="alert">{erro}</p>}
    {visiveis.map(c => <div key={c.linkId}>
      <p>{c.podeEditar ? 'Seleção compartilhada. Só vira pedido quando o cliente confirmar.' : c.envio?.situacao === 'estorno_pendente' ? 'Devolução em acompanhamento.'
        : c.envio?.situacao === 'alteracao_aguardando_confirmacao' ? 'Cliente enviou uma alteração para conferir.' : 'Envio registrado. Gere um novo link para outro ajuste.'}</p>
      <ul>{c.selecao.map((i, n) => <li key={`${i.cardapioItemId}:${i.variacaoId}`}>
        <span>{i.qtd}× {i.nome} · {moeda(i.qtd * i.preco)}</span>
        {i.observacao && <small>{i.observacao}</small>}
        {c.podeEditar && <span className={css.contador}>
          <button type="button" disabled={ocupado} aria-label={`Tirar uma unidade de ${i.nome}`} onClick={() => ajustar(c, n, -1)}>−</button>
          <button type="button" disabled={ocupado || i.qtd >= 99} aria-label={`Adicionar uma unidade de ${i.nome}`} onClick={() => ajustar(c, n, 1)}>+</button>
        </span>}
      </li>)}</ul>
      {c.podeEditar ? <label>Observação para a cozinha
        <textarea key={c.revisao} defaultValue={c.rascunho.observacoes ?? ''} maxLength={2000} disabled={ocupado}
          onBlur={e => { if (e.target.value !== (c.rascunho.observacoes ?? '')) salvar(c, { ...c.rascunho, observacoes: e.target.value }) }} />
      </label> : c.rascunho.observacoes && <p>Observação: {c.rascunho.observacoes}</p>}
      <p>Total previsto: <strong>{moeda(c.totalPrevisto)}</strong>{c.pedido && <> · Já coberto: {moeda(c.pedido.alocado ?? c.pedido.totalPago)}</>}</p>
      {c.envio?.saldo < 0 && <p>Cliente escolheu {c.rascunho.acerto === 'cupom' ? 'cupom' : 'estorno'} de {moeda(-c.envio.saldo)}. Requer dona ou gerente.</p>}
      {c.envio?.situacao === 'alteracao_aguardando_confirmacao' && <div>
        <Botao disabled={ocupado} onClick={() => confirmar(c.linkId, false)}>Manter pedido original</Botao>
        <Botao variante="primario" disabled={ocupado} onClick={() => confirmar(c.linkId, true)}>Confirmar alteração</Botao>
      </div>}
      {c.envio?.situacao === 'estorno_pendente' && <Botao disabled={ocupado} onClick={() => confirmar(c.linkId, true)}>Conferir devolução</Botao>}
    </div>)}
  </section>
}
