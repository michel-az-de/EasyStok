// Substituição de variável de modelo aprovado. Sem React e sem catálogo fixo.
export function preencherModelo(modelo, valores) {
  return modelo.texto.replace(/\{\{(\d+)\}\}/g, (todo, indice) => valores[Number(indice) - 1] ?? todo)
}

export const primeiroNome = (nomeCompleto) => nomeCompleto.split(' ')[0]

// --- Pergunta fora do roteiro nunca fica muda -------------------------------
// Achado P2.6 (rodada 10, `aplicacao/casos/simulacao.js`): "D3 pede
// interpretar, não ramificar". Quando nem resposta automática nem passagem
// para a Thatiane saem do classificador (US-003, US-008), o mínimo é isto
// aqui: sinalizar que uma pergunta ficou sem resposta, em vez de deixar o
// cliente calado até o próximo aviso de esteira (US-009 trata a resposta
// pronta em si, biblioteca de respostas). Rodada 13 (issue #41):
// `dominio/automatico.js` (Precisa de você) reaproveita o mesmo heurístico
// para não inventar uma segunda conta do que "parece pergunta".
export const ehPerguntaSemResposta = (texto) => /\?\s*$/.test((texto ?? '').trim())

export const MOTIVO_PERGUNTA_FORA_DO_ROTEIRO = 'Pergunta fora do roteiro automático, sem resposta pronta.'

// --- Resposta real vs aviso transacional (issue #41, RN-05/RN-32) -----------
// Aviso de esteira (dominio/reducer AVANCAR_ESTEIRA, regra `esteira-<passo>`)
// sai sozinho a cada passo (preparo, embalado, entrega, entregue), mesmo com
// o automático pausado (RN-05) e mesmo que ninguém tenha respondido a
// pergunta do cliente: é status da produção, não conversa com ele. Nota de
// sistema (`dir: 'sistema'`, cadastro criado, e-mail/SMS simulado etc.)
// também não fala com o cliente. Nenhum dos dois pode contar como "alguém
// respondeu": antes da correção, o aviso de esteira zerava o "sem resposta"
// de perguntas de verdade só porque virou a última mensagem da lista
// (achado da varredura R13, issue #41).
const ehAvisoDeEsteira = (mensagem) => Boolean(mensagem?.automatica) && (mensagem?.regra ?? '').startsWith('esteira-')

// Resposta que conversa de verdade com o cliente: a dona escrevendo, ou o
// automático respondendo alguma coisa que não seja aviso de esteira (FAQ,
// captura de lead, agradecimento pós-entrega...). Nota de sistema não conta.
export const respondeAoCliente = (mensagem) => mensagem.dir === 'out' && !['falhou', 'enviando'].includes(mensagem.status) && !ehAvisoDeEsteira(mensagem)

// Mensagens do cliente ainda sem essa resposta, na ordem em que chegaram.
// Anda de trás para frente: aviso de esteira e nota de sistema são
// transparentes (nem contam como pendência, nem zeram uma que já existia),
// e a busca para no primeiro "out" que conversa de verdade. Uma função só
// alimenta o contador de "Precisa de você" (dominio/automatico.js), o selo
// "N mensagens sem resposta" (features/caixa-de-entrada/CartaoConversa.jsx)
// e o lembrete automático de responder (dominio/lembrete.js), para as três
// contas baterem entre si.
export function mensagensSemRespostaReal(conversa) {
  const mensagens = conversa?.mensagens ?? []
  const pendentes = []
  for (let i = mensagens.length - 1; i >= 0; i -= 1) {
    const mensagem = mensagens[i]
    if (respondeAoCliente(mensagem)) break
    if (mensagem.dir === 'in') pendentes.unshift(mensagem)
  }
  return pendentes
}
