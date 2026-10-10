import { useState } from 'react'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { useOcorrencias } from '../../aplicacao/useOcorrencias'
import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { Pilula } from '../../componentes/Pilula'
import { dataHora, moeda } from '../../dominio/formato'
import css from './ficha.module.css'

const categorias = { produto_improprio: 'Produto impróprio', atraso: 'Atraso', preferencia: 'Preferência', outro: 'Outro' }
const origens = { dona: 'Loja', avaliacao: 'Avaliação', agente: 'Agente' }

function Ocorrencia({ o, podeGerenciar, ocupado, apurar, encerrar, retomar }) {
  const [formulario, setFormulario] = useState(false)
  const [resolucao, setResolucao] = useState('')
  const aberta = o.status === 'aberta'
  const pendente = aberta && o.reembolsoSolicitadoEm && o.reembolsoSituacao !== 'recusado'
  const estado = !aberta ? 'Encerrada' : pendente ? 'Aguardando conclusão do reembolso' : o.apuradaEm ? 'Em apuração' : 'Aberta'

  async function salvar(e) {
    e.preventDefault()
    if (resolucao.trim() && await encerrar(o.id, resolucao.trim())) setFormulario(false)
  }

  return <article className={css.acoesPedido} aria-label={`Ocorrência: ${o.relato}`}>
    <p className={css.corpoBloco}><Pilula tom={!aberta ? 'neutro' : 'aviso'}>{estado}</Pilula><br />
      <strong>{categorias[o.categoria] ?? o.categoria}</strong> · {origens[o.origem] ?? o.origem}<br />
      Aberta em {dataHora(o.criadaEm)}<br />{o.relato}
    </p>
    {o.apuradaEm && <p className={css.corpoBloco}>Apuração iniciada em {dataHora(o.apuradaEm)} · {o.apuradaPorNome || 'Responsável registrado'}.</p>}
    {!aberta && <p className={css.corpoBloco}>Encerrada em {dataHora(o.resolvidaEm)} · {o.resolvidaPorNome || 'Responsável registrado'}.<br /><strong>Resolução:</strong> {o.resolucao}</p>}
    {o.reembolsoEm && <p className={css.corpoBloco}>Reembolso confirmado: {moeda(o.reembolsoValor)} em {dataHora(o.reembolsoEm)}.</p>}
    {!aberta && o.reembolsoValor != null && !o.reembolsoEm && <p className={css.corpoBloco}>Devolução manual indicada: {moeda(o.reembolsoValor)}. Este registro não confirma que o dinheiro foi devolvido. Confira em Devoluções.</p>}
    {pendente && <p className={css.corpoBloco}>Solicitação de {moeda(o.reembolsoValor)}: {o.resolucao}. {o.reembolsoSituacao === 'confirmado' ? 'O reembolso foi confirmado; falta concluir a ocorrência.' : 'Aguardando confirmação. Confira a mesma solicitação para concluir.'}</p>}
    {o.reembolsoSituacao === 'recusado' && <p className={css.corpoBloco}>A solicitação de reembolso foi recusada.</p>}
    {aberta && !podeGerenciar && <p className={css.corpoBloco}>Só a dona ou um gerente pode apurar e encerrar.</p>}
    {aberta && podeGerenciar && <>
      {!o.apuradaEm && <Botao disabled={ocupado} onClick={() => apurar(o.id)}>Iniciar apuração</Botao>}
      {pendente ? <Botao disabled={ocupado} onClick={() => retomar(o)}>Conferir reembolso e concluir</Botao> : <>
        {!formulario && <Botao disabled={ocupado} onClick={() => setFormulario(true)}>Encerrar ocorrência</Botao>}
        {formulario && <form className={css.acoesPedido} onSubmit={salvar}>
          <CampoArea rotulo="Resolução da ocorrência" value={resolucao} onChange={(e) => setResolucao(e.target.value)} maxLength={1000} required disabled={ocupado} rows={3} />
          <p className={css.corpoBloco}>A resolução fica no histórico do cliente. Para devolver valores, use Devoluções e confira a confirmação antes de encerrar.</p>
          <Botao type="submit" variante="primario" disabled={ocupado || !resolucao.trim()}>Confirmar encerramento</Botao>
          <Botao type="button" disabled={ocupado} onClick={() => setFormulario(false)}>Voltar</Botao>
        </form>}
      </>}
    </>}
  </article>
}

export function BlocoOcorrenciasApi({ pedidoId, conversaId }) {
  const { acoes } = useAcessoModulos()
  const { dados, erro, ocupado, atualizar, ...comandos } = useOcorrencias(pedidoId, conversaId)
  const abertas = dados?.filter((o) => o.status === 'aberta').length ?? 0
  return <Bloco titulo="Ocorrências" chave="ocorrencias" resumo={dados ? `${abertas} abertas · ${dados.length} no histórico` : 'Consultar ocorrências'}>
    <div className={css.acoesPedido}>
      {erro && <p className={css.corpoBloco} role="alert">{erro}</p>}
      {!dados && !erro && <p className={css.corpoBloco}>Consultando ocorrências…</p>}
      {dados?.length === 0 && <p className={css.corpoBloco}>Nenhuma ocorrência neste pedido.</p>}
      {dados?.map((o) => <Ocorrencia key={o.id} o={o} podeGerenciar={acoes.gerenciarOcorrencias === true} ocupado={ocupado} {...comandos} />)}
      <Botao disabled={ocupado} onClick={atualizar}>Atualizar ocorrências</Botao>
    </div>
  </Bloco>
}
