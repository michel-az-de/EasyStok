import { useEffect, useRef, useState } from 'react'
import { Botao } from './Botao'
import { CampoSelecao, CampoTexto } from './Campo'
import { Modal } from './Modal'
import css from './ModalFrete.module.css'

const moeda = (centavos) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(centavos / 100)
const horario = (valor) => valor ? new Date(valor).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone: 'America/Sao_Paulo' }) : ''
const NOMES = { uber: 'Uber Direct', uber_direct: 'Uber Direct', lalamove: 'Lalamove', '99': '99 Entrega', '99entrega': '99 Entrega', manual: 'Entregador combinado', em_maos: 'Entrega em mãos' }
const nome = (provedor) => NOMES[provedor] ?? provedor
const SITUACOES = { agendado: 'Aguardando coleta', a_caminho: 'A caminho', entregue: 'Entregue', falha: 'Precisa de atenção', devolvendo: 'Voltando à loja', devolvido: 'Devolvido à loja', cancelado: 'Cancelado' }
const ENCERRADOS = new Set(['entregue', 'devolvido', 'cancelado'])

export function ModalFrete({ controlador: c }) {
  if (!c.entrada) return null
  return <Modal titulo="Organizar entrega" descricao={c.entrada.titulo} largura="920px" aoFechar={c.ocupado ? undefined : c.fechar}
    rodape={<Botao disabled={c.ocupado} onClick={c.fechar}>Fechar</Botao>}>
    <div className={css.conteudo} aria-busy={c.ocupado}>
      {c.contexto?.demonstracao && <p className={css.aviso}>Ambiente de demonstração. Cotações e eventos dos serviços são simulados. Nenhum entregador externo será chamado.</p>}
      {c.erro && <p role="alert" className={css.erro}>{c.erro}</p>}
      {c.aviso && <output className={css.sucesso}>{c.aviso}</output>}
      {!c.contexto && <div className={css.linha}><p>{c.ocupado ? 'Carregando pedidos e transportes…' : 'Os dados não carregaram.'}</p>
        {!c.ocupado && <Botao onClick={c.atualizar}>Tentar novamente</Botao>}</div>}
      {c.contexto && <Conteudo controlador={c} />}
      {c.ocupado && <output className={css.apoio}>Confirmando com o servidor…</output>}
    </div>
  </Modal>
}

function Conteudo({ controlador: c }) {
  const cotacoesRef = useRef(null)
  useEffect(() => { if (c.cotacoes) cotacoesRef.current?.focus() }, [c.cotacoes])
  const todos = c.contexto.pedidos ?? []
  const idsDaEntrada = new Set(c.entrada.pedidos.map((p) => p.id))
  const despachos = (c.contexto.despachos ?? []).filter((d) => idsDaEntrada.size === 0 || d.pedidoIds.some((id) => idsDaEntrada.has(id) || c.selecionados.includes(id)))
  const ocupados = new Set((c.contexto.despachos ?? []).filter((d) => !['cancelado', 'devolvido'].includes(d.status)).flatMap((d) => d.pedidoIds))
  const elegiveis = c.selecionados.length > 0 && c.selecionados.every((id) => todos.some((p) => p.id === id) && !ocupados.has(id))
  const disponiveis = (c.cotacoes ?? []).filter((q) => !q.indisponibilidade)
  const menor = disponiveis.length ? Math.min(...disponiveis.map((q) => q.valorCentavos)) : null
  const pedidos = [...todos].sort((a, b) => Number(idsDaEntrada.has(b.id)) - Number(idsDaEntrada.has(a.id)))
  return <>
    <details key={c.cotacoes ? 'cotado' : 'selecionar'} className={css.politica} open={!c.cotacoes && despachos.length === 0}>
      <summary>1. Pedidos para transportar · {c.selecionados.length} {c.selecionados.length === 1 ? 'selecionado' : 'selecionados'}</summary>
      <section className={css.secao} aria-label="Pedidos para transportar">
      <p className={css.apoio}>Agrupe antes de contratar. Uma entrega já contratada não recebe novos pedidos aqui.</p>
      {pedidos.length === 0 && <p>Nenhum pedido disponível neste dia.</p>}
      {pedidos.length === 0 && c.contexto.demonstracao && c.entrada.dia && c.podeConfigurar && <Botao disabled={c.ocupado} onClick={c.prepararDia}>Preparar dia de teste com 10 pedidos</Botao>}
      <ul className={css.lista}>{pedidos.map((p) => <li key={p.id}>
        <label className={css.pedido} aria-label={`Pedido ${p.numero}, ${p.cliente}`}><input type="checkbox" checked={c.selecionados.includes(p.id)} disabled={c.ocupado || (ocupados.has(p.id) && !c.selecionados.includes(p.id))} onChange={() => c.selecionar(p.id)} />
          <span><strong>{p.numero} · {p.cliente}</strong><span className={css.apoio}>{p.endereco || 'Endereço não informado'}</span>
            <span className={css.apoio}>{p.janelaInicio && p.janelaFim ? `Receber entre ${horario(p.janelaInicio)} e ${horario(p.janelaFim)}` : 'Horário não informado'}{ocupados.has(p.id) ? ' · Transporte já contratado' : ''}</span>
          </span>
        </label>
      </li>)}</ul>
      <label className={css.marcador}><input type="checkbox" checked={c.agrupar} disabled={c.ocupado} onChange={(e) => c.mudarAgrupamento(e.target.checked)} />Buscar uma entrega com várias paradas</label>
      <label className={css.marcador}><input type="checkbox" checked={c.incluirManual} disabled={c.ocupado} onChange={(e) => c.mudarManual(e.target.checked)} />Incluir entregador combinado e entrega em mãos</label>
      {c.incluirManual && <CampoTexto rotulo="Custo combinado com o entregador (R$, opcional)" tipo="number" min="0" max="1000" step="0.01" value={c.valorManual} disabled={c.ocupado}
        dica="Valor combinado por telefone ou em outro aplicativo, inclusive 99, Uber ou Lalamove. Entrega em mãos tem custo zero."
        onChange={(e) => c.mudarValorManual(e.target.value)} />}
      <div className={css.linha}><Botao variante="primario" disabled={c.ocupado || !elegiveis || (c.incluirManual && c.valorManual !== '' && (!Number.isFinite(Number(c.valorManual)) || Number(c.valorManual) < 0 || Number(c.valorManual) > 1000))} onClick={c.cotar}>Comparar transportes</Botao>
        {c.contexto.politica?.automatico && <Botao disabled={c.ocupado || !elegiveis} onClick={c.automatico}>Escolher e contratar até {moeda(c.contexto.politica.tetoCentavos)}</Botao>}
        {!elegiveis && <span className={css.apoio}>Selecione pedidos sem transporte contratado.</span>}</div>
      </section>
    </details>
    {c.cotacoes && <section ref={cotacoesRef} tabIndex={-1} className={css.secao} aria-label="Cotações de transporte">
      <h3>2. Compare custo e prazo</h3>
      <p className={css.apoio}>Este é o custo do transportador. O frete cobrado do cliente permanece separado.</p>
      {c.cotacoes.length === 0 && <p>Nenhuma cotação disponível. Tente novamente ou combine uma entrega manual.</p>}
      <div className={css.cotacoes}>{c.cotacoes.map((q) => <article key={q.id} className={css.cotacao} data-recomendada={!q.indisponibilidade && q.valorCentavos === menor}>
        <strong>{nome(q.provedor)}</strong>
        {q.simulado && <span className={css.apoio}>Cotação simulada</span>}
        {q.indisponibilidade ? <p>{q.indisponibilidade}</p> : <>
          <span className={css.preco}>{moeda(q.valorCentavos)}</span>
          <span>{['manual', 'em_maos'].includes(q.provedor) ? 'Horário combinado por você' : `${q.duracaoMinutos} min estimados`}</span>
          <span className={css.apoio}>Válida até {horario(q.expiraEm)}</span>
          {q.valorCentavos === menor && <span className={css.apoio}>Menor custo disponível</span>}
          <Botao disabled={c.ocupado} onClick={() => c.escolher(q)}>Escolher {nome(q.provedor)}</Botao>
        </>}
      </article>)}</div>
    </section>}
    {c.escolhida && <section className={css.confirmacao} aria-label="Confirmar contratação">
      <h3>Confirmar {nome(c.escolhida.provedor)}</h3>
      <p>{c.escolhida.pedidoIds.length} {c.escolhida.pedidoIds.length === 1 ? 'pedido' : 'pedidos'} · <strong>{moeda(c.escolhida.valorCentavos)}</strong></p>
      <ol className={css.historico} aria-label="Ordem das entregas">{c.escolhida.pedidoIds.map((id) => {
        const p = todos.find((pedido) => pedido.id === id)
        return <li key={id}>{p ? `${p.numero} · ${p.cliente} · ${p.endereco}` : 'Pedido selecionado'}</li>
      })}</ol>
      <p className={css.apoio}>O pagamento do transporte é registrado separadamente no caixa, após a confirmação.</p>
      <div className={css.linha}><Botao variante="primario" disabled={c.ocupado} onClick={c.contratar}>{c.escolhida.simulado ? 'Confirmar contratação simulada' : 'Confirmar contratação'}</Botao>
        <Botao variante="texto" disabled={c.ocupado} onClick={() => c.escolher(null)}>Voltar às opções</Botao></div>
    </section>}
    {despachos.length > 0 && <section className={css.secao} aria-label="Acompanhamento de entregas">
      <div className={css.linha}><h3 className={css.cresce}>Acompanhe a entrega</h3><Botao variante="texto" disabled={c.ocupado} onClick={c.atualizar}>Atualizar</Botao></div>
      {despachos.map((d) => <Despacho key={d.id} despacho={d} controlador={c} />)}
    </section>}
    {c.contexto.politica && c.podeConfigurar && <Politica key={JSON.stringify(c.contexto.politica)} politica={c.contexto.politica} controlador={c} />}
  </>
}

function Despacho({ despacho: d, controlador: c }) {
  const [acao, setAcao] = useState(null)
  const [motivo, setMotivo] = useState('')
  const [meio, setMeio] = useState('pix')
  const [referencia, setReferencia] = useState('')
  const terminado = ENCERRADOS.has(d.status)
  const manual = ['manual', 'em_maos'].includes(d.provedor)
  const podeEvento = d.simulado || manual
  const rastreioSeguro = d.rastreioUrl && (/^https:\/\//i.test(d.rastreioUrl)
    || /^\/api\/entregas-demonstracao\/[a-z\d.]+$/i.test(d.rastreioUrl))
  const confirmar = async (e) => {
    e.preventDefault()
    if (acao === 'pagamento') await c.pagar(d.id, meio, referencia.trim())
    else await c.evento(d.id, acao, motivo.trim() || null)
  }
  return <article className={css.viagem} data-despacho-id={d.id}>
    <div className={css.linha}><strong className={css.cresce}>{nome(d.provedor)} · {SITUACOES[d.status] ?? d.status}</strong><strong>{moeda(d.valorCentavos)}</strong></div>
    <div className={css.trilha} aria-hidden="true"><span style={{ width: d.status === 'entregue' ? '100%' : d.status === 'a_caminho' ? '60%' : '15%' }} /></div>
    <p className={css.apoio}>{d.pedidoIds.length} {d.pedidoIds.length === 1 ? 'pedido' : 'pedidos'} · {d.pago ? 'Pagamento registrado no caixa' : 'Pagamento ainda não registrado'}{d.simulado ? ' · Serviço simulado' : ''}</p>
    {d.status === 'entregue' && <p className={css.sucesso}>Entrega concluída.</p>}
    {rastreioSeguro && <a href={d.rastreioUrl} target="_blank" rel="noreferrer">{c.contexto.demonstracao ? 'Rastreio de demonstração' : 'Abrir rastreio do transportador'}</a>}
    <div className={css.linha}>
      {!d.pago && d.valorCentavos > 0 && c.podePagar && <Botao disabled={c.ocupado} onClick={() => setAcao('pagamento')}>Registrar pagamento</Botao>}
      {podeEvento && !terminado && <>
        {['agendado', 'falha'].includes(d.status) && <Botao disabled={c.ocupado} onClick={() => setAcao('a_caminho')}>{d.status === 'falha' ? 'Retomar entrega' : d.simulado ? 'Simular saída' : 'Saiu para entrega'}</Botao>}
        {d.status === 'a_caminho' && <Botao disabled={c.ocupado} onClick={() => setAcao('entregue')}>{d.simulado ? 'Simular entrega' : 'Confirmar entrega'}</Botao>}
        {d.status === 'a_caminho' && <Botao variante="texto" disabled={c.ocupado} onClick={() => setAcao('falha')}>Registrar problema</Botao>}
        {['a_caminho', 'falha'].includes(d.status) && <Botao variante="texto" disabled={c.ocupado} onClick={() => setAcao('devolvendo')}>Iniciar retorno à loja</Botao>}
        {d.status === 'devolvendo' && <Botao disabled={c.ocupado} onClick={() => setAcao('devolvido')}>Confirmar retorno à loja</Botao>}
        {d.status === 'agendado' && <Botao variante="texto" disabled={c.ocupado} onClick={() => setAcao('cancelado')}>Cancelar transporte</Botao>}
      </>}
      {podeEvento && d.status === 'entregue' && <Botao variante="texto" disabled={c.ocupado} onClick={() => setAcao('devolvendo')}>Iniciar retorno à loja</Botao>}
    </div>
    {acao && !(acao === 'pagamento' && d.pago) && !(acao === d.status) && <form className={css.confirmacao} onSubmit={confirmar}>
      <h3>{acao === 'pagamento' ? 'Registrar valor já pago ao transportador' : SITUACOES[acao]}</h3>
      {acao === 'pagamento' ? <><p>Será registrada uma saída de {moeda(d.valorCentavos)} no caixa. Confirme apenas depois de pagar.</p>
        <div className={css.formulario}><CampoSelecao rotulo="Meio usado" value={meio} disabled={c.ocupado} onChange={(e) => setMeio(e.target.value)} opcoes={[{ valor: 'pix', rotulo: 'Pix' }, { valor: 'dinheiro', rotulo: 'Dinheiro' }, { valor: 'credito', rotulo: 'Cartão de crédito' }, { valor: 'debito', rotulo: 'Cartão de débito' }, { valor: 'transferencia', rotulo: 'Transferência' }, { valor: 'outro', rotulo: 'Outro' }]} />
          <CampoTexto rotulo="Referência do pagamento" value={referencia} maxLength={120} required disabled={c.ocupado} onChange={(e) => setReferencia(e.target.value)} /></div></>
        : <><CampoTexto rotulo="Motivo ou observação" value={motivo} maxLength={500} required={['falha', 'devolvendo', 'cancelado'].includes(acao)} disabled={c.ocupado} onChange={(e) => setMotivo(e.target.value)} />
          {['devolvendo', 'devolvido'].includes(acao) && <p className={css.apoio}>O retorno físico não devolve o pagamento do cliente. Confira a devolução financeira na ficha do pedido.</p>}</>}
      <div className={css.linha}><Botao tipo="submit" variante="primario" disabled={c.ocupado}>Confirmar registro</Botao><Botao variante="texto" disabled={c.ocupado} onClick={() => setAcao(null)}>Voltar</Botao></div>
    </form>}
    {d.eventos?.length > 0 && <details><summary>Histórico da entrega</summary><ol className={css.historico}>{d.eventos.map((e, i) => <li key={`${e.em}:${i}`}>{horario(e.em)} · {SITUACOES[e.tipo] ?? e.tipo}{e.motivo ? ` · ${e.motivo}` : ''}</li>)}</ol></details>}
  </article>
}

function Politica({ politica, controlador: c }) {
  const [automatico, setAutomatico] = useState(politica.automatico)
  const [agrupar, setAgrupar] = useState(politica.permitirAgrupar)
  const [teto, setTeto] = useState(String(politica.tetoCentavos / 100))
  return <details className={css.politica}><summary>Preferências de contratação</summary>
    <form onSubmit={(e) => { e.preventDefault(); c.salvarPolitica({ automatico, permitirAgrupar: agrupar, tetoCentavos: Math.round(Number(teto) * 100) }) }}>
      <p className={css.apoio}>A contratação automática só pode escolher dentro das regras salvas. Acima do limite, a decisão volta para você.</p>
      <label className={css.marcador}><input type="checkbox" checked={automatico} disabled={c.ocupado} onChange={(e) => setAutomatico(e.target.checked)} />Permitir escolha automática</label>
      <label className={css.marcador}><input type="checkbox" checked={agrupar} disabled={c.ocupado} onChange={(e) => setAgrupar(e.target.checked)} />Permitir pedidos juntos na mesma entrega</label>
      <CampoTexto rotulo="Limite por entrega (R$)" tipo="number" min="0.01" max="1000" step="0.01" required value={teto} disabled={c.ocupado} onChange={(e) => setTeto(e.target.value)} />
      <Botao tipo="submit" disabled={c.ocupado}>Salvar preferências</Botao>
    </form>
  </details>
}
