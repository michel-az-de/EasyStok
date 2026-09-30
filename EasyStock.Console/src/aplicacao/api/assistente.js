import { perguntarAssistente } from '../../infra/api/assistenteApi'

// Assistente da dona no modo API (F02, S47): a API monta o contexto da conversa pelo id,
// então o histórico local não vai junto. O erro sobe para o balão, que mostra a mensagem.
export function criarAcoesAssistenteApi() {
  return {
    perguntarAssistente: (pergunta, conversa) => perguntarAssistente(pergunta, conversa?.id ?? null),
  }
}
