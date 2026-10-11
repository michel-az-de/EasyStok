import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { moeda } from '../../dominio/formato'
import { partesDoSku, produtoDaPorcao } from '../../dominio/cardapio'
import { itensDoDia } from '../../dominio/cardapioLink'
import { useCatalogo } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { useComandasCliente } from '../../aplicacao/useComandasCliente'
import css from './comanda.module.css'

function SelecaoCompartilhada({ comanda: c, ocupado, salvar, confirmar, cardapio, podeDevolver }) {
  const [edicao, setEdicao] = useState(null)
  const [prato, setPrato] = useState('')
  const rascunho = c.podeEditar && edicao ? edicao.rascunho : c.rascunho
  const conflito = c.podeEditar && edicao && (edicao.revisao !== c.revisao
    || edicao.rascunho.pedidoAtualizadoEm !== c.rascunho.pedidoAtualizadoEm || edicao.rascunho.pedidoId !== c.rascunho.pedidoId)
  const catalogo = cardapio.flatMap(p => p.porcoes?.length ? p.porcoes.map(v => produtoDaPorcao(p, v)) : [p])
  function editar(novo) { setEdicao({ revisao: edicao?.revisao ?? c.revisao, rascunho: novo }) }
  function mudarItem(indice, campos) {
    editar({ ...rascunho, itens: rascunho.itens.map((i, n) => n === indice ? { ...i, ...campos } : i).filter(i => i.qtd > 0) })
  }
  async function persistir() {
    if (await salvar({ ...c, revisao: edicao.revisao }, rascunho)) setEdicao(null)
  }
  function adicionar() {
    const { itemSku, variacaoId } = partesDoSku(prato)
    if (!itemSku) return
    const indice = rascunho.itens.findIndex(i => i.cardapioItemId === itemSku && (i.variacaoId ?? null) === variacaoId)
    if (indice >= 0) mudarItem(indice, { qtd: Math.min(99, rascunho.itens[indice].qtd + 1) })
    else editar({ ...rascunho, itens: [...rascunho.itens, { cardapioItemId: itemSku, variacaoId, qtd: 1 }] })
    setPrato('')
  }
  const pendente = c.envio?.situacao === 'alteracao_aguardando_confirmacao'
  const saldo = c.envio?.situacao === 'estorno_pendente' ? c.envio.saldo
    : (c.podeEditar || pendente ? c.totalPrevisto ?? 0 : c.pedido?.total ?? c.totalPrevisto ?? 0) - (c.pedido?.alocado ?? c.pedido?.totalPago ?? 0)
  const exigeGerente = saldo < 0 || c.envio?.situacao === 'estorno_pendente'
  return <div>
    <p>{c.podeEditar ? 'Cliente e atendimento acompanham esta seleção. Só vira pedido após a confirmação do cliente.'
      : c.envio?.situacao === 'estorno_pendente' ? 'Devolução em acompanhamento.'
        : pendente ? 'Confira a alteração solicitada antes de aplicar ao pedido.' : 'Envio registrado. Compartilhe uma nova edição para ajustar.'}</p>
    {conflito && <p role="alert">A seleção mudou enquanto você editava. Sua edição foi preservada. Confira a seleção atual abaixo antes de continuar.</p>}
    {pendente && <div className={css.comparativo}>
      <b>Pedido atual · {moeda(c.pedido?.total ?? 0)}</b>
      <ul>{c.pedido?.itens?.map((i, n) => <li key={n}>{i.quantidade}× {i.nome}{i.observacao && ' · ' + i.observacao}</li>)}</ul>
      <b>Alteração do cliente · {moeda(c.totalPrevisto ?? c.envio.total)}</b>
    </div>}
    {conflito && <p>Seleção atual: {c.selecao.map(i => i.qtd + '× ' + i.nome + (i.observacao ? ' (' + i.observacao + ')' : '')).join(', ')}. Observação: {c.rascunho.observacoes || 'nenhuma'}.</p>}
    <ul>{rascunho.itens.map((i, n) => {
      const produto = catalogo.find(p => p.sku === (i.variacaoId ? i.cardapioItemId + '~' + i.variacaoId : i.cardapioItemId))
      const atual = c.selecao.find(p => p.cardapioItemId === i.cardapioItemId && (p.variacaoId ?? null) === (i.variacaoId ?? null))
      const nome = produto?.nome ?? atual?.nome ?? 'Prato indisponível'
      return <li key={i.cardapioItemId + ':' + i.variacaoId}>
        <span>{i.qtd}× {nome} · {moeda(i.qtd * (atual?.preco ?? produto?.preco ?? 0))}</span>
        {c.podeEditar ? <>
          <span className={css.contador}>
            <button type="button" disabled={ocupado} aria-label={'Tirar uma unidade de ' + nome} onClick={() => mudarItem(n, { qtd: i.qtd - 1 })}>−</button>
            <button type="button" disabled={ocupado || i.qtd >= 99} aria-label={'Adicionar uma unidade de ' + nome} onClick={() => mudarItem(n, { qtd: i.qtd + 1 })}>+</button>
          </span>
          <label className={css.observacaoCompartilhada}>Observação de {nome}
            <input value={i.observacao ?? ''} maxLength={500} disabled={ocupado} onChange={e => mudarItem(n, { observacao: e.target.value })} />
          </label>
        </> : i.observacao && <small>{i.observacao}</small>}
      </li>
    })}</ul>
    {c.podeEditar ? <>
      <div className={css.adicionarCompartilhado}>
        <label>Adicionar prato
          <select value={prato} disabled={ocupado} onChange={e => setPrato(e.target.value)}>
            <option value="">Escolha um prato</option>
            {itensDoDia(catalogo).map(p => <option key={p.sku} value={p.sku}>{p.nome} · {moeda(p.preco)}</option>)}
          </select>
        </label>
        <Botao disabled={ocupado || !prato} onClick={adicionar}>Adicionar</Botao>
      </div>
      <label className={css.observacaoCompartilhada}>Observação para a cozinha
        <textarea value={rascunho.observacoes ?? ''} maxLength={2000} disabled={ocupado}
          onChange={e => editar({ ...rascunho, observacoes: e.target.value })} />
      </label>
      {edicao && <div>
        <Botao disabled={ocupado} onClick={() => setEdicao(null)}>{conflito ? 'Usar seleção atual' : 'Descartar minha edição'}</Botao>
        {conflito ? <Botao disabled={ocupado || rascunho.pedidoId !== c.rascunho.pedidoId} onClick={() => setEdicao({ revisao: c.revisao,
          rascunho: { ...rascunho, pedidoId: c.rascunho.pedidoId, pedidoAtualizadoEm: c.rascunho.pedidoAtualizadoEm } })}>Manter minha edição para conferir</Botao>
          : <Botao variante="primario" disabled={ocupado} onClick={persistir}>Salvar seleção</Botao>}
      </div>}
      {edicao && <p>Alterações locais. Salve para o cliente ver; os valores finais serão conferidos pelo sistema.</p>}
    </> : c.rascunho.observacoes && <p>Observação: {c.rascunho.observacoes}</p>}
    <p>Total {edicao ? 'da seleção salva' : 'previsto'}: <strong>{moeda(c.totalPrevisto ?? 0)}</strong>{c.pedido && <> · Já coberto: {moeda(c.pedido.alocado ?? c.pedido.totalPago)}</>}</p>
    {c.pedido && <p>{saldo < 0 ? 'A devolver: ' + moeda(-saldo) + ' em ' + (c.rascunho.acerto === 'cupom' ? 'cupom' : 'estorno') + '.' : saldo > 0 ? 'Mais a pagar: ' + moeda(saldo) + '.' : 'Sem diferença de pagamento.'}</p>}
    {exigeGerente && !podeDevolver && <p>A dona ou um gerente precisa conferir esta devolução. Avise pelo atendimento.</p>}
    {pendente && <div>
      <Botao disabled={ocupado} onClick={() => confirmar(c.linkId, false)}>Manter pedido original</Botao>
      <Botao variante="primario" disabled={ocupado || exigeGerente && !podeDevolver} onClick={() => confirmar(c.linkId, true)}>Confirmar alteração</Botao>
    </div>}
    {c.envio?.situacao === 'estorno_pendente' && <Botao disabled={ocupado || !podeDevolver} onClick={() => confirmar(c.linkId, true)}>Conferir devolução</Botao>}
  </div>
}

export function ComandaDoCliente({ conversaId, numeroComanda, ativo, acaoCompartilhar }) {
  const { dados, erro, ocupado, confirmar, salvar } = useComandasCliente(conversaId, ativo)
  const { cardapio } = useCatalogo()
  const { acoes } = useAcessoModulos()
  if (!ativo) return null
  const visiveis = dados.filter(c => c.podeEditar || ['alteracao_aguardando_confirmacao', 'estorno_pendente'].includes(c.envio?.situacao)
    || c.linkId === dados[0]?.linkId && c.envio)
  return <section className={css.clienteCompartilhado} aria-label="Comanda compartilhada com o cliente">
    <div className={css.adicionarCompartilhado}><b>{numeroComanda ? 'Comanda ' + numeroComanda : 'Comanda do cliente'}</b>{acaoCompartilhar}</div>
    {erro && <p role="alert">{erro}</p>}
    {!visiveis.length && !erro && <p>Compartilhe com o cliente para escolherem juntos. A seleção chega aqui antes da confirmação.</p>}
    {visiveis.map(c => <SelecaoCompartilhada key={c.linkId} comanda={c} ocupado={ocupado} salvar={salvar} confirmar={confirmar}
      cardapio={cardapio} podeDevolver={acoes.estornarPagamentoOnline === true} />)}
  </section>
}
