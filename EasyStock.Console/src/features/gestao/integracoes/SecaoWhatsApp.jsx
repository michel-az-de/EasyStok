import { useEffect } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import { useConexaoWhatsAppApi } from '../../../aplicacao/useConexaoWhatsAppApi'
import { useWppConnectApi } from '../../../aplicacao/useWppConnectApi'
import { textoDaSincronizacao } from '../../../dominio/embeddedSignup'
import css from './SecaoWhatsApp.module.css'
import { SecaoWppConnect } from './SecaoWppConnect'

// WhatsApp da loja por coexistência (#1417): o número continua no app WhatsApp
// Business do celular e passa a falar também pelo EasyStok. Só no modo API.
const ROTULO_BOTAO = {
  carregando: 'Carregando…',
  aguardando: 'Aguardando a Meta…',
  conectando: 'Conectando…',
}

function Resultado({ resultado }) {
  return (
    <div className={css.resultado} aria-live="polite">
      <p className={css.numero}>
        <strong>{resultado.displayPhoneNumber ?? resultado.phoneNumberId}</strong>
        {resultado.verifiedName ? ` · ${resultado.verifiedName}` : ''}
      </p>
      {resultado.foraDoAppBusiness
        ? <Pilula tom="aviso">Conectado, mas a Meta não confirma o número no app WhatsApp Business</Pilula>
        : <Pilula tom="ok">Conectado e no app WhatsApp Business</Pilula>}
      <ul className={css.syncs}>
        <li>{textoDaSincronizacao('Contatos do app', resultado.sincronizacaoEstadoApp)}</li>
        <li>Histórico anterior: não importado nesta versão</li>
      </ul>
    </div>
  )
}

// #1447: a tela Canais monta a seção `embutida` (sem título nem moldura, o cabeçalho é dela)
// e recarrega o estado do canal quando a conexão termina (`aoConectar`).
export function SecaoWhatsApp({ embutida = false, aoConectar, canalLigado = false }) {
  const { estado, erro, resultado, conectar } = useConexaoWhatsAppApi()
  const wpp = useWppConnectApi()
  // #2258 (auditoria G1): já conectado por QR, a Meta deixa de ser "conectar" e vira "migrar", e o
  // estado da conexão que está valendo sobe para o topo da seção.
  const conectada = Boolean(wpp.sessao?.conectado)
  useEffect(() => { if (resultado) aoConectar?.() }, [resultado, aoConectar])
  const ocupado = estado in ROTULO_BOTAO
  const desabilitado = ocupado || estado === 'indisponivel'

  // #2258 (banca a11y): a SecaoWppConnect fica sempre na mesma posição da árvore; o que muda de lugar
  // é o bloco da Meta, que não guarda estado. Assim a confirmação aberta não some quando o polling
  // vira "conectado", e o aviso de conexão não roda de novo a cada troca.
  const meta = <>
    {estado !== 'indisponivel' && <p className={css.texto}>
      Na conexão pela Meta, o app WhatsApp Business continua funcionando e as respostas
      feitas pelo celular aparecem aqui na conversa.
    </p>}
    {estado !== 'indisponivel' && (conectada ? (
      <Botao variante="secundario" className={css.botao} onClick={conectar} disabled={desabilitado} aria-busy={ocupado}>
        {ROTULO_BOTAO[estado] ?? 'Migrar para conexão oficial'}
      </Botao>
    ) : (
      <Botao variante="primario" className={css.botao} onClick={conectar} disabled={desabilitado} aria-busy={ocupado}>
        {ROTULO_BOTAO[estado] ?? 'Conectar WhatsApp Business'}
      </Botao>
    ))}
    {estado === 'indisponivel' && (
      <p className={css.explicacao} role="note">
        {canalLigado
          ? 'O canal segue ligado. A conexão de outro número pela Meta ainda não está habilitada.'
          : 'A conexão pela Meta ainda não está habilitada nesta loja. Peça ao responsável pela configuração para ativá-la.'}
      </p>
    )}
    {estado === 'aguardando' && (
      <p className={css.explicacao}>Conclua os passos na janela da Meta. O resultado da conexão aparece aqui.</p>
    )}
    {erro && <p className={css.erro} role="alert">{erro}</p>}
    {resultado && <Resultado resultado={resultado} />}
  </>

  return (
    <section className={embutida ? css.embutida : css.secao} aria-labelledby={embutida ? undefined : 'secao-whatsapp-titulo'}>
      {!embutida && <h3 id="secao-whatsapp-titulo" className={css.titulo}>WhatsApp</h3>}
      {!conectada && meta}
      <SecaoWppConnect wpp={wpp} aoConectar={aoConectar} />
      {conectada && meta}
    </section>
  )
}
