// Linha da conversa no Balcão (#1442). Homologação de 07/10: o cartão de três
// linhas com pílula, contagem escrita e contexto empilhado ficou "confuso" ao
// lado do Atendimento Omnichannel do ERP Lumina. A linha nova diz em uma
// palavra QUEM está com a conversa, mostra quantas mensagens esperam e deixa o
// contexto (pedido, entrega, janela, tag) numa linha miúda só quando existe.
//
// Puro: o motivo de precisar e o passo do pedido chegam já calculados, para a
// regra deles continuar morando em automatico.js e esteira.js.

import { PAUSA_POR_OUTRO } from './automatico'
import { mensagensSemRespostaReal } from './mensagem'

const ESTADOS = {
  bloqueado: { chave: 'bloqueado', texto: 'Bloqueado', tom: 'perigo', icone: 'lock' },
  encerrada: { chave: 'encerrada', texto: 'Encerrada', tom: 'neutro', icone: 'log-out' },
  outro: { chave: 'outro', texto: 'Outro atendente', tom: 'neutro', icone: null },
  voce: { chave: 'voce', texto: 'Com você', tom: 'ok', icone: 'hand' },
  automatico: { chave: 'automatico', texto: 'Automático', tom: 'neutro', icone: 'raio' },
}

// Um estado por linha, por precedência: bloqueio e encerramento calam o resto;
// o motivo de precisar (esperando, reclamação, cobrança...) vence a pausa
// porque diz o que fazer; depois quem conduz. `grupo` é o recolhido do fim de
// "Todas" de onde a linha veio. `motivo` é o de `motivoDePrecisar`, com
// `rotulo` já trocado pelo texto da pílula quando a tela quiser.
export function estadoDaLinha(conversa, { pausado = false, grupo = null, motivo = null } = {}) {
  if (grupo === 'bloqueados' || conversa?.bloqueio) return ESTADOS.bloqueado
  if (grupo === 'encerradas' || conversa?.estado === 'Encerrado') return ESTADOS.encerrada
  if (motivo) {
    return {
      chave: motivo.chave, texto: motivo.rotulo, tom: motivo.tom ?? 'aviso', icone: motivo.icone ?? null,
      titulo: motivo.texto ?? null,
    }
  }
  if (pausado === PAUSA_POR_OUTRO) return ESTADOS.outro
  if (pausado || (conversa?.passagem && !conversa.passagem.assumida)) return ESTADOS.voce
  return ESTADOS.automatico
}

// Número da linha: mensagens do cliente depois da última resposta de verdade
// (aviso de esteira e nota não respondem ninguém, issue #41). Sem mensagens
// carregadas, como a conversa encerrada no modo API, vale o `naoLidas` da API.
export function pendentesDaLinha(conversa) {
  if ((conversa?.mensagens ?? []).length > 0) return mensagensSemRespostaReal(conversa).length
  return Number.isFinite(conversa?.naoLidas) ? conversa.naoLidas : 0
}

// Contexto miúdo embaixo da prévia, na ordem em que ela decide: a origem do
// "Passou para você" (quem, quando e por quê, rodada 12), o passo do pedido, até quando entregar, se a janela de 24 h fechou (aí a hora da
// entrega sai, porque nada sai livre mesmo) e a tag que achou a busca. Lista
// vazia quando não há nada: a linha fica em duas.
export function contextoDaLinha({
  origem = null, pedido = null, entregaAte = null, janelaFechada = false, tagAchada = null,
} = {}) {
  const itens = []
  if (origem) itens.push({ chave: 'origem', texto: origem, icone: null, tom: 'aviso' })
  if (pedido) itens.push({ chave: 'pedido', ...pedido })
  if (janelaFechada) itens.push({ chave: 'janela', texto: 'Só modelo', icone: 'lock', tom: 'neutro' })
  else if (entregaAte) itens.push({ chave: 'entrega', texto: `entrega até ${entregaAte}`, icone: 'moto', tom: 'neutro' })
  if (tagAchada) itens.push({ chave: 'tag', texto: `Tag: ${tagAchada}`, icone: null, tom: 'neutro' })
  return itens
}
