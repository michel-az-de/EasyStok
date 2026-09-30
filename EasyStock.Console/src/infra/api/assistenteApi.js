import { chamarApi } from './cliente'

// Assistente da dona (S47): `api/atendimento/assistente`. Somente leitura; a resposta volta
// só para o console e nunca vai ao cliente. Sem a chave da Anthropic na API, 503 com mensagem.
export async function perguntarAssistente(pergunta, conversaId = null) {
  const resultado = await chamarApi('/api/atendimento/assistente', {
    metodo: 'POST',
    corpo: { pergunta, conversaId },
  })
  return resultado?.resposta ?? ''
}
