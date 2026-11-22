import { chamarApi } from './cliente'

export function inferirCadastroDaConversa(conversaId, { sinal, identidadeEsperada } = {}) {
  return chamarApi('/api/atendimento/assistente/cadastro', {
    metodo: 'POST', corpo: { conversaId }, sinal, identidadeEsperada,
  })
}

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

// #2853: painel do Assistente na Gestão. Uso do mês contra a cota, o fluxo do turno (etapas, roteiro,
// ferramentas e prompt) e o histórico de turnos. Leitura de Operador; mudar é pelo PUT da configuração (Admin).
const PAINEL = '/api/atendimento/assistente'
export const obterUsoAssistente = (sinal) => chamarApi(`${PAINEL}/uso`, { sinal, prazoComCancelamento: true })
export const obterFluxoAssistente = (sinal) => chamarApi(`${PAINEL}/fluxo`, { sinal, prazoComCancelamento: true })
export const listarHistoricoAssistente = ({ pagina = 1, tamanhoPagina = 20, resultado = null, conversaId = null } = {}, sinal) => {
  const busca = new URLSearchParams({ pagina: String(pagina), tamanhoPagina: String(tamanhoPagina) })
  if (resultado) busca.set('resultado', resultado)
  if (conversaId) busca.set('conversaId', conversaId)
  return chamarApi(`${PAINEL}/historico?${busca}`, { sinal, prazoComCancelamento: true })
}
