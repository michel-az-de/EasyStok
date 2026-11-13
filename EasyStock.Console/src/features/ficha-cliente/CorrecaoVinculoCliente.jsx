import { useEffect, useRef, useState } from 'react'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { obterCandidatosCliente, obterVinculoCliente } from '../../aplicacao/vinculoCliente'
import { useRascunhoDoAjuste } from '../../aplicacao/rascunhosDosAjustes'
import { Botao } from '../../componentes/Botao'
import { CampoArea, CampoTexto } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { moeda } from '../../dominio/formato'
import css from './CorrecaoVinculoCliente.module.css'

const nomeAcao = { retirar: 'Retirada do vínculo', restaurar: 'Restauração do vínculo', vincular: 'Vínculo conferido' }
export function CorrecaoVinculoCliente({ conversa }) {
  const { acoes } = useAcessoModulos()
  const [aberto, abrir] = useState(false)
  if (!acoes.corrigirVinculoClienteConversa) return null
  return <>
    <Botao variante="texto" icone="usuarios" onClick={() => abrir(true)}>Conferir identidade</Botao>
    {aberto && <ConferenciaVinculo key={conversa.id} conversa={conversa} aoFechar={() => abrir(false)} />}
  </>
}

function ConferenciaVinculo({ conversa, aoFechar }) {
  const { corrigirVinculoClienteConversa } = useAcoes()
  const { sessao } = useAtendimento()
  const { acoes } = useAcessoModulos()
  const [estado, mostrar] = useState(null)
  const [carregando, carregar] = useState(true)
  const [erro, falhar] = useState(null)
  const [revisao, revisar] = useState(0)
  const [confirmado, confirmar] = useState(false)
  const [escolha, escolher] = useState(null)
  const [sucesso, avisar] = useState(null)
  const [telefone, informarTelefone] = useState(conversa.cliente?.telefoneCanal || conversa.contatoInformado?.telefone || '')
  const [candidatos, listar] = useState(null)
  const [buscando, buscar] = useState(false)
  const [salvando, salvarEstado] = useState(false)
  const [conflito, marcarConflito] = useState(false)
  const [pendente, guardarPendente] = useState(null)
  const [rascunho, guardarRascunho] = useRascunhoDoAjuste(`vinculo:${conversa.id}`)
  const motivo = rascunho?.motivo ?? ''
  const emVoo = useRef(false)
  const consultaCandidatos = useRef(null)
  const montado = useRef(true)
  useEffect(() => { montado.current = true; return () => { montado.current = false; consultaCandidatos.current?.abort() } }, [])
  useEffect(() => {
    const controle = new AbortController()
    carregar(true); falhar(null); confirmar(false); escolher(null)
    obterVinculoCliente(conversa.id, controle.signal).then(
      dados => { if (!controle.signal.aborted) { mostrar(dados); carregar(false); marcarConflito(false) } },
      e => { if (!controle.signal.aborted) { falhar(e.message); carregar(false) } },
    )
    return () => controle.abort()
  }, [conversa.id, revisao])
  useEffect(() => {
    if (estado && conversa.vinculoRevisao > estado.vinculoRevisao && !salvando) { marcarConflito(true); confirmar(false) }
  }, [conversa.vinculoRevisao, estado, salvando])
  const impedido = salvando || Boolean(pendente) || conflito || !estado?.podeCorrigir || !acoes.corrigirVinculoClienteConversa
  const escolherAcao = (acao, cliente = null) => { escolher({ acao, cliente }); confirmar(false); falhar(null) }
  async function consultarTelefone() {
    if (buscando || impedido || !telefone.trim()) return
    consultaCandidatos.current?.abort()
    const controle = new AbortController(); consultaCandidatos.current = controle
    buscar(true); falhar(null); listar(null); escolher(null); confirmar(false)
    try { const lista = await obterCandidatosCliente(conversa.id, telefone, controle.signal); if (!controle.signal.aborted) listar(lista) }
    catch (e) { if (!controle.signal.aborted) falhar(e.message) }
    finally { if (!controle.signal.aborted) buscar(false) }
  }
  async function executar() {
    if (emVoo.current || !estado || (!pendente && (impedido || !escolha || !confirmado || motivo.trim().length < 3))) return
    const corpo = pendente ?? { requestId: crypto.randomUUID(), vinculoRevisao: estado.vinculoRevisao, clienteId: estado.clienteId,
      acao: escolha.acao, destinoClienteId: escolha.acao === 'vincular' ? escolha.cliente.id : null, motivo: motivo.trim() }
    emVoo.current = true; salvarEstado(true); falhar(null)
    try {
      const atualizado = await corrigirVinculoClienteConversa(conversa.id, corpo)
      if (!montado.current) return
      mostrar(atualizado); guardarPendente(null); guardarRascunho(null); escolher(null); confirmar(false); listar(null)
      avisar('Vínculo atualizado e registrado no histórico.')
    } catch (e) {
      if (!montado.current) return
      if (e.status === 409) { marcarConflito(true); confirmar(false); guardarPendente(null); falhar(e.message) }
      else { if (e.status === 0 || e.status >= 500) guardarPendente(corpo); falhar(e.message) }
    } finally { emVoo.current = false; if (montado.current) salvarEstado(false) }
  }
  return <Modal titulo="Conferir identidade da conversa" largura="md" focarCampo aoFechar={salvando ? undefined : aoFechar} segurarFechamento={salvando} confirmarDescarte={Boolean(motivo.trim()) && !salvando}
    rodape={<><Botao disabled={salvando} onClick={e => e.currentTarget.closest('dialog')?.dispatchEvent(new Event('cancel', { cancelable: true }))}>Fechar</Botao>{pendente ? <Botao variante="primario" disabled={salvando} onClick={executar}>{salvando ? 'Conferindo…' : 'Conferir resultado'}</Botao> : escolha && <Botao variante="primario" disabled={impedido || !confirmado || motivo.trim().length < 3} onClick={executar}>{salvando ? 'Salvando…' : 'Confirmar alteração'}</Botao>}</>}>
    <div className={css.conteudo}>
      {carregando && <p role="status">Conferindo vínculo e pedidos…</p>}
      {erro && <p className={css.erro} role="alert">{erro}</p>}
      {sucesso && <p className={css.sucesso} role="status">{sucesso}</p>}
      {pendente && <p className={css.erro}>A resposta não chegou. Confira o resultado da mesma solicitação antes de fazer outra alteração.</p>}
      {(conflito || (!carregando && !estado)) && <Botao disabled={salvando} onClick={() => revisar(n => n + 1)}>Atualizar e conferir</Botao>}
      {estado && <>
        <section className={css.identidade}><span>Cadastro vinculado</span><strong>{estado.clienteNome || 'Sem vínculo confirmado'}</strong><small>Conversa de {conversa.canal}</small></section>
        {!estado.podeCorrigir && <div className={css.erro} role="alert"><p>{estado.motivoBloqueio || 'O vínculo desta conversa não pode ser alterado.'}</p>{estado.pedidos?.length > 0 && <ul>{estado.pedidos.map((p, indice) => <li key={p.id}>Pedido {indice + 1} · {p.status} · {moeda(p.total)}</li>)}</ul>}</div>}
        {estado.podeCorrigir && <>
          <div className={css.acoes}>
            {estado.clienteId && <Botao disabled={impedido} onClick={() => escolherAcao('retirar')}>Retirar vínculo com {estado.clienteNome}</Botao>}
            {!estado.clienteId && estado.clienteAnteriorId && <Botao disabled={impedido || !estado.clienteAnteriorNome} onClick={() => escolherAcao('restaurar')}>Restaurar vínculo com {estado.clienteAnteriorNome || 'cadastro anterior'}</Botao>}
          </div>
          {!estado.clienteId && <section className={css.busca}><CampoTexto rotulo="Telefone para conferir" tipo="tel" value={telefone} disabled={impedido} onChange={e => { consultaCandidatos.current?.abort(); buscar(false); informarTelefone(e.target.value); listar(null); escolher(null); confirmar(false) }} /><Botao disabled={impedido || buscando || !telefone.trim()} onClick={consultarTelefone}>{buscando ? 'Buscando…' : 'Consultar cadastros'}</Botao>
            {candidatos && <div className={css.candidatos}>{candidatos.length === 0 ? <p>Nenhum cadastro encontrado para este telefone.</p> : <><p>Confira com a pessoa antes de escolher. Compartilhar telefone não une cadastros.</p>{candidatos.map(c => <label key={c.id} className={css.candidato} aria-label={`Selecionar ${c.nome}, ${c.telefone}`}><input type="radio" name="candidato-cliente" checked={escolha?.cliente?.id === c.id} disabled={impedido || c.bloqueado} onChange={() => escolherAcao('vincular', c)} /><span><strong>{c.nome}</strong><small>{c.telefone}</small><small>{c.ativo === true ? 'Cadastro ativo' : c.ativo === false ? 'Cadastro inativo' : 'Situação não informada'}</small><small>{Number.isInteger(c.quantidadePedidos) && c.quantidadePedidos >= 0 ? c.quantidadePedidos === 0 ? 'Nenhum pedido no histórico' : `${c.quantidadePedidos} ${c.quantidadePedidos === 1 ? 'pedido no histórico' : 'pedidos no histórico'}${c.ultimoPedidoEm ? ` · Último em ${new Date(c.ultimoPedidoEm).toLocaleDateString('pt-BR')}` : ''}` : 'Histórico não informado'}</small><Pilula tom={c.bloqueado ? 'alerta' : 'neutro'}>{c.bloqueado ? 'Bloqueado' : c.ehLead ? 'Lead cadastrado' : 'Cliente'}</Pilula></span></label>)}</>}</div>}
          </section>}
          {escolha && <section className={css.revisao}>
            <strong>{escolha.acao === 'retirar' ? `Retirar somente o vínculo com ${estado.clienteNome}` : escolha.acao === 'restaurar' ? `Restaurar o vínculo com ${estado.clienteAnteriorNome}` : `Vincular a conversa a ${escolha.cliente.nome}`}</strong>
            <p>Cadastros, mensagens e rascunhos serão preservados. {escolha.acao === 'retirar' ? 'Links e sessões de acesso desta conversa serão invalidados.' : 'Nenhum pedido será transferido.'}</p>
            <CampoArea rotulo="Motivo da alteração" value={motivo} minLength={3} maxLength={500} disabled={impedido} onChange={e => guardarRascunho({ motivo: e.target.value })} />
            <label className={css.confirmacao}><input type="checkbox" checked={confirmado} disabled={impedido} onChange={e => confirmar(e.target.checked)} />Conferi a identidade com a pessoa e revisei esta alteração.</label>
          </section>}
        </>}
        {estado.historico?.length > 0 && <details><summary>Histórico de conferências</summary><ol className={css.historico}>{estado.historico.map(h => <li key={h.id}><strong>{nomeAcao[h.acao] || 'Alteração de vínculo'}</strong><small>{new Date(h.criadaEm).toLocaleString('pt-BR')} · {h.usuarioId === sessao?.usuario?.id ? 'Você' : 'Outra pessoa da equipe'}</small><p>{h.motivo}</p></li>)}</ol></details>}
      </>}
    </div>
  </Modal>
}
