import { useState } from 'react'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { useEstornosManuais } from '../../aplicacao/useEstornosManuais'
import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { CampoMascarado } from '../../componentes/CampoMascarado'
import { lerMoeda, mascaraMoeda, moeda } from '../../dominio/formato'
import css from './ficha.module.css'
import { EstornosOnline } from './EstornosOnline'

const meios = [
  { valor: 'dinheiro', rotulo: 'Dinheiro' }, { valor: 'pix', rotulo: 'Pix direto' },
  { valor: 'credito', rotulo: 'Cartão de crédito' }, { valor: 'debito', rotulo: 'Cartão de débito' },
  { valor: 'transferencia', rotulo: 'Transferência' }, { valor: 'outro', rotulo: 'Outro' },
]
const data = (valor) => new Date(valor).toLocaleString('pt-BR')

export function BlocoDevolucoes({ pedidoId }) {
  const { acoes } = useAcessoModulos()
  const { dados, erro, aviso, pendente, ocupado, atualizar, registrar } = useEstornosManuais(pedidoId)
  const [aberto, setAberto] = useState(false)
  const [pagamentoId, setPagamentoId] = useState('')
  const [centavos, setCentavos] = useState('')
  const [metodo, setMetodo] = useState('dinheiro')
  const [motivo, setMotivo] = useState('')
  const [referencia, setReferencia] = useState('')
  const [confirmado, setConfirmado] = useState(false)
  const disponiveis = dados?.pagamentos.filter((p) => p.manual && p.disponivel > 0) ?? []
  const pagamento = disponiveis.find((p) => p.id === pagamentoId)
  const valor = Number(centavos || 0) / 100
  const podeRegistrar = acoes.devolverPagamentoManual === true
  const valido = pagamento && valor > 0 && valor <= pagamento.disponivel && motivo.trim() && referencia.trim() && confirmado
  const totalDevolvido = dados?.estornos.reduce((soma, e) => soma + e.valor, 0) ?? 0

  function escolher(id) {
    setPagamentoId(id)
    const p = disponiveis.find((item) => item.id === id)
    setCentavos(p ? String(Math.round(p.disponivel * 100)) : '')
    setMetodo(p?.metodo ?? 'dinheiro')
    setConfirmado(false)
  }

  async function salvar(evento) {
    evento.preventDefault()
    if (!valido || ocupado) return
    if (await registrar({ pagamentoId, valor, metodo, motivo: motivo.trim(), referencia: referencia.trim(), valorJaDevolvido: confirmado })) {
      setAberto(false); setConfirmado(false); setPagamentoId(''); setMotivo(''); setReferencia('')
    }
  }

  return (
    <Bloco titulo="Devoluções" chave="devolucoes" resumo={totalDevolvido ? `${moeda(totalDevolvido)} devolvidos` : 'Recebimentos e estornos'}>
      <div className={css.acoesPedido}>
        {erro && <p role="alert" className={css.corpoBloco}>{erro}</p>}
        {aviso && <output className={css.corpoBloco}>{aviso}</output>}
        {!dados && <p className={css.corpoBloco}>{ocupado ? 'Consultando devoluções…' : 'Não foi possível consultar as devoluções.'}</p>}
        {dados && <>
          <p className={css.corpoBloco}>Recebido: {moeda(dados.pagamentos.reduce((s, p) => s + p.valor, 0))}. Devolução manual confirmada: {moeda(totalDevolvido)}.</p>
          {dados.pagamentos.some((p) => !p.manual) && <EstornosOnline pedidoId={pedidoId} />}
          {dados.estornos.map((e) => <p key={e.id} className={css.corpoBloco}>
            <strong>{moeda(e.valor)} · {meios.find((m) => m.valor === e.metodo)?.rotulo ?? e.metodo}</strong><br />
            {data(e.registradoEm)}{e.usuarioNome ? ` · ${e.usuarioNome}` : ''}<br />
            {e.motivo}<br />Referência: {e.referencia}
          </p>)}
          {!podeRegistrar && <p className={css.corpoBloco}>Só a dona ou um gerente pode confirmar a devolução.</p>}
          {!pendente && podeRegistrar && disponiveis.length > 0 && !aberto && <Botao onClick={() => { escolher(disponiveis[0].id); setAberto(true) }}>Registrar devolução manual</Botao>}
          {!pendente && podeRegistrar && !disponiveis.length && <p className={css.corpoBloco}>Nenhum recebimento manual com saldo para devolver.</p>}
        </>}
        {pendente && <>
          <output className={css.corpoBloco}>{ocupado ? 'Conferindo o registro da devolução…' : `Há uma confirmação de ${moeda(pendente.valor)} sem resposta conclusiva. Atualize para conferir ou repita o mesmo registro. Não devolva o dinheiro novamente.`}</output>
          {podeRegistrar && <Botao disabled={ocupado} onClick={() => registrar(pendente)}>Repetir confirmação pendente</Botao>}
        </>}
        {aberto && !pendente && podeRegistrar && <form className={css.acoesPedido} onSubmit={salvar}>
          <CampoSelecao rotulo="Recebimento a devolver" value={pagamentoId} disabled={ocupado} onChange={(e) => escolher(e.target.value)} opcoes={disponiveis.map((p) => ({ valor: p.id, rotulo: `${data(p.pagoEm)} · ${p.metodo} · disponível ${moeda(p.disponivel)}` }))} />
          <CampoMascarado rotulo="Valor devolvido" tipo="moeda" valor={mascaraMoeda(centavos)} aoMudarDigitos={setCentavos} aoColarTexto={(t) => setCentavos(String(lerMoeda(t)))} disabled={ocupado} dica={pagamento ? `Até ${moeda(pagamento.disponivel)} deste recebimento.` : 'Escolha um recebimento.'} />
          <CampoSelecao rotulo="Meio usado na devolução" value={metodo} onChange={(e) => setMetodo(e.target.value)} opcoes={meios} disabled={ocupado} />
          <CampoTexto rotulo="Motivo da devolução" value={motivo} onChange={(e) => setMotivo(e.target.value)} maxLength={500} required disabled={ocupado} />
          <CampoTexto rotulo="Comprovante ou referência da devolução" value={referencia} onChange={(e) => setReferencia(e.target.value)} maxLength={120} required disabled={ocupado} />
          <label className={css.corpoBloco}><input type="checkbox" checked={confirmado} onChange={(e) => setConfirmado(e.target.checked)} disabled={ocupado} /> Confirmo que já devolvi este valor ao cliente.</label>
          <p className={css.corpoBloco}>O registro gera uma saída no Caixa de hoje e preserva o recebimento original. O Caixa do dia precisa estar sem fechamento.</p>
          <Botao type="submit" variante="primario" disabled={!valido || ocupado}>{ocupado ? 'Registrando…' : 'Confirmar devolução já realizada'}</Botao>
          <Botao type="button" disabled={ocupado} onClick={() => setAberto(false)}>Voltar</Botao>
        </form>}
        <Botao disabled={ocupado} onClick={atualizar}>Atualizar devoluções</Botao>
      </div>
    </Bloco>
  )
}
