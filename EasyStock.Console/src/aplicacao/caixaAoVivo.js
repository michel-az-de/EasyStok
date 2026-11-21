import { dataIsoNoFuso } from '../dominio/formato'

// Regras do caixa do dia ao vivo (#2378), puras para a prova rodar sem navegador.

// O que entra no caixa sem o operador tocar nele (Pix confirmado por webhook, "Recebi na porta" nas
// Entregas) chega como evento de pedido ou de pagamento; `ready` é a releitura sem replay do SSE.
export const recarregaCaixaCom = (evento) => evento === 'ready' || evento.startsWith('pedido.') || evento.startsWith('pagamento.')

// A cada leitura nasce uma geração; só a resposta da geração mais nova pode mexer na tela.
export function criarGeracaoDeLeitura() {
  let atual = 0
  return {
    nova: () => { atual += 1; return atual },
    vale: (geracao) => geracao === atual,
  }
}

// Virou o dia no fuso da loja desde a última conferência? Quem confere relê o caixa: "Hoje" é o dia da API.
export function criarVigiaDoDia(agora = () => Date.now()) {
  let visto = dataIsoNoFuso(agora())
  return {
    virou() {
      const hoje = dataIsoNoFuso(agora())
      if (hoje === visto) return false
      visto = hoje
      return true
    },
  }
}
