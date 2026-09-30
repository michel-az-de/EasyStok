// Criadores de ação da Frente Entregas e integrações (rodada 13, issue #46,
// registro 108). `AtendimentoProvider.jsx` e `useEspelhoDeEntregas.js`
// chamam `criarAcoesIntegracoes(despachar)` e espalham o resultado no objeto
// `acoes`, mesmo padrão de `acoes/entregas.js` (F6): esta frente só mexe
// aqui, nunca nos dois arquivos acima além da linha de registro.
//
// `cotarEntrega` NÃO despacha nada: é a mesma ideia de `emitirCobranca`
// (`infra/provedoresDeCobranca.js`, chamado de dentro de `acoes/cobranca.js`)
// só que devolvendo o valor para a TELA mostrar antes da dona confirmar a
// chamada — cotar não muda estado nenhum, só chamar muda.
import * as acao from '../acoes'
import {
  cancelar as cancelarNoProvedor, chamar as chamarNoProvedor, cotar as cotarNoProvedor, consultarStatus,
} from '../../infra/provedoresDeEntrega'

const novoId = () => crypto.randomUUID()

export function criarAcoesIntegracoes(despachar) {
  return {
    alternarProvedorLogistica: (chave) => despachar({ tipo: acao.ALTERNAR_PROVEDOR_LOGISTICA, chave }),
    salvarCredencialProvedor: (chave, credencial) => despachar({
      tipo: acao.SALVAR_CREDENCIAL_PROVEDOR, chave, credencial,
    }),
    definirProvedorPadrao: (chave) => despachar({ tipo: acao.DEFINIR_PROVEDOR_PADRAO, chave }),

    // Cotação simulada (estudo do adapter, `infra/provedoresDeEntrega.js:
    // cotar`): a tela mostra preço e prazo ANTES de chamar, para a Thati
    // decidir se vale a pena.
    cotarEntrega: (provedor, pedido, bairro) => cotarNoProvedor({ provedor, pedido, bairro }),

    // "Chamar entregador" com provedor integrado: efetiva a corrida
    // (`infra/provedoresDeEntrega.js: chamar`) e só então grava no estado, com
    // o código do provedor já em mãos (ponto onde o `POST /v3/orders` real da
    // Lalamove entraria).
    chamarEntregadorComProvedor: async (viagemId, agora, provedor, cotacao, pedido) => {
      const { codigo } = await chamarNoProvedor({ provedor, pedido })
      despachar({
        tipo: acao.CHAMAR_ENTREGADOR_PROVEDOR, viagemId, agora, provedor, cotacao, codigo,
      })
    },

    cancelarCorrida: async (viagemId, provedor, codigo) => {
      await cancelarNoProvedor({ provedor, codigo })
      despachar({ tipo: acao.AVANCAR_CORRIDA, viagemId, status: 'cancelado', mensagemId: novoId() })
    },

    // Um tique da simulação (`useSimulacaoDeCorrida`, em
    // `PainelViagem.jsx`): sonda o próximo status simulado e só despacha o
    // que voltou, sem inventar nada aqui.
    avancarCorrida: async (viagemId, agora, provedor, statusAtual) => {
      const { status, entregador } = await consultarStatus({ provedor, status: statusAtual })
      despachar({
        tipo: acao.AVANCAR_CORRIDA, viagemId, agora, status, entregador, mensagemId: novoId(),
      })
    },
  }
}
