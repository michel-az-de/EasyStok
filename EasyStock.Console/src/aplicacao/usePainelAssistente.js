import { useCallback, useEffect, useState } from 'react'
import { listarHistoricoAssistente, obterFluxoAssistente, obterUsoAssistente } from '../infra/api/assistenteApi'
import { obterConfiguracao, salvarAjusteDoAssistente } from '../infra/api/configuracaoApi'

// #2853: painel do Assistente. Lê uso e fluxo ao abrir e depois de cada ajuste salvo, para a tela mostrar
// o que o robô usa de fato (o fluxo vem da mesma fonte que o turno). Cada ajuste é um PUT só com o campo dele.
export function usePainelAssistente() {
  const [leitura, setLeitura] = useState({ versaoLida: -1, uso: null, fluxo: null, erro: null })
  const [versao, setVersao] = useState(0)
  const [gravacao, setGravacao] = useState({ salvando: null, erro: null, salvo: null })

  useEffect(() => {
    const controle = new AbortController()
    Promise.all([obterUsoAssistente(controle.signal), obterFluxoAssistente(controle.signal)])
      .then(([uso, fluxo]) => { if (!controle.signal.aborted) setLeitura({ versaoLida: versao, uso, fluxo, erro: null }) })
      .catch((erro) => { if (!controle.signal.aborted) setLeitura((atual) => ({ ...atual, versaoLida: versao, erro: erro.message })) })
    return () => controle.abort()
  }, [versao])

  const recarregar = useCallback(() => setVersao((v) => v + 1), [])

  // `qual` nomeia o ajuste na tela ("cota", "roteiro", "ferramentas") para o retorno aparecer ao lado dele.
  const salvar = useCallback(async (qual, ajuste) => {
    setGravacao({ salvando: qual, erro: null, salvo: null })
    try {
      await salvarAjusteDoAssistente(ajuste)
      setGravacao({ salvando: null, erro: null, salvo: qual })
      setVersao((v) => v + 1)
      return true
    } catch (erro) {
      setGravacao({ salvando: null, erro: { qual, mensagem: erro.message }, salvo: null })
      return false
    }
  }, [])

  const carregando = leitura.versaoLida !== versao
  return { carregando, uso: leitura.uso, fluxo: leitura.fluxo, erro: carregando ? null : leitura.erro, gravacao, salvar, recarregar }
}

// Histórico de turnos, página a página; só lê quando a aba aparece.
export function useHistoricoAssistente(ativo) {
  const [pagina, setPagina] = useState(1)
  const [estado, setEstado] = useState({ chave: null, dados: null, erro: null })
  const chave = ativo ? `p${pagina}` : null

  useEffect(() => {
    if (!chave) return undefined
    const controle = new AbortController()
    listarHistoricoAssistente({ pagina, tamanhoPagina: 20 }, controle.signal)
      .then((dados) => { if (!controle.signal.aborted) setEstado({ chave, dados, erro: null }) })
      .catch((erro) => { if (!controle.signal.aborted) setEstado((atual) => ({ ...atual, chave, erro: erro.message })) })
    return () => controle.abort()
  }, [chave, pagina])

  return { carregando: Boolean(chave) && estado.chave !== chave, dados: estado.dados, erro: estado.erro, pagina, setPagina }
}

// #3149: dicionário da loja. Só lê quando `ativo` (o GET da configuração é de quem administra) e não relê ao voltar à
// aba: `recarregar` é quem pede outra leitura, depois de salvar. `leitura` conta as leituras feitas, para a aba
// recomeçar o rascunho do que a API devolveu.
export function useDicionarioDaLoja(ativo) {
  const [versao, setVersao] = useState(0)
  const [estado, setEstado] = useState({ versaoLida: -1, leitura: 0, dicionario: null, erro: null })

  useEffect(() => {
    if (!ativo) return undefined
    const controle = new AbortController()
    obterConfiguracao()
      .then((configuracao) => { if (!controle.signal.aborted) setEstado((a) => ({ versaoLida: versao, leitura: a.leitura + 1, dicionario: configuracao?.dicionario ?? [], erro: null })) })
      .catch((erro) => { if (!controle.signal.aborted) setEstado((a) => ({ ...a, versaoLida: versao, erro: erro.message })) })
    return () => controle.abort()
  }, [ativo, versao])

  const recarregar = useCallback(() => setVersao((v) => v + 1), [])
  // `definir`: o que o PUT acabou de gravar vira o valor salvo, sem outra leitura.
  const definir = useCallback((dicionario) => setEstado((a) => ({ ...a, dicionario })), [])
  return { carregando: ativo && estado.versaoLida !== versao, dicionario: estado.dicionario, leitura: estado.leitura, erro: estado.versaoLida === versao ? estado.erro : null, recarregar, definir }
}
