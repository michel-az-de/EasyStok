import { hashDoModulo } from '../dominio/modulos'
import { useIntegracoesApi } from '../aplicacao/useIntegracoesApi'
import { textoDaFaixa } from '../dominio/chavesIntegracao'
import { useEffect, useRef } from 'react'
import { useAcoes, useAtendimento } from '../aplicacao/contextos'
import { avisoDeVencimento, rotuloDaSessao } from '../dominio/sessao'
import { useHash } from '../hooks/useHash'
import { DESKTOP, useTamanhoTela } from '../hooks/useTamanhoTela'
import css from './faixaApi.module.css'

// #1474: trocar de tela apaga o aviso da faixa, que era da tela de antes. Fica montado junto do
// provedor (as telas e a faixa remontam a cada rota; o aviso mora no provedor e sobrevive).
// Aviso persistente de configuração (`avisoFixo`, ex.: loja online desligada) não sai aqui:
// `fecharAvisoApi()` sem `fixo` só apaga o aviso de ação.
export function LimparAvisoAoNavegar() {
  const { fecharAvisoApi } = useAcoes()
  const hash = useHash()
  const hashAnterior = useRef(hash)
  useEffect(() => {
    if (hashAnterior.current === hash) return
    hashAnterior.current = hash
    fecharAvisoApi?.()
  }, [hash, fecharAvisoApi])
  return null
}

// O dialog torna o restante da página inerte; a resposta à ação precisa estar dentro dele.
export function AvisoDaAcaoApi() {
  const { fonteApi, sincronizacao } = useAtendimento()
  const { fecharAvisoApi } = useAcoes()
  if (!fonteApi || !sincronizacao?.aviso) return null
  return (
    <output className={css.avisoModal}>
      <span>{sincronizacao.aviso}</span>
      <button type="button" className={css.sair} onClick={() => fecharAvisoApi()}>Fechar aviso</button>
    </output>
  )
}

// Faixa fina do modo API (F01): quem está logado, em qual empresa, se a lista
// está atualizando e o último aviso de ação recusada ou ainda não ligada. Some no
// modo demonstração. O aviso fica até a dona fechar (F06): a sincronização não o apaga.
// #1870: no desktop (e nos módulos, `somenteAvisos`) a faixa só aparece com aviso, falha,
// integração parada ou sessão vencendo. Quem está logado e o Sair moram no avatar do trilho.
export function FaixaApi({ aoSair, somenteAvisos = false }) {
  const { fonteApi, sincronizacao, sessao, agora } = useAtendimento()
  const { fecharAvisoApi } = useAcoes()
  const enxuta = useTamanhoTela() === DESKTOP || somenteAvisos
  const { lista: integracoes } = useIntegracoesApi({ ativo: fonteApi, intervaloMs: 60_000 })
  const integracaoParada = textoDaFaixa(integracoes)
  if (!fonteApi) return null
  // F07, item 6: 10 min antes do JWT vencer. Sessão antiga, sem `venceEm`, usa o `expiraEm`.
  const vencimento = !sessao?.persistente && avisoDeVencimento(sessao?.venceEm ?? (sessao?.expiraEm ? sessao.expiraEm + 60000 : null), agora)
  const falhou = sincronizacao?.estado === 'erro'
  const carregando = sincronizacao?.estado === 'carregando'
  // O aviso de ação vem na frente; fechado ele, aparece o persistente, que só sai pelo botão.
  const avisoDeAcao = sincronizacao?.aviso ?? null
  const aviso = !carregando && !falhou ? avisoDeAcao ?? sincronizacao?.avisoFixo ?? null : null
  if (enxuta && !falhou && !aviso && !integracaoParada && !vencimento) return null
  const texto = carregando
    ? 'Carregando conversas…'
    : falhou
      ? `Conversas sem atualização: ${sincronizacao.mensagem || 'a consulta não foi concluída.'} A próxima tentativa é automática.`
      : aviso ?? 'Conversas ao vivo do EasyStok'
  return (
    <output className={`${css.faixa} ${falhou ? css.falha : aviso ? css.aviso : ''}`}>
      {(!enxuta || falhou || aviso) && <span className={css.texto}>
        {texto}
        {aviso && (
          <button type="button" className={css.sair} onClick={() => fecharAvisoApi({ fixo: !avisoDeAcao })}>Fechar aviso</button>
        )}
      </span>}
      {integracaoParada && <span className={css.texto}>{integracaoParada} <a href={hashDoModulo('configuracoes', 'integracoes')}>Ver integrações</a></span>}
      <span className={css.quem}>
        {vencimento && <strong className={css.vence}>{vencimento}</strong>}
        {!enxuta && rotuloDaSessao(sessao)}
        {!enxuta && <button type="button" className={css.sair} onClick={aoSair}>Sair</button>}
      </span>
    </output>
  )
}
