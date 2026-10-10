import { useState } from 'react'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { useEstornosOnline } from '../../aplicacao/useEstornosOnline'
import { Botao } from '../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { CampoMascarado } from '../../componentes/CampoMascarado'
import { lerMoeda, mascaraMoeda, moeda } from '../../dominio/formato'
import css from './ficha.module.css'

const data = (valor) => new Date(valor).toLocaleString('pt-BR')
const situacoes = { pendente: 'Aguardando confirmação', confirmado: 'Confirmado', recusado: 'Recusado' }

export function EstornosOnline({ pedidoId }) {
  const { acoes } = useAcessoModulos()
  const { dados, erro, aviso, semResposta, ocupado, atualizar, solicitar, retomar } = useEstornosOnline(pedidoId)
  const [aberto, setAberto] = useState(false)
  const [pagamentoId, setPagamentoId] = useState('')
  const [centavos, setCentavos] = useState('')
  const [motivo, setMotivo] = useState('')
  const [confirmado, setConfirmado] = useState(false)
  const podeSolicitar = acoes.estornarPagamentoOnline === true
  const disponiveis = dados?.pagamentos.filter((p) => p.disponivel > 0 && p.reservado === 0) ?? []
  const pagamento = disponiveis.find((p) => p.id === pagamentoId)
  const valor = Number(centavos || 0) / 100
  const valido = pagamento && valor > 0 && valor <= pagamento.disponivel && motivo.trim() && confirmado

  function escolher(id) {
    setPagamentoId(id)
    const p = disponiveis.find((item) => item.id === id)
    setCentavos(p ? String(Math.round(p.disponivel * 100)) : '')
    setConfirmado(false)
  }

  async function salvar(evento) {
    evento.preventDefault()
    if (!valido || ocupado) return
    if (await solicitar({ pagamentoId, valor, motivo: motivo.trim() })) {
      setAberto(false); setConfirmado(false); setMotivo(''); setPagamentoId('')
    }
  }

  return <section aria-label="Estornos pelo Mercado Pago" className={css.acoesPedido}>
    <h3 className={css.corpoBloco}>Mercado Pago</h3>
    {erro && <p role="alert" className={css.corpoBloco}>{erro}</p>}
    {aviso && <output className={css.corpoBloco}>{aviso}</output>}
    {dados?.pagamentos.map((p) => <p key={p.id} className={css.corpoBloco}>
      Recebido: {moeda(p.valor)} · Devolvido: {moeda(p.devolvido)} · Disponível: {moeda(p.disponivel)}
      {p.reservado > 0 && <> · Em confirmação: {moeda(p.reservado)}</>}
      {p.legado && <><br />Estorno anterior já registrado na cobrança.</>}
    </p>)}
    {dados?.estornos.map((e) => <div key={e.id} className={css.acoesPedido}>
      <p className={css.corpoBloco}><strong>{moeda(e.valor)} · {situacoes[e.situacao] ?? e.situacao}</strong><br />
        {data(e.confirmadoEm ?? e.criadoEm)}{e.usuarioNome ? ` · ${e.usuarioNome}` : ''}<br />{e.motivo}
        {e.estornoExternoId && <><br />Referência no Mercado Pago: {e.estornoExternoId}</>}
        {e.detalhe && <><br />{e.detalhe}</>}
      </p>
      {e.situacao === 'pendente' && podeSolicitar && <Botao disabled={ocupado} onClick={() => retomar(e.id)}>Conferir estorno pendente</Botao>}
    </div>)}
    {semResposta && <>
      <output className={css.corpoBloco}>Solicitação de {moeda(semResposta.valor)} sem resposta conclusiva. Atualize ou retome esta mesma solicitação. Não devolva por outro meio.</output>
      {podeSolicitar && <Botao disabled={ocupado} onClick={() => solicitar(semResposta)}>Retomar solicitação sem resposta</Botao>}
    </>}
    {!podeSolicitar && <p className={css.corpoBloco}>Só a dona ou um gerente pode solicitar o estorno.</p>}
    {podeSolicitar && !semResposta && !aberto && disponiveis.length > 0 && <Botao disabled={ocupado} onClick={() => { escolher(disponiveis[0].id); setAberto(true) }}>Solicitar estorno pelo Mercado Pago</Botao>}
    {podeSolicitar && !semResposta && aberto && <form className={css.acoesPedido} onSubmit={salvar}>
      <CampoSelecao rotulo="Recebimento do Mercado Pago" value={pagamentoId} disabled={ocupado} onChange={(e) => escolher(e.target.value)} opcoes={disponiveis.map((p) => ({ valor: p.id, rotulo: `${data(p.pagoEm)} · ${p.metodo} · disponível ${moeda(p.disponivel)}` }))} />
      <CampoMascarado rotulo="Valor do estorno online" tipo="moeda" valor={mascaraMoeda(centavos)} aoMudarDigitos={setCentavos} aoColarTexto={(t) => setCentavos(String(lerMoeda(t)))} disabled={ocupado} dica={pagamento ? `Até ${moeda(pagamento.disponivel)} deste recebimento.` : 'Atualize para conferir o recebimento.'} />
      <CampoTexto rotulo="Motivo do estorno online" value={motivo} onChange={(e) => setMotivo(e.target.value)} maxLength={500} required disabled={ocupado} />
      <label className={css.corpoBloco}><input type="checkbox" checked={confirmado} onChange={(e) => setConfirmado(e.target.checked)} disabled={ocupado} /> Confirmo a solicitação de devolução de {moeda(valor)} pelo Mercado Pago.</label>
      <p className={css.corpoBloco}>O Mercado Pago devolverá pelo meio original. A saída no Caixa será registrada quando o provedor confirmar. Não faça uma devolução manual para o mesmo valor.</p>
      <Botao type="submit" variante="primario" disabled={!valido || ocupado}>{ocupado ? 'Conferindo estorno…' : 'Confirmar solicitação de estorno'}</Botao>
      <Botao type="button" disabled={ocupado} onClick={() => setAberto(false)}>Voltar</Botao>
    </form>}
    <Botao disabled={ocupado} onClick={atualizar}>Atualizar estornos online</Botao>
  </section>
}
