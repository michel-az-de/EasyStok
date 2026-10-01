import { useAcoes, useAtendimento } from '../aplicacao/contextos'
import { avisoDeVencimento } from '../dominio/sessao'
import css from './faixaApi.module.css'

// Faixa fina do modo API (F01): quem está logado, em qual empresa, se a lista
// está atualizando e o último aviso de ação recusada ou ainda não ligada. Some no
// modo demonstração. O aviso fica até a dona fechar (F06): a sincronização não o apaga.
export function FaixaApi({ aoSair }) {
  const { fonteApi, sincronizacao, sessao, agora } = useAtendimento()
  const { fecharAvisoApi } = useAcoes()
  if (!fonteApi) return null
  // F07, item 6: 10 min antes do JWT vencer. Sessão antiga, sem `venceEm`, usa o `expiraEm`.
  const vencimento = avisoDeVencimento(sessao?.venceEm ?? (sessao?.expiraEm ? sessao.expiraEm + 60000 : null), agora)
  const falhou = sincronizacao?.estado === 'erro'
  const carregando = sincronizacao?.estado === 'carregando'
  const aviso = !carregando && !falhou ? sincronizacao?.aviso ?? null : null
  const texto = carregando
    ? 'Carregando conversas…'
    : falhou
      ? `Sem atualizar: ${sincronizacao.mensagem}`
      : aviso ?? 'Conversas ao vivo do EasyStok'
  return (
    <div className={`${css.faixa} ${falhou || aviso ? css.alerta : ''}`} role="status">
      <span className={css.texto}>
        {texto}
        {aviso && (
          <button type="button" className={css.sair} onClick={fecharAvisoApi}>Fechar aviso</button>
        )}
      </span>
      <span className={css.quem}>
        {vencimento && <strong className={css.vence}>{vencimento}</strong>}
        {sessao?.usuario?.nome}{sessao?.empresa ? ` · ${sessao.empresa.nome}` : ''}
        <button type="button" className={css.sair} onClick={aoSair}>Sair</button>
      </span>
    </div>
  )
}
