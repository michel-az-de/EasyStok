import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import css from './SecaoWhatsApp.module.css'

function textoDoStatus(sessao, ocupado) {
  if (ocupado) return 'Atualizando a conexão…'
  if (sessao.conectado) return 'WhatsApp conectado'
  if (sessao.estado === 'INITIALIZING') return 'Gerando QR…'
  if (sessao.estado === 'INDISPONIVEL') return 'Não foi possível confirmar a conexão'
  if (sessao.qrCode) return 'Aguardando leitura do QR'
  return sessao.vinculado ? 'WhatsApp desconectado' : 'Número ainda não conectado'
}

// #2258 (auditoria G1): o estado fica no topo da seção; "Atualizar conexão" é texto (ação leve) e
// "Desconectar número" é perigo e pede confirmação antes, no mesmo lugar, sem modal.
// `wpp` é o retorno de useWppConnectApi(), levantado para a SecaoWhatsApp decidir qual conexão oferecer.
export function SecaoWppConnect({ wpp, aoConectar }) {
  const { sessao, erro, ocupado, atualizar, conectar, reconectar, desconectar } = wpp
  const [confirmando, setConfirmando] = useState(false)
  const conectado = sessao?.conectado
  const vinculado = sessao?.vinculado
  useEffect(() => { if (vinculado !== undefined) aoConectar?.() }, [conectado, vinculado, aoConectar])
  // #2258 (banca a11y): abrir a confirmação leva o foco a "Manter conectado"; manter devolve o foco a
  // "Desconectar número". Sem isto o foco caía no body quando o botão some.
  const manterRef = useRef(null)
  const desconectarRef = useRef(null)
  const voltarAoDesconectar = useRef(false)
  useEffect(() => {
    if (confirmando) manterRef.current?.focus()
    else if (voltarAoDesconectar.current) { voltarAoDesconectar.current = false; desconectarRef.current?.focus() }
  }, [confirmando])
  if (!sessao?.habilitado && !erro) return null

  const pedirDesconexao = () => setConfirmando(true)
  const manter = () => { voltarAoDesconectar.current = true; setConfirmando(false) }
  const confirmarDesconexao = async () => { await desconectar(); setConfirmando(false) }

  return (
    <div className={css.resultado}>
      <h4 className={css.titulo}>Conexão provisória por QR</h4>
      {sessao && <Pilula tom={sessao.conectado ? 'ok' : 'aviso'}>{textoDoStatus(sessao, ocupado)}</Pilula>}
      <p className={css.texto}>Use um número de teste enquanto a conexão pela Meta está pendente.
        Esta conexão não oficial pode sofrer interrupções ou bloqueio do número.</p>
      {!ocupado && !conectado && sessao?.qrCode && <>
        <p className={css.texto}>No celular: WhatsApp Business → Aparelhos conectados → Conectar aparelho.</p>
        <img className={css.qr} src={sessao.qrCode} alt="QR para conectar o número de teste ao chat" />
      </>}
      {!confirmando && <div className={css.acoes}>
        {!sessao?.vinculado && <Botao variante="primario" onClick={conectar} disabled={ocupado}>Conectar por QR</Botao>}
        {sessao?.vinculado && !conectado && <Botao onClick={sessao.qrCode ? atualizar : reconectar} disabled={ocupado || sessao.estado === 'INITIALIZING'}>{sessao.qrCode ? 'Atualizar QR' : 'Reconectar'}</Botao>}
        <Botao variante="texto" onClick={atualizar} disabled={ocupado}>Atualizar conexão</Botao>
        {sessao?.vinculado && <Botao ref={desconectarRef} variante="perigo" onClick={pedirDesconexao} disabled={ocupado}>Desconectar número</Botao>}
      </div>}
      {confirmando && (
        <div className={css.confirmacao} role="group" aria-label="Confirmar desconexão do número">
          <p className={css.texto}>Desconectar este número? O chat para de receber e de enviar mensagens por ele até você conectar de novo.</p>
          <div className={css.acoes}>
            <Botao ref={manterRef} variante="texto" disabled={ocupado} onClick={manter}>Manter conectado</Botao>
            <Botao variante="perigo" disabled={ocupado} onClick={confirmarDesconexao}>{ocupado ? 'Desconectando…' : 'Confirmar desconexão'}</Botao>
          </div>
        </div>
      )}
      {ocupado && !confirmando && <p className={css.texto} role="status">Atualizando a conexão…</p>}
      {erro && <p className={css.erro} role="alert">{erro}</p>}
      <p className={css.explicacao}>A conexão provisória recebe texto, imagem e áudio e permite responder pelo chat.
        Responda pelo chat: respostas feitas no celular não são sincronizadas por esta conexão. Modelos aprovados pela Meta não estão disponíveis.</p>
    </div>
  )
}
