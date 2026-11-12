import { useCallback, useEffect, useState } from 'react'
import { listarFechamentos, obterCaixaDoDia } from '../infra/api/caixaApi'
import { ligarRecargaAoVivo } from './recargaAoVivo'
import { criarGeracaoDeLeitura, criarVigiaDoDia, recarregaCaixaCom } from './caixaAoVivo'

const CONFERE_O_DIA_MS = 60000

// Caixa do dia lido da API (#1443): o resumo, os lançamentos e os últimos fechamentos. A aba Caixa e
// o gesto de abrir a loja leem daqui e chamam `recarregar` depois de cada ação; nada fica no
// reducer, porque o saldo esperado é conta da API, não do navegador.
//
// estado: carregando | ok | erro
export function useCaixaDoDiaApi({ fechamentos: quantosFechamentos = 7 } = {}) {
  const [carga, setCarga] = useState({ estado: 'carregando', dia: null, fechamentos: [], erro: null })
  const [geracao] = useState(criarGeracaoDeLeitura)

  // #2005 (console-02): devolve o dia lido, ou nulo quando a leitura falhou. Quem vai fechar o caixa precisa
  // saber se o número que tem em mãos é de agora.
  // #2378: leitura que chega depois de uma mais nova não sobrescreve a tela (a carga inicial, o SSE e a ação
  // do operador disparam leituras em paralelo).
  const recarregar = useCallback(() => {
    const minha = geracao.nova()
    return Promise.all([obterCaixaDoDia(), listarFechamentos(quantosFechamentos)])
      .then(([dia, fechamentos]) => { if (geracao.vale(minha)) setCarga({ estado: 'ok', dia, fechamentos, erro: null }); return dia })
      .catch((erro) => { if (geracao.vale(minha)) setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message })); return null })
  }, [quantosFechamentos, geracao])

  useEffect(() => {
    recarregar()
    return () => { geracao.nova() }
  }, [recarregar, geracao])

  // #2378: o caixa muda sem o operador tocar nele. Relê a cada evento de pedido/pagamento, ao voltar para a
  // aba e na virada do dia no fuso da loja; sem isso "Na gaveta" e "Hoje" ficam velhos.
  useEffect(() => {
    const vigia = criarVigiaDoDia()
    const desligar = ligarRecargaAoVivo({ recarregar, recarregaCom: recarregaCaixaCom })
    const aoVoltar = () => { if (document.visibilityState !== 'hidden') recarregar() }
    const conferirODia = setInterval(() => { if (vigia.virou()) recarregar() }, CONFERE_O_DIA_MS)
    document.addEventListener('visibilitychange', aoVoltar)
    window.addEventListener('focus', aoVoltar)
    return () => {
      desligar()
      clearInterval(conferirODia)
      document.removeEventListener('visibilitychange', aoVoltar)
      window.removeEventListener('focus', aoVoltar)
    }
  }, [recarregar])

  return { ...carga, recarregar }
}
