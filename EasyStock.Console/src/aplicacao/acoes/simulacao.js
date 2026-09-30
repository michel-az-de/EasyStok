// Criadores de ação da Frente 7 · Menu de simulações (rodada 5, seção 7).
// `AtendimentoProvider.jsx` chama `criarAcoesSimulacao(despachar)` e espalha
// o resultado no objeto `acoes` do contexto; a F7 só mexe aqui dentro, nunca
// no Provider. `despachar` já é o `dispatch` do reducer.
//
// Fronteira de camada (`ferramentas/verificar-camadas.mjs`): `aplicacao` pode
// importar `dominio` e `infra`, mas `features` não pode importar `infra`
// direto. Por isso a resolução do roteiro (que precisa dos textos e dos
// moldes de conversa de `infra/roteirosSimulacao.js`) mora AQUI, não no hook
// `features/simulacoes/useRoteiro.js`: o hook só chama `montarRoteiro` (dado
// pronto, sem import de infra) e depois `executarPassoSimulado` a cada
// `setTimeout`.
import * as acao from '../acoes'
import { proximoId, proximoNumeroPedido } from '../../infra/repositorioConversas'
import { cenarioPorId, deslocamentoParaHora } from '../../dominio/simulacoes'
import {
  ESQUELETOS, FALAS, FALA_VOLUME, FALA_VOLUME_RECLAMACAO, VARIACOES_VOLUME,
} from '../../infra/roteirosSimulacao'

function falaDoPasso(cenario, passo, categoriaAtual) {
  if (cenario.id === 'dez-de-uma-vez') {
    return categoriaAtual?.startsWith('reclamacao') ? FALA_VOLUME_RECLAMACAO : FALA_VOLUME
  }
  return FALAS[cenario.id]?.[passo.indice ?? 0] ?? { texto: '' }
}

// Molde final da conversa: mesmo formato que `infra/massaConversas.js`
// entrega (ultimaEm em ISO, mensagens vazias à espera do evento real), com
// `id` e `cadastroId` resolvidos agora, na hora de montar o roteiro.
function conversaDoEsqueleto(esqueleto, agora) {
  const conversa = {
    estado: 'Aberto', responsavel: null, atrasada: false, bloqueio: null,
    ...esqueleto,
    id: proximoId('sim'),
    mensagens: [],
    ultimaEm: new Date(agora).toISOString(),
  }
  conversa.cadastroId = conversa.cadastroId ?? conversa.id
  return conversa
}

// Resolve os passos do cenário em ações prontas para despachar: cada uma já
// carrega `tipo` (a constante de ação) e `payload`. Puro o bastante (só lê
// `agora` de fora, nunca o relógio de verdade), então dá para montar tudo de
// uma vez e deixar só o `setTimeout` para o hook.
export function montarRoteiro(cenarioId, agora) {
  const cenario = cenarioPorId(cenarioId)
  if (!cenario) return []

  let idAtual = null
  let categoriaAtual = null
  let ultimaFala = null

  return cenario.roteiro.map((passo) => {
    if (passo.tipo === 'conversa') {
      const construir = passo.variacao ? VARIACOES_VOLUME[passo.variacao] : ESQUELETOS[cenario.id]
      const conversa = conversaDoEsqueleto(construir?.(agora) ?? {}, agora)
      idAtual = conversa.id
      categoriaAtual = passo.variacao ?? null
      return { aposMs: passo.aposMs, tipo: acao.SIMULAR_CONVERSA, payload: { conversa } }
    }

    if (passo.tipo === 'mensagem') {
      const fala = falaDoPasso(cenario, passo, categoriaAtual)
      ultimaFala = fala.texto
      return {
        aposMs: passo.aposMs,
        tipo: acao.SIMULAR_MENSAGEM,
        payload: {
          id: idAtual, texto: fala.texto, agora, mensagemId: proximoId('msg'),
          comprovante: Boolean(fala.comprovante), foto: Boolean(fala.foto),
          endereco: fala.endereco ?? null,
          // Rodada 7 · frente Anexos: áudio recebido (fala do dono 24/09).
          audio: Boolean(fala.audio), duracaoMs: fala.duracaoMs ?? null,
          // Rodada 11 (registro 92): a fala em que o cliente escolhe o prato
          // reserva o número do pedido que o automático anota. Só ela, para o
          // contador de pedidos não pular número a cada fala simulada.
          numeroPedido: fala.reservaPedido ? proximoNumeroPedido() : null,
        },
      }
    }

    if (passo.tipo === 'pagamento') {
      return {
        aposMs: passo.aposMs,
        tipo: acao.SIMULAR_PAGAMENTO,
        payload: { id: idAtual, agora, mensagemId: proximoId('msg'), valorForcado: passo.valorForcado ?? null },
      }
    }

    if (passo.tipo === 'relogio') {
      const incrementoMs = passo.paraHora != null
        ? deslocamentoParaHora(agora, passo.paraHora)
        : (passo.incrementoMinutos ?? 0) * 60000
      return {
        aposMs: passo.aposMs,
        tipo: acao.DESLOCAR_RELOGIO,
        payload: { incrementoMs, lojaSegueHorario: Boolean(passo.lojaSegueHorario) },
      }
    }

    // Rodada 8 · cenário "Internet cai" (US-042, D6, UC-04 E1, decisão 70):
    // só troca `conexao.online`, sem molde de conversa nem fala — o
    // reducer (`casos/lote.js`) tira a foto de quem está aberto sozinho.
    if (passo.tipo === 'conexao') {
      return {
        aposMs: passo.aposMs,
        tipo: passo.online ? acao.CONEXAO_VOLTOU : acao.CONEXAO_CAIU,
        payload: { agora },
      }
    }

    // Integração da rodada 5: a reclamação do Simular abre a ocorrência de
    // verdade da frente Reclamação (RN-35, UC-06), com o que o cliente disse
    // como relato. Antes gravava `pedido.reclamacao`, campo que ninguém lê mais.
    if (passo.tipo === 'reclamacao') {
      return {
        aposMs: passo.aposMs,
        tipo: acao.ABRIR_OCORRENCIA,
        payload: { id: idAtual, agora, ocorrenciaId: proximoId('ocorrencia'), relatoCliente: ultimaFala },
      }
    }

    return null
  }).filter(Boolean)
}

export function criarAcoesSimulacao(despachar) {
  return {
    montarRoteiro,
    // `agoraAtual`, quando vem, substitui o `agora` congelado na montagem do
    // roteiro: um passo de relógio (DESLOCAR_RELOGIO) no MEIO do cenário só
    // vale para os passos de depois se eles lerem a hora de verdade na hora
    // de disparar, não a hora de quando o botão foi clicado (é o caso de
    // "Pedido para outro dia": sem isso a mensagem chega classificada com o
    // horário de ANTES do relógio pular para as 2h).
    executarPassoSimulado: (passo, agoraAtual = null) => despachar({
      tipo: passo.tipo, ...passo.payload, ...(agoraAtual != null && 'agora' in passo.payload ? { agora: agoraAtual } : {}),
    }),
    deslocarRelogioSimulado: (minutos) => despachar({ tipo: acao.DESLOCAR_RELOGIO, incrementoMs: minutos * 60000 }),
    zerarRelogioSimulado: () => despachar({ tipo: acao.DESLOCAR_RELOGIO, zerar: true }),
    limparSimulacoes: () => despachar({ tipo: acao.LIMPAR_SIMULACOES }),
    // Rodada 10 (registro 79): o cliente simulado que reage às ações da
    // Thatiane. `aplicacao/useReacaoClienteSimulado.js` monta o payload
    // (texto, item, marcador) e só chama isto; a decisão de QUANDO reagir
    // fica inteira no observador, não aqui.
    reagirComoClienteSimulado: (payload) => despachar({ tipo: acao.SIMULAR_EVENTO_CLIENTE, ...payload }),
    // Rodada 11 (registro 92): a resposta "digitando" sai de verdade.
    // `aplicacao/useDigitacaoAutomatica.js` decide QUANDO; o caso decide se
    // ainda pode sair (automático parado descarta).
    entregarRespostaAutomatica: (id, agora) => despachar({ tipo: acao.ENTREGAR_RESPOSTA_AUTOMATICA, id, agora }),
  }
}
