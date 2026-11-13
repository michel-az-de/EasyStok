import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { abrirOcorrencia } from '../../aplicacao/api/ocorrencias'
import css from './cozinha.module.css'

export function RegistrarAtraso({ pedidoId }) {
  const [aberto, setAberto] = useState(false)
  const [relato, setRelato] = useState('')
  const [ocupado, setOcupado] = useState(false)
  const [erro, setErro] = useState(null)
  const [registrado, setRegistrado] = useState(null)
  const emVoo = useRef(false)
  const chave = useRef(null)
  async function registrar(e) {
    e.preventDefault()
    if (emVoo.current || !relato.trim()) return
    emVoo.current = true; setOcupado(true); setErro(null)
    chave.current ??= crypto.randomUUID()
    try {
      const resposta = await abrirOcorrencia({ pedidoId, conversaId: null, categoria: 'atraso', relato: relato.trim() }, chave.current)
      setRegistrado(resposta)
    } catch (falha) { setErro(falha.message) }
    finally { emVoo.current = false; setOcupado(false) }
  }
  if (registrado) return <p className={css.comandaApoio} role="status">{registrado.conversaId
    ? 'Atraso registrado. Atendimento avisado na conversa vinculada.'
    : 'Atraso registrado no histórico do pedido.'}</p>
  if (!aberto) return <Botao onClick={() => setAberto(true)}>Registrar atraso</Botao>
  return <form className={css.comanda} onSubmit={registrar}>
    <CampoArea rotulo="O que atrasou?" value={relato} onChange={e => { setRelato(e.target.value); chave.current = null }}
      maxLength={1000} rows={3} required disabled={ocupado} />
    <p className={css.comandaApoio}>O relato fica no pedido. Se houver conversa vinculada, o Atendimento recebe o aviso.</p>
    {erro && <p className={css.motivoRecusa} role="alert">{erro}</p>}
    <div className={css.acoesCartao}>
      <Botao tipo="submit" variante="primario" disabled={ocupado || !relato.trim()}>{ocupado ? 'Registrando…' : 'Salvar ocorrência'}</Botao>
      <Botao disabled={ocupado} onClick={() => setAberto(false)}>Voltar</Botao>
    </div>
  </form>
}
