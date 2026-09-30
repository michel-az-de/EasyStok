import { useAtendimento } from '../aplicacao/contextos'
import css from './faixaApi.module.css'

// Faixa fina do modo API (F01): quem está logado, em qual empresa, se a lista
// está atualizando e o último aviso de ação recusada. Some no modo demonstração.
export function FaixaApi({ aoSair }) {
  const { fonteApi, sincronizacao, sessao } = useAtendimento()
  if (!fonteApi) return null
  const falhou = sincronizacao?.estado === 'erro'
  const texto = sincronizacao?.estado === 'carregando'
    ? 'Carregando conversas…'
    : falhou
      ? `Sem atualizar: ${sincronizacao.mensagem}`
      : sincronizacao?.aviso ?? 'Conversas ao vivo do EasyStok'
  return (
    <div className={`${css.faixa} ${falhou || sincronizacao?.aviso ? css.alerta : ''}`} role="status">
      <span>{texto}</span>
      <span className={css.quem}>
        {sessao?.usuario?.nome}{sessao?.empresa ? ` · ${sessao.empresa.nome}` : ''}
        <button type="button" className={css.sair} onClick={aoSair}>Sair</button>
      </span>
    </div>
  )
}
