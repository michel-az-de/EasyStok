// Prazo de cada chamada à API (#1565, #2379). Leitura sem prazo ficava pendurada em rede ruim; escrita
// também: numa conexão meio-aberta a tela ficava em "Enviando" para sempre.
//
// Sem prazo: o que pode demorar (arquivo, upload multipart), o que quem chama já cancela por conta
// própria (`sinal`) e a escrita com `keepalive`, que existe para sobreviver à aba fechando.
export const PRAZO_DE_LEITURA_MS = 20000
export const PRAZO_DE_ESCRITA_MS = 30000

export const MENSAGEM_LEITURA_SEM_RESPOSTA = 'O EasyStok demorou a responder.'
// A escrita pode ter sido gravada: a dona confere antes de mandar de novo.
export const MENSAGEM_ESCRITA_SEM_RESPOSTA = 'Não houve resposta. Confira antes de repetir.'

export function prazoDaChamada({ metodo = 'GET', arquivo = false, keepalive = false, formulario = null, sinal = null, prazoComCancelamento = false }) {
  if (arquivo || keepalive || formulario || (sinal && !prazoComCancelamento)) return null
  return metodo === 'GET' ? PRAZO_DE_LEITURA_MS : PRAZO_DE_ESCRITA_MS
}
