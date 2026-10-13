import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { moeda } from '../../dominio/formato'
import { partesDoSku, produtoDaPorcao } from '../../dominio/cardapio'
import { itensDoDia } from '../../dominio/cardapioLink'
import { clienteEscolhendoPeloLink, rascunhoMantendoEdicao, rotuloDoTotalDaSelecao } from '../../dominio/pedido'
import { useCatalogo } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { useComandasCliente } from '../../aplicacao/useComandasCliente'
import { useRascunhoDoAjuste } from '../../aplicacao/rascunhosDosAjustes'
import css from './comanda.module.css'

function AcertoDaComanda({ comanda: c }) {
  const f = c.financeiro, a = c.acerto, uso = c.envio?.cupomAplicado
  if (!f && !a && !uso) return null
  const situacao = { pendente: 'Em acompanhamento', confirmado: 'Confirmado', recusado: 'Não confirmado' }
  return <section className={css.comparativo} aria-label="Acerto da comanda">
    {f && <details className={css.historicoFinanceiro}>
      <summary>Histórico financeiro</summary>
      <p>Dinheiro recebido: {moeda(f.recebido)}</p>
      <p>Dinheiro devolvido: {moeda(f.devolvido)}</p>
      <p>Convertido em cupom: {moeda(f.convertidoEmCupom)}</p>
      <p>Cupom aplicado: {moeda(f.cupomAplicado)}</p>
      <p>Cobertura atual do pedido: {moeda(f.alocado)}</p>
    </details>}
    {f?.reservado > 0 && <p>Reservado para acerto: {moeda(f.reservado)}</p>}
    {f?.dinheiroDisponivel > 0 && <p>Disponível no pagamento para acerto: {moeda(f.dinheiroDisponivel)}</p>}
    {a && <>
      <b>{a.tipo === 'cupom' ? 'Cupom desta edição' : 'Devolução desta edição'} · {a.situacao === 'recusado' ? 'Precisa de conferência' : situacao[a.situacao] ?? 'Em acompanhamento'} · {moeda(a.valor)}</b>
      {a.codigoCupom && <p>Código do cupom: {a.codigoCupom}</p>}
      {a.tipo === 'cupom' && a.saldoCupom != null && <p>Saldo atual do cupom: {moeda(a.saldoCupom)}</p>}
      {a.devolucoes?.map(d => <p key={d.operacaoId}>{moeda(d.valor)} · {d.meio} · {situacao[d.situacao] ?? 'Em acompanhamento'}</p>)}
      {a.tipo === 'estorno' && a.situacao === 'confirmado' && <p>Confirmação registrada no provedor. O prazo para aparecer na conta depende do meio de pagamento.</p>}
      {a.situacao === 'pendente' && <p>O acerto ainda não foi confirmado. O cliente acompanha a atualização nesta mesma comanda.</p>}
      {a.situacao === 'recusado' && <p>O acerto ainda exige conferência. Confira os valores confirmados e não confirmados antes de solicitar outra devolução.</p>}
    </>}
    {uso && <>
      <b>Cupom aplicado neste envio</b>
      <p>{uso.codigo} · {moeda(uso.valor)} utilizados · Saldo atual: {moeda(uso.saldoCupom)}</p>
    </>}
  </section>
}

function SelecaoCompartilhada({ comanda: c, ocupado, salvar, confirmar, combinarFrete, cardapio, podeDevolver }) {
  const [edicao, setEdicao] = useRascunhoDoAjuste('comanda-cliente:' + c.linkId)
  const [prato, setPrato] = useState('')
  const [frete, setFrete] = useState('')
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
    {c.rascunho.endereco && <details className={css.enderecoCompartilhado} open={c.entregaACombinar}>
      <summary>Entrega · {c.rascunho.endereco.logradouro}, {c.rascunho.endereco.numero}</summary>
      <p>{c.rascunho.endereco.logradouro}, {c.rascunho.endereco.numero}
      {c.rascunho.endereco.complemento && ' · ' + c.rascunho.endereco.complemento} · {c.rascunho.endereco.bairro}
      {' · ' + c.rascunho.endereco.cidade + '/' + c.rascunho.endereco.uf + ' · CEP ' + c.rascunho.endereco.cep}</p>
    </details>}
    {c.entregaACombinar && <form onSubmit={e => { e.preventDefault(); combinarFrete(c, Number(frete)) }}>
      <p>O cliente quer combinar a entrega. Confira o endereço e o transporte antes de informar o frete.</p>
      <label>Frete combinado (R$) <input type="number" min="0" max="100000" step="0.01" required value={frete}
        disabled={ocupado} onChange={e => setFrete(e.target.value)} /></label>
      <Botao tipo="submit" disabled={ocupado || frete === ''} variante="primario">Confirmar frete combinado</Botao>
      <p>O cliente confere o total na comanda antes de confirmar e pagar.</p>
    </form>}
    {c.freteCombinado != null && !c.pedido && <p>Frete combinado: {moeda(c.freteCombinado)}. Aguarde a confirmação do cliente.</p>}
    <p className={css.estadoCompartilhado}>{c.podeEditar ? c.pedido ? 'O pedido confirmado continua valendo até a aprovação deste ajuste.'
      : 'Em montagem · aguardando confirmação do cliente.'
      : c.envio?.situacao === 'estorno_pendente' ? 'Devolução em acompanhamento.'
        : pendente ? 'Confira a alteração solicitada antes de aplicar ao pedido.' : 'Envio registrado. Compartilhe uma nova edição para ajustar.'}</p>
    {conflito && <p role="alert">A seleção mudou enquanto você editava. Sua edição foi preservada. Confira a seleção atual abaixo antes de continuar.</p>}
    {pendente && <div className={css.comparativo}>
      <b>Pedido atual · {moeda(c.pedido?.total ?? 0)}</b>
      <ul>{c.pedido?.itens?.map((i, n) => <li key={n}>{i.quantidade}× {i.nome}{i.observacao && ' · ' + i.observacao}</li>)}</ul>
      <b>Alteração do cliente · {moeda(c.totalPrevisto ?? c.envio.total)}</b>
    </div>}
    {conflito && <p>Seleção atual: {c.selecao.map(i => i.qtd + '× ' + i.nome + (i.observacao ? ' (' + i.observacao + ')' : '')).join(', ')}. Observação: {c.rascunho.observacoes || 'nenhuma'}.</p>}
    <p className={css.totalCompartilhado}>{rotuloDoTotalDaSelecao(c, Boolean(edicao))}: <strong>{moeda(c.totalPrevisto ?? 0)}</strong>{c.pedido && <> · Já coberto: {moeda(c.pedido.alocado ?? c.pedido.totalPago)}</>}</p>
    <ul>{rascunho.itens.map((i, n) => {
      const produto = catalogo.find(p => p.sku === (i.variacaoId ? i.cardapioItemId + '~' + i.variacaoId : i.cardapioItemId))
      const atual = c.selecao.find(p => p.cardapioItemId === i.cardapioItemId && (p.variacaoId ?? null) === (i.variacaoId ?? null))
      const nome = produto?.nome ?? atual?.nome ?? 'Prato indisponível'
      return <li key={i.cardapioItemId + ':' + i.variacaoId}>
        <div className={css.linhaCompartilhada}>
        <span><strong>{nome}</strong><small>{moeda(i.qtd * (atual?.preco ?? produto?.preco ?? 0))}</small></span>
        {c.podeEditar ? <>
          <span className={css.contador}>
            <button type="button" disabled={ocupado} aria-label={'Tirar uma unidade de ' + nome} onClick={() => mudarItem(n, { qtd: i.qtd - 1 })}>−</button>
            <b aria-label={'Quantidade de ' + nome}>{i.qtd}</b>
            <button type="button" disabled={ocupado || i.qtd >= 99} aria-label={'Adicionar uma unidade de ' + nome} onClick={() => mudarItem(n, { qtd: i.qtd + 1 })}>+</button>
          </span>
        </> : <b>{i.qtd}×</b>}
        </div>
        {c.podeEditar ? <details className={css.anotacaoCompartilhada}>
          <summary>Observação do prato{i.observacao && <small>{i.observacao}</small>}</summary>
          <label className={css.observacaoCompartilhada}>Observação de {nome}
            <input value={i.observacao ?? ''} maxLength={500} disabled={ocupado} onChange={e => mudarItem(n, { observacao: e.target.value })} />
          </label>
        </details> : i.observacao && <small>{i.observacao}</small>}
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
      <details className={css.anotacaoCompartilhada}>
      <summary>Observação do pedido{rascunho.observacoes && <small>{rascunho.observacoes}</small>}</summary>
      <label className={css.observacaoCompartilhada}>Observação para a cozinha
        <textarea value={rascunho.observacoes ?? ''} maxLength={2000} disabled={ocupado}
          onChange={e => editar({ ...rascunho, observacoes: e.target.value })} />
      </label>
      </details>
      {edicao && <div className={css.salvarCompartilhado}>
        <Botao disabled={ocupado} onClick={() => setEdicao(null)}>{conflito ? 'Usar seleção atual' : 'Descartar minha edição'}</Botao>
        {conflito ? <Botao disabled={ocupado || rascunho.pedidoId !== c.rascunho.pedidoId} onClick={() => setEdicao({ revisao: c.revisao,
          rascunho: rascunhoMantendoEdicao(c.rascunho, rascunho) })}>Manter minha edição para conferir</Botao>
          : <Botao variante="primario" disabled={ocupado} onClick={persistir}>Salvar seleção</Botao>}
      </div>}
      {edicao && <p>Alterações locais. Salve para o cliente ver; os valores finais serão conferidos pelo sistema.</p>}
    </> : c.rascunho.observacoes && <p>Observação: {c.rascunho.observacoes}</p>}
    {c.pedido && <p>{saldo < 0 ? 'A devolver: ' + moeda(-saldo) + ' em ' + (c.rascunho.acerto === 'cupom' ? 'cupom' : 'estorno') + '.' : saldo > 0 ? 'Mais a pagar: ' + moeda(saldo) + '.' : 'Sem diferença de pagamento.'}</p>}
    <AcertoDaComanda comanda={c} />
    {exigeGerente && !podeDevolver && <p>A dona ou um gerente precisa conferir esta devolução. Avise pelo atendimento.</p>}
    {pendente && <div>
      <Botao disabled={ocupado} onClick={() => confirmar(c.linkId, false)}>Manter pedido original</Botao>
      <Botao variante="primario" disabled={ocupado || exigeGerente && !podeDevolver} onClick={() => confirmar(c.linkId, true)}>Confirmar alteração</Botao>
    </div>}
    {c.envio?.situacao === 'estorno_pendente' && <Botao disabled={ocupado || !podeDevolver} onClick={() => confirmar(c.linkId, true)}>Conferir devolução</Botao>}
  </div>
}

export function ComandaDoCliente({ conversaId, numeroComanda, ativo, acaoCompartilhar, pedido = null, aoMudarEscolha }) {
  const { dados, erro, ocupado, confirmar, salvar, combinarFrete } = useComandasCliente(conversaId, ativo)
  const { cardapio } = useCatalogo()
  const { acoes } = useAcessoModulos()
  if (!ativo) return null
  const visiveis = dados.filter(c => c.podeEditar || ['alteracao_aguardando_confirmacao', 'estorno_pendente', 'estorno_recusado'].includes(c.envio?.situacao)
    || c.linkId === dados[0]?.linkId && c.envio)
  // #1683: avisa a Ficha, depois de pintar, se o cliente está escolhendo pelo link; a comanda de
  // baixo deixa de somar item nesse meio tempo. Valor igual não redesenha.
  const avisarEscolha = no => { if (no) aoMudarEscolha?.(clienteEscolhendoPeloLink(dados, pedido) ? conversaId : null) }
  return <section ref={avisarEscolha} className={css.clienteCompartilhado} aria-label="Comanda compartilhada com o cliente">
    <div className={css.adicionarCompartilhado}>
      <h3>{visiveis.some(c => c.pedido) ? 'Link do cliente' : 'Pedido em montagem'}<small>{numeroComanda ? 'Comanda ' + numeroComanda : 'Seleção compartilhada'}</small></h3>
      {acaoCompartilhar}
    </div>
    {erro && <p role="alert">{erro}</p>}
    {!visiveis.length && !erro && <p>Compartilhe com o cliente para escolherem juntos. A seleção chega aqui antes da confirmação.</p>}
    {visiveis.map(c => {
      const pendente = ['alteracao_aguardando_confirmacao', 'estorno_pendente', 'estorno_recusado'].includes(c.envio?.situacao)
      const selecao = <SelecaoCompartilhada key={c.linkId} comanda={c} ocupado={ocupado} salvar={salvar} confirmar={confirmar}
        combinarFrete={combinarFrete} cardapio={cardapio} podeDevolver={acoes.estornarPagamentoOnline === true} />
      return c.pedido ? <details key={c.linkId} className={css.historicoCompartilhado} open={c.podeEditar || pendente}>
        <summary>{pendente ? 'Ajuste para conferir' : c.podeEditar ? 'Seleção em edição' : 'Histórico do link'}</summary>
        {selecao}
      </details> : selecao
    })}
  </section>
}
