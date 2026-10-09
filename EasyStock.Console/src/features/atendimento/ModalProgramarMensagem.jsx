import { useCallback, useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea, CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { useAcoes } from '../../aplicacao/contextos'
import { dataHora } from '../../dominio/formato'
import { primeiroNome } from '../../dominio/mensagem'
import {
  FINALIDADES, SITUACOES, faltaParaProgramar, horarioLocalPadrao, horarioLocalParaUtc,
  parametrosDoTexto, podeCancelar,
} from '../../dominio/mensagemProgramada'
import css from './atendimento.module.css'

// #1424: programar mensagem ao cliente. Quem guarda e dispara é o EasyStok (S39); aqui só
// se agenda, lista e cancela. O erro da API (janela de 24 h no horário do envio, cliente
// sem consentimento ou sem telefone) fica escrito na modal, com a mensagem dela.
export function ModalProgramarMensagem({
  conversa, canal, rascunho, aoAgendar, aoFechar,
}) {
  const { programarMensagem, listarProgramadas, cancelarProgramada } = useAcoes()
  const [quando, setQuando] = useState(() => horarioLocalPadrao(Date.now()))
  const [texto, setTexto] = useState(rascunho ?? '')
  const [finalidade, setFinalidade] = useState(FINALIDADES[0].valor)
  const [usarModelo, setUsarModelo] = useState(false)
  const [nomeModelo, setNomeModelo] = useState('')
  const [idioma, setIdioma] = useState('pt_BR')
  const [parametros, setParametros] = useState('')
  const [erro, setErro] = useState(null)
  const [salvando, setSalvando] = useState(false)
  const [sucesso, setSucesso] = useState(null)
  const ocupado = useRef(false)
  const consulta = useRef(0)
  const montada = useRef(true)
  const [lista, setLista] = useState({ estado: 'carregando', itens: [], erro: null })

  const conversaId = conversa.id
  const recarregar = useCallback(async () => {
    const versao = ++consulta.current
    setLista((atual) => ({ ...atual, estado: 'carregando', erro: null }))
    try {
      const itens = await listarProgramadas(conversaId)
      if (consulta.current === versao) setLista({ estado: 'pronto', itens, erro: null })
    } catch (e) {
      if (consulta.current === versao) setLista((atual) => ({ ...atual, estado: 'erro', erro: e.message }))
    }
  }, [listarProgramadas, conversaId])

  useEffect(() => {
    montada.current = true
    recarregar()
    return () => { montada.current = false; consulta.current++ }
  }, [recarregar])

  const comModelo = usarModelo && canal.aceitaModelo
  const agendadaPara = horarioLocalParaUtc(quando)
  const bloqueado = Boolean(conversa.bloqueio)
  const fechar = () => { if (!ocupado.current) aoFechar() }

  async function programar() {
    if (ocupado.current || bloqueado) return
    const falta = faltaParaProgramar({ agendadaPara, agoraMs: Date.now(), usarModelo: comModelo, texto, nomeModelo })
    if (falta) { setErro(falta); return }
    setErro(null)
    setSucesso(null)
    ocupado.current = true
    setSalvando(true)
    try {
      const programada = await programarMensagem(conversaId, {
        finalidade,
        agendadaPara,
        ...(comModelo
          ? { modelo: { nome: nomeModelo, idioma, parametros: parametrosDoTexto(parametros) } }
          : { texto }),
      })
      if (!montada.current) return
      setLista((atual) => ({ ...atual, itens: [programada, ...atual.itens.filter((p) => p.id !== programada.id)] }))
      setSucesso('Mensagem programada. O envio será tentado no horário escolhido.')
      aoAgendar?.(comModelo ? null : texto)
      if (!comModelo) setTexto('')
      await recarregar()
    } catch (e) {
      if (montada.current) setErro(e.message)
    } finally {
      ocupado.current = false
      setSalvando(false)
    }
  }

  async function cancelar(id) {
    if (ocupado.current) return
    ocupado.current = true
    setSalvando(true)
    setErro(null)
    setSucesso(null)
    try {
      const cancelada = await cancelarProgramada(id)
      if (!montada.current) return
      setLista((atual) => ({ ...atual, itens: atual.itens.map((p) => p.id === id ? cancelada : p) }))
      setSucesso('Mensagem cancelada.')
      await recarregar()
    } catch (e) {
      if (montada.current) setErro(e.message)
    } finally {
      ocupado.current = false
      setSalvando(false)
    }
  }

  return (
    <Modal
      titulo="Programar mensagem"
      descricao={`Programe o envio para ${primeiroNome(conversa.nome)} pelo ${canal.nome}. O EasyStok tenta enviar no horário escolhido, mesmo com o console fechado.`}
      aoFechar={fechar}
      rodape={(
        <>
          <Botao onClick={fechar} disabled={salvando}>Fechar</Botao>
          <Botao variante="primario" icone="relogio" disabled={salvando || bloqueado} onClick={programar}>
            {salvando ? 'Aguarde…' : 'Programar'}
          </Botao>
        </>
      )}
    >
      <div className={css.programar}>
        {bloqueado && <p className={css.programarErro}>Cliente bloqueado. Desbloqueie na ficha para programar. Você pode consultar e cancelar os agendamentos existentes.</p>}
        <fieldset className={css.programarCampos} disabled={salvando || bloqueado}>
        <CampoTexto
          rotulo="Quando enviar"
          tipo="datetime-local"
          value={quando}
          onChange={(e) => setQuando(e.target.value)}
        />
        <CampoSelecao
          rotulo="Para quê"
          opcoes={FINALIDADES}
          value={finalidade}
          onChange={(e) => setFinalidade(e.target.value)}
        />

        {canal.aceitaModelo && (
          <label className={css.programarOpcao}>
            <input type="checkbox" checked={usarModelo} onChange={(e) => setUsarModelo(e.target.checked)} />
            Usar modelo aprovado (obrigatório se o horário cair fora das 24 h depois da última mensagem do cliente)
          </label>
        )}

        {comModelo ? (
          <>
            <CampoTexto
              rotulo="Nome do modelo na Meta"
              dica="Exatamente como aprovado no WhatsApp Business (ex.: lembrete_pedido)."
              value={nomeModelo}
              onChange={(e) => setNomeModelo(e.target.value)}
            />
            <CampoTexto rotulo="Idioma do modelo" value={idioma} onChange={(e) => setIdioma(e.target.value)} />
            <CampoArea
              rotulo="Parâmetros do modelo"
              dica="Um por linha, na ordem {{1}}, {{2}}… do modelo."
              rows={3}
              value={parametros}
              onChange={(e) => setParametros(e.target.value)}
            />
          </>
        ) : (
          <CampoArea rotulo="Mensagem" rows={4} value={texto} onChange={(e) => setTexto(e.target.value)} />
        )}
        </fieldset>

        {erro && <p className={css.programarErro} role="alert">{erro}</p>}
        {sucesso && <output>{sucesso}</output>}

        <div className={css.programadaTopo}>
          <p className={css.cardapioRotulo}>Programadas desta conversa</p>
          <Botao onClick={recarregar} disabled={salvando || lista.estado === 'carregando'}>Atualizar lista</Botao>
        </div>
        {lista.estado === 'carregando' && <p className={css.restricao}>Carregando…</p>}
        {lista.estado === 'erro' && <p className={css.programarErro} role="alert">{lista.erro}</p>}
        {lista.estado === 'pronto' && lista.itens.length === 0 && (
          <p className={css.restricao}>Nenhuma mensagem programada para esta conversa.</p>
        )}
        {lista.itens.length > 0 && (
          <ul className={css.programadas}>
            {lista.itens.map((p) => {
              const situacao = SITUACOES[p.situacao] ?? { rotulo: p.situacao, tom: 'neutro' }
              return (
                <li key={p.id}>
                  <div className={css.programadaTopo}>
                    <strong>{p.agendadaPara ? dataHora(p.agendadaPara) : 'sem horário'}</strong>
                    <Pilula tom={situacao.tom}>{situacao.rotulo}</Pilula>
                    {podeCancelar(p) && (
                      <Botao variante="texto" disabled={salvando} onClick={() => cancelar(p.id)}>Cancelar</Botao>
                    )}
                  </div>
                  <p className={css.programadaTexto}>
                    {p.texto ?? `Modelo ${p.modelo?.nome ?? ''}${p.modelo?.parametros?.length ? ` (${p.modelo.parametros.join(', ')})` : ''}`}
                  </p>
                  {p.erro && <p className={css.programarErro}>{p.erro}</p>}
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </Modal>
  )
}
