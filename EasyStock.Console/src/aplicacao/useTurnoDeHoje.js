import { useCallback, useEffect, useState } from 'react'
import { contextoCadastro } from '../infra/api/cadastrosApi'
import { contarPedidos } from '../infra/api/pedidosCadastroApi'
import { dataIsoNoFuso } from '../dominio/formato'
import { GRUPOS_DE_PEDIDOS, registrarUso } from '../dominio/turnoDeHoje'
import { ligarRecargaAoVivo } from './recargaAoVivo'
import { criarGeracaoDeLeitura } from './caixaAoVivo'

const recarregaPedidosCom = (evento) => evento === 'ready' || evento.startsWith('pedido.') || evento.startsWith('pagamento.')

// #2832: pedidos de hoje por monte (GRUPOS_DE_PEDIDOS) e o total. Relê a cada evento de pedido ou pagamento e na
// conferência de 30 s do SSE (a mesma regra da Cozinha e do Caixa); o dia é o da loja, lido a cada leitura.
// estado: carregando | ok | erro
export function usePedidosDeHoje({ ligado = true } = {}) {
  const [carga, setCarga] = useState({ estado: 'carregando', contagens: null, total: null, erro: null })
  const [geracao] = useState(criarGeracaoDeLeitura)

  const recarregar = useCallback(() => {
    const minha = geracao.nova()
    const contexto = contextoCadastro()
    const desde = dataIsoNoFuso(Date.now())
    return Promise.all([
      contarPedidos({ desde }, contexto),
      ...GRUPOS_DE_PEDIDOS.map((g) => contarPedidos({ status: g.status, desde }, contexto)),
    ])
      .then(([total, ...porGrupo]) => {
        if (!geracao.vale(minha)) return
        const contagens = Object.fromEntries(GRUPOS_DE_PEDIDOS.map((g, i) => [g.chave, porGrupo[i]]))
        setCarga({ estado: 'ok', contagens, total, erro: null })
      })
      .catch((erro) => { if (geracao.vale(minha)) setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message })) })
  }, [geracao])

  useEffect(() => {
    if (!ligado) return undefined
    recarregar()
    const desligar = ligarRecargaAoVivo({ recarregar, recarregaCom: recarregaPedidosCom })
    return () => { desligar(); geracao.nova() }
  }, [ligado, recarregar, geracao])

  return { ...carga, recarregar }
}

// "Você mais usa" (#2832): a contagem de aberturas por tela fica no navegador, por pessoa e loja. Sem
// armazenamento (aba anônima, cota cheia), a faixa só não aparece.
const CHAVE_DO_USO = (identidade) => `cdb:gestao-uso:v1:${encodeURIComponent(identidade ?? 'anonimo')}`

export function lerUsoDaGestao(identidade, armazenamento) {
  try {
    const dados = JSON.parse((armazenamento ?? globalThis.localStorage)?.getItem(CHAVE_DO_USO(identidade)) ?? '{}')
    return dados && typeof dados === 'object' && !Array.isArray(dados) ? dados : {}
  } catch { return {} }
}

export function contarUsoDaGestao(identidade, aba, armazenamento) {
  const proximo = registrarUso(lerUsoDaGestao(identidade, armazenamento), aba)
  try { (armazenamento ?? globalThis.localStorage)?.setItem(CHAVE_DO_USO(identidade), JSON.stringify(proximo)) } catch { /* sem armazenamento */ }
  return proximo
}

export const identidadeDaGestao = () => contextoCadastro().identidade
