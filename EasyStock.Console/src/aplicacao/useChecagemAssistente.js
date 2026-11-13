import { useCallback, useEffect, useState } from 'react'
import { checarAssistente } from '../infra/api/conversasApi'
import { salvarAtendimentoAutomatico } from '../infra/api/configuracaoApi'

// #2496: checagem do Assistente na conversa aberta. Lê ao abrir e a cada "Checar de novo"; mudar a chave da
// loja grava só `ativo` e checa de novo, para a tela mostrar o que o robô faria com a chave nova.
// "Carregando" é a versão pedida ainda não lida: o efeito só grava quando a resposta chega.
export function useChecagemAssistente(conversaId) {
  const [leitura, setLeitura] = useState({ versaoLida: -1, checagem: null, erro: null })
  const [chave, setChave] = useState({ salvando: false, erro: null })
  const [versao, setVersao] = useState(0)

  useEffect(() => {
    if (!conversaId) return undefined
    const controle = new AbortController()
    checarAssistente(conversaId, controle.signal)
      .then((checagem) => { if (!controle.signal.aborted) setLeitura({ versaoLida: versao, checagem, erro: null }) })
      .catch((erro) => { if (!controle.signal.aborted) setLeitura((atual) => ({ ...atual, versaoLida: versao, erro: erro.message })) })
    return () => controle.abort()
  }, [conversaId, versao])

  const checarDeNovo = useCallback(() => setVersao((v) => v + 1), [])
  const mudarChave = useCallback(async (ativo) => {
    setChave({ salvando: true, erro: null })
    try {
      await salvarAtendimentoAutomatico(ativo)
      setChave({ salvando: false, erro: null })
      setVersao((v) => v + 1)
    } catch (erro) {
      setChave({ salvando: false, erro: erro.message })
    }
  }, [])

  const carregando = leitura.versaoLida !== versao
  return {
    carregando,
    checagem: leitura.checagem,
    erro: carregando ? null : leitura.erro,
    salvandoChave: chave.salvando,
    erroChave: chave.erro,
    checarDeNovo,
    mudarChave,
  }
}
