import { useAcoes, useAtendimento } from '../aplicacao/contextos'
import { useIntegracoesApi } from '../aplicacao/useIntegracoesApi'
import { textoDaFaixa } from '../dominio/chavesIntegracao'
import { avisoDeVencimento } from '../dominio/sessao'
import css from './faixaApi.module.css'

// F16 (#1246): o vigia testa as integrações a cada 15 min; a faixa relê a cada minuto.
const RELEITURA_INTEGRACOES_MS = 60_000

// Faixa fina do modo API (F01): quem está logado, em qual empresa, se a lista
// está atualizando e o último aviso de ação recusada ou ainda não ligada. Some no
// modo demonstração. O aviso fica até a dona fechar (F06): a sincronização não o apaga.
// F16: integração parada (último teste falhou) acende a faixa com o atalho para a aba.
export function FaixaApi({ aoSair, aoAbrirIntegracoes }) {
  const { fonteApi, sincronizacao, sessao, agora } = useAtendimento()
  const { fecharAvisoApi } = useAcoes()
  const { lista: integracoes } = useIntegracoesApi({ ativo: fonteApi, intervaloMs: RELEITURA_INTEGRACOES_MS })
  if (!fonteApi) return null
  const integracaoParada = textoDaFaixa(integracoes)
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
    <div className={`${css.faixa} ${falhou || aviso || integracaoParada ? css.alerta : ''}`} role="status">
      <span className={css.texto}>
        {integracaoParada && (
          <strong>
            {integracaoParada}{' '}
            <button type="button" className={css.sair} onClick={aoAbrirIntegracoes}>Ver integrações</button>
          </strong>
        )}
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
