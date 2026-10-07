import { useCallback, useEffect, useState } from 'react'
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
  const [lista, setLista] = useState({ estado: 'carregando', itens: [], erro: null })

  const conversaId = conversa.id
  const recarregar = useCallback(() => listarProgramadas(conversaId)
    .then((itens) => setLista({ estado: 'pronto', itens, erro: null }))
    .catch((e) => setLista({ estado: 'erro', itens: [], erro: e.message })), [listarProgramadas, conversaId])

  useEffect(() => { recarregar() }, [recarregar])

  const comModelo = usarModelo && canal.aceitaModelo
  const agendadaPara = horarioLocalParaUtc(quando)

  async function programar() {
    const falta = faltaParaProgramar({ agendadaPara, agoraMs: Date.now(), usarModelo: comModelo, texto, nomeModelo })
    if (falta) { setErro(falta); return }
    setErro(null)
    setSalvando(true)
    try {
      await programarMensagem(conversaId, {
        finalidade,
        agendadaPara,
        ...(comModelo
          ? { modelo: { nome: nomeModelo, idioma, parametros: parametrosDoTexto(parametros) } }
          : { texto }),
      })
      aoAgendar?.(comModelo ? null : texto)
      if (!comModelo) setTexto('')
      await recarregar()
    } catch (e) {
      setErro(e.message)
    } finally {
      setSalvando(false)
    }
  }

  async function cancelar(id) {
    setErro(null)
    try {
      await cancelarProgramada(id)
      await recarregar()
    } catch (e) {
      setErro(e.message)
    }
  }

  return (
    <Modal
      titulo="Programar mensagem"
      descricao={`Sai para ${primeiroNome(conversa.nome)} pelo ${canal.nome} no horário escolhido, mesmo com o console fechado.`}
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao onClick={aoFechar}>Fechar</Botao>
          <Botao variante="primario" icone="relogio" disabled={salvando} onClick={programar}>
            {salvando ? 'Programando…' : 'Programar'}
          </Botao>
        </>
      )}
    >
      <div className={css.programar}>
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

        {erro && <p className={css.programarErro} role="alert">{erro}</p>}

        <p className={css.cardapioRotulo}>Programadas desta conversa</p>
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
                      <Botao variante="texto" onClick={() => cancelar(p.id)}>Cancelar</Botao>
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
