// Frente 79 (rodada 10) · o observador do cliente simulado que reage às
// ações da Thatiane (registro 79). Mesmo molde de `useAvisoSonoro.js`: um
// hook de `aplicacao` que só observa `estado.conversas` e agenda, nunca um
// componente de tela. Montado uma vez em `app/App.jsx` (não em
// `features/simulacoes/*`, que desmonta quando a gaveta fecha, e o cliente
// simulado precisa continuar reagindo mesmo com a gaveta fechada).
//
// Fronteira (não editado): `dominio/simulacoes.js: respostaParaMensagemDeCliente`
// e `dominio/automacao.js`, da frente 80. Tudo aqui despacha a ação nova
// `SIMULAR_EVENTO_CLIENTE` (aplicacao/casos/simulacao.js), ao lado.
import { useEffect, useRef } from 'react'
import { useAcoes, useCatalogo } from './contextos'
import { proximoId, proximoNumeroPedido } from '../infra/repositorioConversas'
import {
  ATRASO_ESCOLHA_RECORRENTE_MS, ATRASO_CONFIRMA_RECEBIMENTO_MS, ATRASO_PROMPT_AVALIACAO_MS,
  ATRASO_RESPOSTA_AVALIACAO_MS, MINUTOS_TRAJETO_ENTREGA, atrasoAleatorioMs,
  escolhaRecorrente, textoConfirmaRecebimento, TEXTO_PROMPT_AVALIACAO, AVALIACOES,
  textoRespostaAvaliacao, RELATO_AVALIACAO_NEGATIVA,
} from '../dominio/clienteSimulado'

const ehConversaSimulada = (id) => id.startsWith('sim-')

export function useReacaoClienteSimulado({ conversas, agora }) {
  const { reagirComoClienteSimulado, deslocarRelogioSimulado } = useAcoes()
  const { cardapio } = useCatalogo()

  // Guarda em memória (nunca reagenda o que já mandou para o setTimeout) MAIS
  // o marcador persistido na própria conversa (`reacoesClienteFeitas`, grava
  // no reducer): a primeira evita agendar duas vezes no mesmo StrictMode
  // render, a segunda sobrevive a um F5 no meio do caminho.
  const agendadosRef = useRef(new Set())
  const timersRef = useRef([])
  const agoraRef = useRef(agora)
  useEffect(() => { agoraRef.current = agora }, [agora])
  useEffect(() => () => timersRef.current.forEach(clearTimeout), [])

  const jaAgendado = (id, marcador) => agendadosRef.current.has(`${id}:${marcador}`)
  const marcarAgendado = (id, marcador) => agendadosRef.current.add(`${id}:${marcador}`)
  const agendar = (ms, fn) => { timersRef.current.push(setTimeout(fn, ms)) }

  useEffect(() => {
    for (const conversa of conversas) {
      if (!ehConversaSimulada(conversa.id)) continue
      const feitas = conversa.reacoesClienteFeitas ?? []

      // (b) US-002, RN-07, UC-03 passo 4: recorrente responde à oferta de
      // favorito ou novidade, e a comanda nasce com o item certo.
      const mEscolha = 'escolha-recorrente'
      if (!feitas.includes(mEscolha) && !jaAgendado(conversa.id, mEscolha) && !conversa.pedido) {
        const ofertou = conversa.mensagens.some((m) => m.automatica && m.regra === 'saudacao-recorrente')
        if (ofertou) {
          const escolha = escolhaRecorrente(conversa.cliente?.tags, cardapio)
          if (escolha) {
            marcarAgendado(conversa.id, mEscolha)
            agendar(atrasoAleatorioMs(...ATRASO_ESCOLHA_RECORRENTE_MS), () => {
              reagirComoClienteSimulado({
                id: conversa.id, dir: 'in', texto: escolha.texto, marcador: mEscolha,
                item: { sku: escolha.item.sku }, numeroPedido: proximoNumeroPedido(),
                agora: agoraRef.current, mensagemId: proximoId('sim-msg'),
              })
            })
          }
        }
      }

      // (c) UC-04 passo 8, US-045: confirmação de recebimento logo depois do
      // aviso de saída, com minutos de trajeto somados ao relógio simulado.
      const mRecebimento = 'confirma-recebimento'
      if (!feitas.includes(mRecebimento) && !jaAgendado(conversa.id, mRecebimento)
        && conversa.pedido?.estado === 'entrega') {
        marcarAgendado(conversa.id, mRecebimento)
        agendar(atrasoAleatorioMs(...ATRASO_CONFIRMA_RECEBIMENTO_MS), () => {
          const minutos = Math.round(atrasoAleatorioMs(...MINUTOS_TRAJETO_ENTREGA))
          deslocarRelogioSimulado(minutos)
          reagirComoClienteSimulado({
            id: conversa.id, dir: 'in', texto: textoConfirmaRecebimento(), marcador: mRecebimento,
            agora: agoraRef.current + minutos * 60000, mensagemId: proximoId('sim-msg'),
          })
        })
      }

      // (d) US-046, RN-34, RN-35: avaliação de um toque, distinta da
      // reclamação por texto livre (achado P2.7). Só corre quando o cenário
      // marcou `avaliacaoPendente` na conversa (infra/roteirosSimulacao.js).
      if (conversa.avaliacaoPendente) {
        const mPrompt = 'avaliacao-prompt'
        if (!feitas.includes(mPrompt) && !jaAgendado(conversa.id, mPrompt)) {
          marcarAgendado(conversa.id, mPrompt)
          agendar(atrasoAleatorioMs(...ATRASO_PROMPT_AVALIACAO_MS), () => {
            reagirComoClienteSimulado({
              id: conversa.id, dir: 'out', texto: TEXTO_PROMPT_AVALIACAO, formato: 'avaliacaoPedido',
              marcador: mPrompt, agora: agoraRef.current, mensagemId: proximoId('sim-msg'),
            })
          })
        }
        const mResposta = 'avaliacao-resposta'
        if (feitas.includes(mPrompt) && !feitas.includes(mResposta) && !jaAgendado(conversa.id, mResposta)) {
          marcarAgendado(conversa.id, mResposta)
          const valor = conversa.avaliacaoPendente
          agendar(atrasoAleatorioMs(...ATRASO_RESPOSTA_AVALIACAO_MS), () => {
            reagirComoClienteSimulado({
              id: conversa.id, dir: 'in', texto: textoRespostaAvaliacao(valor), formato: 'avaliacaoResposta',
              valor, marcador: mResposta, agora: agoraRef.current, mensagemId: proximoId('sim-msg'),
              ...(valor === AVALIACOES.NEGATIVA ? {
                abrirOcorrenciaComRelato: RELATO_AVALIACAO_NEGATIVA, ocorrenciaId: proximoId('sim-ocorrencia'),
              } : {}),
            })
          })
        }
      }
    }
    // `cardapio` entra porque a escolha do favorito depende do estoque do
    // dia; `reagirComoClienteSimulado`/`deslocarRelogioSimulado` são estáveis
    // (useMemo do Provider) e não precisam disparar o efeito de novo.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [conversas, cardapio])
}
