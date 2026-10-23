import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { SugestaoDeTexto } from '../../componentes/SugestaoDeTexto'
import { useComplementoDeTexto } from '../../aplicacao/useComplementoDeTexto'
import { Modal } from '../../componentes/Modal'
import { moeda } from '../../dominio/formato'
import { partesDoSku, produtoDaPorcao } from '../../dominio/cardapio'
import { itensDoDia } from '../../dominio/cardapioLink'
import { clienteEscolhendoPeloLink, rascunhoMantendoEdicao, rotuloDoTotalDaSelecao, saldoDaAlteracao, tituloDaComanda } from '../../dominio/pedido'
import { useCatalogo } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { congelado } from '../../dominio/congelados'
import { useComandasCliente, idDaJanela, janelaDoId } from '../../aplicacao/useComandasCliente'
import { useRascunhoDoAjuste } from '../../aplicacao/rascunhosDosAjustes'
import { ConsultaTransporte } from './BlocoFreteApi'
import { SeletorJanelaApi } from './SeletorJanelaApi'
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

function etapaDaSelecao(c) {
  // #1686-2: o link usado de pedido cancelado ou entregue não traz mais o pedido nem o saldo dele.
  if (!c.pedido && c.pedidoEncerrado) return {
    titulo: 'Histórico do link', acao: 'Consultar histórico',
    estado: c.pedidoEncerrado === 'cancelado' ? 'Pedido cancelado' : 'Pedido entregue',
    orientacao: 'O pedido deste link foi encerrado. Para uma nova compra, envie um link novo ao cliente.',
  }
  if (!c.pedido) return {
    titulo: 'Seleção do cliente', estado: 'Não confirmada', acao: c.podeEditar ? 'Revisar seleção' : 'Consultar seleção',
    orientacao: 'Salvar a seleção atualiza os itens no link. Para virar pedido, é preciso revisar e confirmar.',
  }
  if (c.podeEditar || c.envio?.situacao === 'alteracao_aguardando_confirmacao') return {
    titulo: c.podeEditar ? 'Alteração em edição' : 'Alteração para aprovar', estado: 'Pedido atual mantido',
    acao: c.podeEditar ? 'Revisar alteração' : 'Comparar e aprovar',
    orientacao: c.podeEditar ? 'Esta seleção ainda não altera o pedido. O cliente precisa enviar a alteração pelo link para você aprovar.'
      : 'O pedido atual continua valendo. Compare os itens antes de aprovar a alteração.',
  }
  return {
    titulo: 'Histórico do link', acao: 'Consultar histórico',
    estado: c.envio?.situacao === 'alteracao_recusada' ? 'Alteração recusada'
      : c.envio?.situacao === 'estorno_pendente' ? 'Devolução em acompanhamento'
        : c.envio?.situacao === 'estorno_recusado' ? 'Devolução para conferir' : 'Pedido criado',
    orientacao: 'Esta é a seleção enviada pelo link. Consulte o pedido atual abaixo; pagamento e entrega ficam no Resumo.',
  }
}

export function ConfirmacaoPedidoCompartilhado({ comanda: c, janela, ocupado, erro, desatualizado, aoFechar, aoConfirmar }) {
  const endereco = c.rascunho.endereco
  return <Modal titulo="Confirmar pedido com o cliente" aoFechar={ocupado ? undefined : aoFechar}
    rodape={<><Botao disabled={ocupado} onClick={aoFechar}>Voltar à seleção</Botao>
      <Botao variante="primario" disabled={ocupado || desatualizado} onClick={aoConfirmar}>Criar pedido</Botao></>}>
    <p>A seleção só vira pedido ao tocar em Criar pedido. Confira os itens e a entrega com o cliente. Se houver saldo, ele ficará aguardando pagamento.</p>
    <ul>{c.selecao.map(i => <li key={i.cardapioItemId + ':' + i.variacaoId}>{i.qtd}× {i.nome} · {moeda(i.qtd * i.preco)}{i.observacao && ' · ' + i.observacao}</li>)}</ul>
    {c.rascunho.observacoes && <p>Observação: {c.rascunho.observacoes}</p>}
    <p>Entrega: {endereco.logradouro}, {endereco.numero}{endereco.complemento && ' · ' + endereco.complemento} · {endereco.bairro} · {endereco.cidade}/{endereco.uf} · CEP {endereco.cep}</p>
    <p>Janela de entrega: <strong>{janela}</strong></p>
    <p>Frete combinado: <strong>{moeda(c.freteCombinado)}</strong></p>
    <p>Total com frete: <strong>{moeda(c.totalPrevisto)}</strong></p>
    {desatualizado && <p role="alert">A seleção mudou. Volte para conferir os dados atuais antes de criar o pedido.</p>}
    {erro && <p role="alert">{erro}</p>}
  </Modal>
}

export function SelecaoCompartilhada({ conversaId, comanda: c, ocupado, erro, salvar, confirmar, combinarFrete, encaminharEntrega, confirmarPedido, cardapio, podeDevolver }) {
  const [edicao, setEdicao] = useRascunhoDoAjuste('comanda-cliente:' + c.linkId)
  const [prato, setPrato] = useState('')
  const [frete, setFrete] = useState('')
  const [janelas, setJanelas] = useState([])
  const [confirmacao, setConfirmacao] = useState(null)
  const selecaoRef = useRef(null)
  const rascunho = c.podeEditar && edicao ? edicao.rascunho : c.rascunho
  const conflito = c.podeEditar && edicao && (edicao.revisao !== c.revisao
    || edicao.rascunho.pedidoAtualizadoEm !== c.rascunho.pedidoAtualizadoEm || edicao.rascunho.pedidoId !== c.rascunho.pedidoId)
  const catalogo = cardapio.flatMap(p => p.porcoes?.length ? p.porcoes.map(v => produtoDaPorcao(p, v)) : [p])
  function editar(novo) { setEdicao({ revisao: edicao?.revisao ?? c.revisao, base: edicao?.base ?? c.rascunho, rascunho: novo }) }
  const sugestao = useComplementoDeTexto({
    texto: rascunho.observacoes ?? '', contexto: 'cozinha', conversaId,
    ativo: Boolean(conversaId) && c.podeEditar && !ocupado,
    aoCompletar: observacoes => editar({ ...rascunho, observacoes }),
  })
  function mudarItem(indice, campos) {
    editar({ ...rascunho, itens: rascunho.itens.map((i, n) => n === indice ? { ...i, ...campos } : i).filter(i => i.qtd > 0) })
    if (campos.qtd === 0) selecaoRef.current?.focus()
  }
  function limparEdicao() { setEdicao(null); selecaoRef.current?.focus() }
  async function persistir() {
    if (await salvar({ ...c, revisao: edicao.revisao }, rascunho)) limparEdicao()
  }
  function adicionar() {
    const { itemSku, variacaoId } = partesDoSku(prato)
    if (!itemSku) return
    const indice = rascunho.itens.findIndex(i => i.cardapioItemId === itemSku && (i.variacaoId ?? null) === variacaoId)
    if (indice >= 0) mudarItem(indice, { qtd: Math.min(99, rascunho.itens[indice].qtd + 1) })
    else editar({ ...rascunho, itens: [...rascunho.itens, { cardapioItemId: itemSku, variacaoId, qtd: 1 }] })
    setPrato('')
  }
  const pendente = !c.pedidoEncerrado && c.envio?.situacao === 'alteracao_aguardando_confirmacao'
  // J2 (#1990): a diferença acompanha a edição antes de salvar (antes dizia "Sem diferença" com +R$ 38 na tela).
  const saldo = saldoDaAlteracao(c, edicao?.rascunho ?? null, catalogo)
  const naoSalvo = Boolean(edicao) && c.podeEditar && !c.pedidoEncerrado
  const resumoSaldo = (saldo < 0 ? 'A devolver: ' + moeda(-saldo) + ' em ' + (c.rascunho.acerto === 'cupom' ? 'cupom' : 'estorno') + '.'
    : saldo > 0 ? 'Mais a pagar: ' + moeda(saldo) + '.' : 'Sem diferença de pagamento.')
    + (naoSalvo ? ' Conta com os ajustes ainda não salvos.' : '')
  const exigeGerente = saldo < 0 || c.envio?.situacao === 'estorno_pendente'
  const janela = rascunho.janelaId && rascunho.dataEntrega ? idDaJanela(rascunho.janelaId, rascunho.dataEntrega) : null
  const rotuloJanela = janelas.find(j => j.id === janela)?.rotulo
  const etapa = etapaDaSelecao(c)
  const faltaConfirmar = edicao ? 'Salve os ajustes da seleção antes de confirmar o pedido.'
    : !rascunho.itens.length ? 'Adicione e salve os pratos antes de confirmar.'
      : !rascunho.endereco ? 'Peça o endereço ao cliente e registre na Ficha.'
        : !janela || !rotuloJanela ? 'Escolha uma janela de entrega disponível e salve a seleção.'
          : c.freteCombinado == null ? 'Abra “Combinar entrega” e confirme o frete antes de criar o pedido, inclusive se for R$ 0,00.' : null
  const enderecoCompartilhado = c.rascunho.endereco && <details className={css.enderecoCompartilhado}>
      <summary>Entrega · {c.rascunho.endereco.logradouro}, {c.rascunho.endereco.numero}</summary>
      <p>{c.rascunho.endereco.logradouro}, {c.rascunho.endereco.numero}
      {c.rascunho.endereco.complemento && ' · ' + c.rascunho.endereco.complemento} · {c.rascunho.endereco.bairro}
      {' · ' + c.rascunho.endereco.cidade + '/' + c.rascunho.endereco.uf + ' · CEP ' + c.rascunho.endereco.cep}</p>
    </details>
  return <section ref={selecaoRef} tabIndex={-1} aria-label="Seleção da comanda">
    <div className={css.etapaCompartilhada}>
      <strong>{etapa.estado}</strong><p>{etapa.orientacao}</p>
    </div>
    {c.ajudaSolicitada && <output className={css.ajudaCompartilhada}>
      <strong>Pedido de ajuda registrado</strong>
    </output>}
    {c.pedido && enderecoCompartilhado}
    {conflito && <p role="alert">A seleção mudou enquanto você editava. Sua edição foi preservada. Confira a seleção atual abaixo antes de continuar.</p>}
    {c.pedido && <details className={css.pedidoAtualCompartilhado} open={pendente}>
      <summary>Pedido atual · {moeda(c.pedido.total)}</summary>
      <ul>{c.pedido.itens?.map((i, n) => <li key={n}>{i.quantidade}× {i.nome}{i.observacao && ' · ' + i.observacao}</li>)}</ul>
      <p>Pagamento já coberto: {moeda(c.pedido.alocado ?? c.pedido.totalPago ?? 0)}</p>
    </details>}
    {c.pedido && <h3>{c.podeEditar || pendente ? 'Alteração proposta' : 'Seleção enviada pelo link'}</h3>}
    {conflito && <p>Seleção atual: {c.selecao.map(i => i.qtd + '× ' + i.nome + (i.observacao ? ' (' + i.observacao + ')' : '')).join(', ')}. Observação: {c.rascunho.observacoes || 'nenhuma'}.</p>}
    <p className={css.totalCompartilhado}>{c.pedido && !edicao ? (c.podeEditar || pendente ? 'Total proposto' : 'Total enviado pelo link') : rotuloDoTotalDaSelecao(c, Boolean(edicao))}: <strong>{moeda(c.totalPrevisto ?? 0)}</strong></p>
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
          onChange={e => editar({ ...rascunho, observacoes: e.target.value })}
          onFocus={sugestao.aoFocar} onBlur={sugestao.aoSair} onKeyDown={sugestao.aoTeclar} />
      </label>
      <SugestaoDeTexto sugestao={sugestao} />
      </details>
      {!c.pedido && <details className={css.entregaDaSelecao} open={!janela || c.entregaACombinar || c.freteCombinado == null}>
      <summary>Entrega e horário{rotuloJanela && ` · ${rotuloJanela}`}</summary>
      {enderecoCompartilhado}
      {c.freteCombinado != null && <p>Frete combinado: {moeda(c.freteCombinado)}.</p>}
      <SeletorJanelaApi key={c.revisao} editavel={!ocupado}
        pedido={{ itens: rascunho.itens.map(i => ({ sku: i.cardapioItemId })), janela }}
        dataInicial={c.rascunho.dataEntrega ?? ''} aoLerJanelas={setJanelas}
        aoEscolher={valor => { const escolhida = janelaDoId(valor); editar({ ...rascunho, janelaId: escolhida?.janelaId ?? null, dataEntrega: escolhida?.data ?? null }) }} />
      <details className={css.transporteCompartilhado} open={c.entregaACombinar}>
        <summary>Combinar entrega{!congelado('lalamove') && ' · consultar Lalamove'}</summary>
        <ConsultaTransporte semPedido />
        {!c.entregaACombinar && c.freteCombinado == null && <>
          <Botao disabled={ocupado || Boolean(edicao) || !c.rascunho.endereco || !c.rascunho.itens.length}
            onClick={() => encaminharEntrega(c)}>Combinar entrega desta seleção</Botao>
          {!c.rascunho.endereco ? <p>Peça o endereço ao cliente e registre na Ficha.</p>
            : !c.rascunho.itens.length && <p>Adicione e salve os pratos antes de combinar a entrega.</p>}
        </>}
        {c.entregaACombinar && <form className={css.freteCompartilhado} onSubmit={e => { e.preventDefault(); combinarFrete(c, Number(frete)) }}>
          <p>Confira o transporte e combine o frete com o cliente.</p>
          <label>Frete combinado (R$) <input type="number" min="0" max="100000" step="0.01" required value={frete}
            disabled={ocupado} onChange={e => setFrete(e.target.value)} /></label>
          <Botao tipo="submit" disabled={ocupado || Boolean(edicao) || frete === ''} variante="primario">Confirmar frete combinado</Botao>
        </form>}
        {edicao && <p>Salve os ajustes da seleção antes de combinar a entrega.</p>}
      </details>
      </details>}
      {edicao && <div className={css.salvarCompartilhado}>
        <Botao disabled={ocupado} onClick={limparEdicao}>{conflito ? 'Usar seleção atual' : 'Descartar minha edição'}</Botao>
        {conflito ? <Botao disabled={ocupado || rascunho.pedidoId !== c.rascunho.pedidoId} onClick={() => setEdicao({ revisao: c.revisao, base: c.rascunho,
          rascunho: rascunhoMantendoEdicao(c.rascunho, rascunho, edicao.base) })}>Manter minha edição para conferir</Botao>
          : <Botao variante="primario" disabled={ocupado} onClick={persistir}>Salvar seleção</Botao>}
      </div>}
      {edicao && <output className={css.avisoCompartilhado}>Alterações ainda não salvas. Salvar seleção atualiza o link{c.pedido ? ', sem alterar o pedido atual.' : ', sem criar um pedido.'}</output>}
      {!c.pedido && !edicao && <div className={css.confirmarCompartilhado}>
        <Botao variante="primario" disabled={ocupado || Boolean(faltaConfirmar)}
          onClick={() => setConfirmacao({ comanda: c, janela: rotuloJanela })}>Revisar e criar pedido</Botao>
        {faltaConfirmar && <p>{faltaConfirmar}</p>}
      </div>}
    </> : c.rascunho.observacoes && <p>Observação: {c.rascunho.observacoes}</p>}
    {c.pedido && <p>{c.podeEditar || pendente ? 'Após aprovar a alteração: ' + resumoSaldo : resumoSaldo}</p>}
    <AcertoDaComanda comanda={c} />
    {exigeGerente && !podeDevolver && <p>A dona ou um gerente precisa conferir esta devolução. Avise pelo atendimento.</p>}
    {pendente && <div>
      <Botao disabled={ocupado} onClick={() => confirmar(c.linkId, false)}>Manter pedido original</Botao>
      <Botao variante="primario" disabled={ocupado || exigeGerente && !podeDevolver} onClick={() => confirmar(c.linkId, true)}>Confirmar alteração</Botao>
    </div>}
    {c.envio?.situacao === 'estorno_pendente' && <Botao disabled={ocupado || !podeDevolver} onClick={() => confirmar(c.linkId, true)}>Conferir devolução</Botao>}
    {confirmacao && <ConfirmacaoPedidoCompartilhado {...confirmacao} ocupado={ocupado} erro={erro}
      desatualizado={confirmacao.comanda.revisao !== c.revisao || !c.podeEditar || Boolean(c.pedido)}
      aoFechar={() => setConfirmacao(null)} aoConfirmar={async () => { if (await confirmarPedido(confirmacao.comanda)) setConfirmacao(null) }} />}
  </section>
}

// #1761 (L10): pedido vazio eram três textos em dois blocos. Fica um bloco: o título, "Adicionar
// itens" no topo (`acaoMontar`, que a Ficha entrega) e uma frase.
function ComandaCompacta({ pedido, visiveis, erro, carregando, dados, acaoCompartilhar, acaoMontar, ocupado, selecaoDe, nomeCliente, atualizar }) {
  const [linkAberto, setLinkAberto] = useState(null)
  if (pedido && !visiveis.length && !erro) return null
  const aberta = visiveis.find(c => c.linkId === linkAberto)
  return <section className={css.clienteCompartilhado} aria-label="Comanda compartilhada com o cliente">
    <div className={css.adicionarCompartilhado}><h3>Link do cliente</h3>{acaoCompartilhar}</div>
    {erro && !aberta && <><p role="alert">{erro}</p><Botao onClick={atualizar} disabled={ocupado || carregando}>Atualizar comanda</Botao></>}
    {carregando && !dados.length ? <output className={css.avisoCompartilhado}>Consultando a comanda…</output> : <>
      {acaoMontar}
      {!visiveis.length && !erro && !pedido && <p>O cliente também pode escolher pelo Link de compra.</p>}
      {visiveis.map(c => {
        const etapa = etapaDaSelecao(c)
        const quantidade = c.selecao.reduce((soma, item) => soma + item.qtd, 0)
        return <div key={c.linkId} className={css.resumoCompartilhado}>
          <strong>{etapa.titulo}</strong>
          <span className={css.estadoDaSelecao}>{etapa.estado}</span>
          {!c.pedidoEncerrado && (!c.pedido || c.podeEditar || c.envio?.situacao === 'alteracao_aguardando_confirmacao') && <p>
            {quantidade} {quantidade === 1 ? 'item' : 'itens'}{c.totalPrevisto != null && ` · ${c.pedido ? 'Proposta' : 'Seleção'}: ${moeda(c.totalPrevisto)}`}
          </p>}
          {c.ajudaSolicitada && <p>Cliente pediu ajuda</p>}
          {c.entregaACombinar && <p>Falta combinar a entrega</p>}
          <Botao largo aria-haspopup="dialog" onClick={() => setLinkAberto(c.linkId)}>
            {etapa.acao}
          </Botao>
        </div>
      })}
    </>}
    {aberta && <Modal titulo={`${etapaDaSelecao(aberta).titulo}${nomeCliente ? ` · ${nomeCliente}` : ''}`} largura="760px"
      aoFechar={ocupado ? undefined : () => setLinkAberto(null)}>
      {erro && <><p role="alert">{erro}</p><Botao onClick={atualizar} disabled={ocupado || carregando}>Atualizar comanda</Botao></>}
      {selecaoDe(aberta)}
    </Modal>}
  </section>
}

export function ComandaDoCliente({ conversaId, numeroComanda, ativo, acaoCompartilhar, acaoMontar = null, pedido = null, aoMudarEscolha, compacta = false, nomeCliente }) {
  const { dados, erro, ocupado, carregando, confirmar, salvar, combinarFrete, encaminharEntrega, confirmarPedido, atualizar } = useComandasCliente(conversaId, ativo)
  const { cardapio } = useCatalogo()
  const { acoes } = useAcessoModulos()
  const escolhendo = clienteEscolhendoPeloLink(dados, pedido)
  useEffect(() => { aoMudarEscolha?.(escolhendo ? conversaId : null) }, [aoMudarEscolha, escolhendo, conversaId])
  if (!ativo) return null
  const visiveis = dados.filter(c => c.podeEditar || ['alteracao_aguardando_confirmacao', 'estorno_pendente', 'estorno_recusado'].includes(c.envio?.situacao)
    || c.linkId === dados[0]?.linkId && c.envio)
  const selecaoDe = c => <SelecaoCompartilhada key={c.linkId} conversaId={conversaId} comanda={c} ocupado={ocupado} erro={erro} salvar={salvar} confirmar={confirmar}
    combinarFrete={combinarFrete} encaminharEntrega={encaminharEntrega} confirmarPedido={confirmarPedido} cardapio={cardapio} podeDevolver={acoes.estornarPagamentoOnline === true} />
  if (compacta) return <ComandaCompacta pedido={pedido} visiveis={visiveis} erro={erro} carregando={carregando} dados={dados}
    acaoCompartilhar={acaoCompartilhar} acaoMontar={acaoMontar} ocupado={ocupado} selecaoDe={selecaoDe} nomeCliente={nomeCliente} atualizar={atualizar} />
  return <section className={css.clienteCompartilhado} aria-label="Comanda compartilhada com o cliente">
    <div className={css.adicionarCompartilhado}>
      <h3>{tituloDaComanda(pedido, { temLinkComPedido: visiveis.some(c => c.pedido) })}<small>{numeroComanda ? 'Comanda ' + numeroComanda : 'Seleção compartilhada'}</small></h3>
      {acaoCompartilhar}
    </div>
    {erro && <p role="alert">{erro}</p>}
    {acaoMontar}
    {!visiveis.length && !erro && !pedido?.pedidoId && <p>Compartilhe com o cliente pelo Link de compra ou adicione os itens por aqui.</p>}
    {visiveis.map(c => {
      const pendente = (!c.pedidoEncerrado || c.envio?.situacao !== 'alteracao_aguardando_confirmacao')
        && ['alteracao_aguardando_confirmacao', 'estorno_pendente', 'estorno_recusado'].includes(c.envio?.situacao)
      const selecao = selecaoDe(c)
      return c.pedido || c.pedidoEncerrado ? <details key={c.linkId} className={css.historicoCompartilhado} open={c.podeEditar || pendente}>
        <summary>{pendente ? 'Ajuste para conferir' : c.podeEditar ? 'Seleção em edição' : 'Histórico do link'}</summary>
        {selecao}
      </details> : selecao
    })}
  </section>
}
