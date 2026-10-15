import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { useGravacaoDoAjuste, useRascunhoDoAjuste } from '../../aplicacao/rascunhosDosAjustes'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { DIAS_DA_SEMANA } from '../../dominio/funcionamento'
import { partesNoFuso } from '../../dominio/formato'
import { DiaDeFuncionamento } from './ModalAutomacoes'
import css from './automacoes.module.css'

const mesmosHorarios = (a, b) => DIAS_DA_SEMANA.every(({ chave }) => {
  const dia = a?.[chave], salvo = b?.[chave]
  return dia?.fechado === salvo?.fechado && (dia?.fechado || (dia?.abre === salvo?.abre && dia?.fecha === salvo?.fecha))
})

export function ModalHorariosDaSemana({ aoFechar }) {
  const { agora, lojaAberta } = useAtendimento()
  const { recarregarExpediente, salvarHorariosExpediente } = useAcoes()
  const [rascunho, setRascunho] = useRascunhoDoAjuste('horarios-atendimento')
  const [gravando, iniciarGravacao, estaGravando] = useGravacaoDoAjuste('expediente-atendimento')
  const [gravado, setGravado] = useState(null)
  const [carga, setCarga] = useState({ estado: 'carregando' })
  const [situacao, setSituacao] = useState({ estado: 'ocioso' })
  const consulta = useRef(0)
  const ocupado = useRef(false)
  const formularioId = useId()
  const valor = rascunho ?? gravado
  const pendente = Boolean(rascunho) && !mesmosHorarios(rascunho, gravado)
  const salvando = situacao.estado === 'salvando'

  const carregar = useCallback(async () => {
    const atual = ++consulta.current
    const semGravacao = !estaGravando()
    try {
      const confirmado = await recarregarExpediente()
      if (atual !== consulta.current) return
      if (!confirmado?.funcionamento) throw new Error('Não foi possível conferir os horários agora. Tente novamente.')
      setGravado(confirmado.funcionamento)
      if (semGravacao && !estaGravando()) setRascunho((edicao) => edicao && !mesmosHorarios(edicao, confirmado.funcionamento) ? edicao : null)
      setCarga({ estado: 'ok' })
    } catch (erro) {
      if (atual === consulta.current) setCarga({ estado: 'erro', mensagem: erro.message })
    }
  }, [recarregarExpediente, setRascunho, estaGravando])
  const cancelarConsulta = useCallback(() => { consulta.current++ }, [])

  useEffect(() => {
    carregar()
    return cancelarConsulta
  }, [carregar, cancelarConsulta])

  const anterior = useRef({ rascunho, gravando })
  useEffect(() => {
    const limpou = anterior.current.rascunho !== null && rascunho === null
    const terminou = anterior.current.gravando && !gravando
    anterior.current = { rascunho, gravando }
    // Uma gravação iniciada em uma instância anterior pode terminar depois de voltar.
    if ((limpou || terminou) && ['ocioso', 'desfeito'].includes(situacao.estado)) carregar()
  }, [rascunho, situacao.estado, carregar, gravando])

  function editar(chave, campos) {
    if (ocupado.current || carga.estado !== 'ok') return
    const proximo = { ...valor, [chave]: { ...valor[chave], ...campos } }
    setRascunho(!gravando && mesmosHorarios(proximo, gravado) ? null : proximo)
    setSituacao({ estado: 'ocioso' })
  }

  async function salvar(evento) {
    evento.preventDefault()
    if (ocupado.current || gravando || !pendente || carga.estado !== 'ok' || !evento.currentTarget.reportValidity()) return
    const liberar = iniciarGravacao()
    if (!liberar) return
    ocupado.current = true
    const atual = ++consulta.current
    const enviado = rascunho
    setSituacao({ estado: 'salvando' })
    try {
      const confirmado = await salvarHorariosExpediente(enviado)
      if (!confirmado?.funcionamento) throw new Error('O EasyStok não confirmou os horários. Tente novamente.')
      if (atual === consulta.current) {
        setGravado(confirmado.funcionamento)
        setSituacao({ estado: 'salvo' })
      }
      setRascunho((edicao) => edicao === enviado ? null : edicao)
    } catch (erro) {
      if (atual === consulta.current) setSituacao({ estado: 'erro', mensagem: erro.message })
    } finally {
      ocupado.current = false
      liberar()
    }
  }

  const fechar = () => { if (!ocupado.current && !gravando) aoFechar() }
  const hoje = DIAS_DA_SEMANA[partesNoFuso(agora).diaDaSemana].chave
  return <Modal titulo="Horários da semana" largura="min(760px, calc(100vw - 24px))"
    descricao="As mudanças valem após salvar. Fechar mantém o rascunho nesta sessão."
    aoFechar={salvando || gravando ? undefined : fechar}
    rodape={<>
      <Botao variante="primario" tipo="submit" form={formularioId} disabled={!pendente || gravando || salvando || carga.estado !== 'ok'}>{salvando ? 'Salvando…' : 'Salvar horários'}</Botao>
      <Botao variante="texto" disabled={!pendente || salvando} onClick={() => { setRascunho(null); setSituacao({ estado: 'desfeito' }) }}>Desfazer edição</Botao>
      <Botao disabled={salvando || gravando} onClick={fechar}>Fechar</Botao>
    </>}>
    <form id={formularioId} onSubmit={salvar}>
      <p className={css.descricao}>{lojaAberta == null
        ? 'A loja segue os horários da semana.'
        : `Loja ${lojaAberta ? 'aberta' : 'fechada'} manualmente. Salvar horários mantém essa escolha.`}</p>
      {carga.estado === 'carregando' && <output>Conferindo os horários salvos…</output>}
      {carga.estado === 'erro' && <div className={css.alerta} role="alert"><p>{carga.mensagem}</p><Botao onClick={() => { setCarga({ estado: 'carregando' }); carregar() }}>Tentar novamente</Botao></div>}
      {valor && <fieldset className={css.camposHorarios} disabled={salvando || carga.estado !== 'ok'}>
        <legend className="sr">Horários por dia da semana</legend>
        <ul className={css.diasFuncionamento}>{DIAS_DA_SEMANA.map((dia) => <DiaDeFuncionamento key={dia.chave} dia={dia}
          valor={valor[dia.chave]} obrigatorio hoje={dia.chave === hoje} aoEditar={(campos) => editar(dia.chave, campos)} />)}</ul>
      </fieldset>}
      <output className={css.editando}>{salvando
        ? 'Salvando horários. Sair não cancela o que já foi enviado.'
        : gravando ? 'Outro ajuste está sendo salvo. Você pode editar; aguarde para salvar. Sair não cancela o que já foi enviado.'
          : situacao.estado === 'salvo' ? 'Horários salvos no EasyStok.'
          : situacao.estado === 'desfeito' ? 'Edição desfeita. Os horários salvos continuam valendo.'
            : pendente ? 'Alterações não salvas. Salve antes de sair do EasyStok.' : ''}</output>
      {situacao.estado === 'erro' && <p className={css.alerta} role="alert">Não foi possível confirmar o salvamento. {situacao.mensagem} Sua edição foi mantida.</p>}
    </form>
  </Modal>
}
