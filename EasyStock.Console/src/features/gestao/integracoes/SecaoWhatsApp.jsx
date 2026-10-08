import { useEffect } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import { useConexaoWhatsAppApi } from '../../../aplicacao/useConexaoWhatsAppApi'
import { textoDaSincronizacao } from '../../../dominio/embeddedSignup'
import css from './SecaoWhatsApp.module.css'

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
export function SecaoWhatsApp({ embutida = false, aoConectar }) {
  const { estado, erro, resultado, conectar } = useConexaoWhatsAppApi()
  useEffect(() => { if (resultado) aoConectar?.() }, [resultado, aoConectar])
  const ocupado = estado in ROTULO_BOTAO
  const desabilitado = ocupado || estado === 'indisponivel'

  return (
    <section className={embutida ? css.embutida : css.secao} aria-labelledby={embutida ? undefined : 'secao-whatsapp-titulo'}>
      {!embutida && <h3 id="secao-whatsapp-titulo" className={css.titulo}>WhatsApp</h3>}
      <p className={css.texto}>
        Conecte o número da loja sem tirá-lo do celular: o app WhatsApp Business continua funcionando, e o
        que a loja responder por ele aparece aqui na conversa.
      </p>
      <Botao variante="primario" className={css.botao} onClick={conectar} disabled={desabilitado} aria-busy={ocupado}>
        {ROTULO_BOTAO[estado] ?? 'Conectar WhatsApp Business'}
      </Botao>
      {estado === 'indisponivel' && (
        <p className={css.explicacao} role="note">
          A conexão ainda não está habilitada neste servidor: falta configurar o app da Meta (AppId,
          configuração do Embedded Signup e segredo). Veja docs/dev/whatsapp-coexistencia.md.
        </p>
      )}
      {estado === 'aguardando' && (
        <p className={css.explicacao}>Siga os passos na janela da Meta. Esta tela termina sozinha quando ela fechar.</p>
      )}
      {erro && <p className={css.erro} role="alert">{erro}</p>}
      {resultado && <Resultado resultado={resultado} />}
    </section>
  )
}
