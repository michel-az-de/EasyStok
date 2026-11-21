// Prazo de cada chamada à API (#1565, #2379). Leitura sem prazo ficava pendurada em rede ruim; escrita
// também: numa conexão meio-aberta a tela ficava em "Enviando" para sempre.
//
// Sem prazo: o que pode demorar (arquivo, upload multipart) e o que quem chama já cancela por conta
// própria (`sinal`). A escrita com `keepalive` TEM prazo (#2431): o timer não roda após a aba fechar, então
// o envio pendente continua, e numa conexão meio-aberta a comanda não fica presa em "Enviando".
export const PRAZO_DE_LEITURA_MS = 20000
export const PRAZO_DE_ESCRITA_MS = 30000

// Rota que chama o modelo de linguagem passa de 30 s: pede prazo próprio por `prazoMs` (#2401).
export const PRAZO_DE_IA_MS = 120000

export const MENSAGEM_LEITURA_SEM_RESPOSTA = 'O EasyStok demorou a responder.'
// A escrita pode ter sido gravada: a dona confere antes de mandar de novo.
export const MENSAGEM_ESCRITA_SEM_RESPOSTA = 'Não houve resposta. Confira antes de repetir.'

export function prazoDaChamada({ metodo = 'GET', arquivo = false, keepalive = false, formulario = null, sinal = null, prazoComCancelamento = false, prazoMs = null }) {
  if (arquivo || formulario || (sinal && !prazoComCancelamento)) return null
  if (prazoMs) return prazoMs
  return metodo === 'GET' ? PRAZO_DE_LEITURA_MS : PRAZO_DE_ESCRITA_MS
}
