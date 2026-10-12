import { useEffect, useState } from 'react'
import { somarDiasIso } from '../dominio/formato'
import { obterCaixaDoDia } from '../infra/api/caixaApi'

// Os dias anteriores do fluxo de caixa (#1624): o resumo de cada um vem de `api/caixa/dia?data=`,
// a mesma conta do caixa do dia. O dia de hoje não é lido aqui: a tela já o tem e o relê depois de
// cada lançamento, então a linha de hoje nunca discorda do resumo acima dela.
//
// `sessao` (#1627) é o que faz os dias anteriores mudarem sem a data mudar: fechar o caixa esquecido
// troca a situação de um dia passado e devolve à tabela os dias que a sessão cobria. Abrir o caixa
// de hoje não mexe em dia anterior e não relê nada.
//
// estado: carregando | ok | erro
export function useFluxoDeCaixaApi(hoje, diasAntes = 6, sessao = '') {
  // Sem a data de hoje não há dia anterior para pedir: a tabela fica só com a linha de hoje.
  const [carga, setCarga] = useState(() => ({ estado: hoje ? 'carregando' : 'ok', dias: [], erro: null }))
  const [tentativa, setTentativa] = useState(0)

  useEffect(() => {
    if (!hoje) return undefined
    let vivo = true
    const datas = Array.from({ length: diasAntes }, (_, i) => somarDiasIso(hoje, -(i + 1)))
    Promise.all(datas.map((data) => obterCaixaDoDia(data)))
      .then((dias) => { if (vivo) setCarga({ estado: 'ok', dias, erro: null }) })
      .catch((erro) => { if (vivo) setCarga({ estado: 'erro', dias: [], erro: erro.message }) })
    return () => { vivo = false }
  }, [hoje, diasAntes, sessao, tentativa])

  const recarregar = () => {
    setCarga((atual) => ({ ...atual, estado: 'carregando', erro: null }))
    setTentativa((n) => n + 1)
  }
  return { ...carga, recarregar }
}
