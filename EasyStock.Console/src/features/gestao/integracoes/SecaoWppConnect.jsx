import { useEffect } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import { useWppConnectApi } from '../../../aplicacao/useWppConnectApi'
import css from './SecaoWhatsApp.module.css'

export function SecaoWppConnect({ aoConectar }) {
  const { sessao, erro, ocupado, atualizar, conectar, reconectar, desconectar } = useWppConnectApi()
  const conectado = sessao?.conectado
  const vinculado = sessao?.vinculado
  useEffect(() => { if (vinculado !== undefined) aoConectar?.() }, [conectado, vinculado, aoConectar])
  if (!sessao?.habilitado && !erro) return null

  return (
    <div className={css.resultado}>
      <h4 className={css.titulo}>Conexão provisória por QR</h4>
      <p className={css.texto}>Use um número de teste enquanto a conexão pela Meta está pendente.
        Esta conexão não oficial pode sofrer interrupções ou bloqueio do número.</p>
      {sessao && <Pilula tom={sessao.conectado ? 'ok' : 'aviso'}>
        {ocupado ? 'Atualizando a conexão…' : sessao.conectado ? 'WhatsApp conectado' : sessao.estado === 'INITIALIZING' ? 'Gerando QR…' : sessao.estado === 'INDISPONIVEL' ? 'Não foi possível confirmar a conexão' : sessao.qrCode ? 'Aguardando leitura do QR' : sessao.vinculado ? 'WhatsApp desconectado' : 'Número ainda não conectado'}
      </Pilula>}
      {!ocupado && sessao?.qrCode && <>
        <p className={css.texto}>No celular: WhatsApp Business → Aparelhos conectados → Conectar aparelho.</p>
        <img className={css.qr} src={sessao.qrCode} alt="QR para conectar o número de teste ao chat" />
      </>}
      <div className={css.acoes}>
        {!sessao?.vinculado && <Botao variante="primario" onClick={conectar} disabled={ocupado}>Conectar por QR</Botao>}
        {sessao?.vinculado && <Botao onClick={sessao.qrCode ? atualizar : reconectar} disabled={ocupado || sessao.estado === 'INITIALIZING'}>{sessao.qrCode ? 'Atualizar QR' : 'Reconectar'}</Botao>}
        <Botao onClick={atualizar} disabled={ocupado}>Atualizar conexão</Botao>
        {sessao?.vinculado && <Botao onClick={desconectar} disabled={ocupado}>Desconectar número</Botao>}
      </div>
      {ocupado && <p className={css.texto} role="status">Atualizando a conexão…</p>}
      {erro && <p className={css.erro} role="alert">{erro}</p>}
      <p className={css.explicacao}>O piloto recebe texto, imagem e áudio e permite responder pelo chat.
        Modelos aprovados pela Meta não estão disponíveis nesta conexão.</p>
    </div>
  )
}
