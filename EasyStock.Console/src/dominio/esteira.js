// A esteira do pedido. Passo e mensagem são dados, não código: acrescentar um
// passo novo não exige tocar em componente nenhum.
// Rodada 10: dominio/respostas.js passou a importar PASSOS daqui, e isso
// expôs uma falta de extensão pre-existente neste import (Vite tolera,
// `node teste-*.mjs` puro não) — mesma classe de defeito já visto e
// corrigido na decisão 60 (infra/catalogo.js -> dominio/respostas). Extensão
// .js aqui, sem mudar comportamento nenhum.
import { preencherFaixa } from './entrega.js'

export const PASSOS = [
  { id: 'aguardando', rotulo: 'Aguardando pagamento', tom: 'aviso', mensagem: null },
  {
    id: 'pago', rotulo: 'Pago', tom: 'neutro',
    mensagem: 'Pagamento confirmado, obrigada! Já coloco seu pedido na fila.',
  },
  {
    id: 'preparo', rotulo: 'Em preparo', tom: 'ok',
    mensagem: 'Seu pedido entrou em preparo. Previsão de entrega entre {faixa}.',
  },
  {
    id: 'embalado', rotulo: 'Embalado', tom: 'ok',
    mensagem: 'Seu pedido está embalado e aguardando o entregador.',
  },
  {
    id: 'entrega', rotulo: 'Em entrega', tom: 'info',
    mensagem: 'Seu pedido saiu para entrega com o {entregador}.',
    // US-040: com motoboy nomeado ou entregador de plataforma o cliente lê o
    // nome (`{entregador}` acima); "eu mesma levo" é outra frase, primeira
    // pessoa da Thatiane, não um nome encaixado no molde de cima.
    mensagemPropria: 'Seu pedido saiu para entrega, eu mesma vou levar até você!',
    // Integração 72 (US-040 x US-042): o lote de papel lança "Em entrega"
    // sem saber quem levou, porque o canhoto não guarda isso. O molde de
    // cima sem o nome, nunca o texto fixo antigo.
    mensagemSemNome: 'Seu pedido saiu para entrega.',
  },
  {
    id: 'entregue', rotulo: 'Entregue', tom: 'info',
    mensagem: 'Pedido entregue. Obrigada pela preferência, boa massa!',
  },
]

export const indiceDoPasso = (id) => PASSOS.findIndex((p) => p.id === id)

export const passoPorId = (id) => PASSOS.find((p) => p.id === id) ?? null

export const ehProximoPasso = (id, atual) => indiceDoPasso(id) === indiceDoPasso(atual) + 1

// Rótulo textual da situação do passo. Cor nunca informa sozinha.
export function situacaoDoPasso(id, atual) {
  const i = indiceDoPasso(id)
  const a = indiceDoPasso(atual)
  if (i < a) return { chave: 'feito', rotulo: 'feito' }
  if (i === a) return { chave: 'atual', rotulo: 'agora' }
  if (i === a + 1) return { chave: 'proximo', rotulo: 'marcar' }
  return { chave: 'futuro', rotulo: 'a seguir' }
}

// Nome que entra no molde da mensagem (US-040): motoboy nomeado por ela ou
// entregador que o chamado simulado achou usam o mesmo nome que já aparecem
// em `features/entregas/PainelViagem.jsx`. Sem nome resolvido, `null`: quem
// chama decide (não manda aviso nenhum em vez de inventar um).
function nomeParaMensagem(entregador) {
  if (entregador?.tipo === 'motoboy' || entregador?.tipo === 'plataforma') {
    return entregador.nome || null
  }
  return null
}

export function avisoDoPasso(id, contexto) {
  const passo = passoPorId(id)
  if (!passo || !passo.mensagem) return null
  if (passo.mensagemPropria && contexto.entregador?.tipo === 'propria') {
    return passo.mensagemPropria
  }
  const precisaDeNome = passo.mensagem.includes('{entregador}')
  const nome = nomeParaMensagem(contexto.entregador)
  // Sem entregador resolvido, nenhum aviso automático sai: o texto fixo
  // "motoboy da casa" mentia sempre, e um "{entregador}" sem preencher
  // mentiria do mesmo jeito (US-040, achado 67).
  if (precisaDeNome && !nome) return null
  return preencherFaixa(passo.mensagem, contexto.faixa ?? 'a combinar')
    .replace('{entregador}', nome ?? '')
}

export const passoAnterior = (atual) => PASSOS[indiceDoPasso(atual) - 1]?.id ?? null

export const proximoPasso = (atual) => PASSOS[indiceDoPasso(atual) + 1] ?? null

// RN-14 / US-019: cliente bloqueado não é atendido, então o pedido dele não
// entra em preparo, não é embalado e não sai da casa. "Entregue" passa de
// propósito: registra um fato físico de pedido que já saiu, e recusar só
// deixaria a esteira mentindo sobre onde o pedido está. `bloqueado` chega
// de fora (`dominio/conversa.js: estaBloqueada`): importar conversa.js aqui
// fecharia o ciclo esteira -> conversa -> automatico -> cozinha -> esteira.
const PASSOS_TRAVADOS_POR_BLOQUEIO = new Set(['preparo', 'embalado', 'entrega'])

export const MOTIVO_CLIENTE_BLOQUEADO = 'Cliente bloqueado: desbloqueie para seguir com o pedido.'

// `null` quando pode avançar; senão, o texto que a tela mostra junto do
// botão desabilitado. O reducer usa a mesma resposta para recusar.
export const motivoParaNaoAvancar = (passo, { bloqueado }) =>
  (bloqueado && PASSOS_TRAVADOS_POR_BLOQUEIO.has(passo) ? MOTIVO_CLIENTE_BLOQUEADO : null)

// Segundos em que dá para voltar atrás depois de marcar. Vem do padrão de
// desfazer envio: a pessoa percebe o engano nos primeiros segundos ou não percebe.
export const SEGUNDOS_PARA_DESFAZER = 10
