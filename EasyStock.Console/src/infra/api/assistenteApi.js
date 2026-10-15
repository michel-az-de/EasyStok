import { chamarApi } from './cliente'

// Assistente da dona (S47): `api/atendimento/assistente`. A resposta volta só para o console e
// nunca vai ao cliente. Com conversa, traz também as ações que o modelo propôs (#1445:
// `{ tipo, texto, tela }`), que só rodam no clique da atendente. Sem a chave da Anthropic na API,
// 503 com mensagem.
export async function perguntarAssistente(pergunta, conversaId = null, { sinal, identidadeEsperada } = {}) {
  const resultado = await chamarApi('/api/atendimento/assistente', {
    metodo: 'POST',
    corpo: { pergunta, conversaId },
    sinal, identidadeEsperada,
  })
  return { texto: resultado?.resposta ?? '', acoes: resultado?.acoes ?? [] }
}
