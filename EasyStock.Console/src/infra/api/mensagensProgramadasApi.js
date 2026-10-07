import { chamarApi } from './cliente'

// Mensagem programada ao cliente (S39, #1424): `api/atendimento/mensagens-programadas`.
// Devolve o formato da API (`MensagemProgramadaResult`); a tradução mora em traducaoConversas.js.
// O disparo roda no EasyStok (MensagensProgramadasBackgroundService); o console só agenda,
// lista e cancela. Janela, consentimento e cliente sem telefone voltam 400 com a mensagem.
const BASE = '/api/atendimento/mensagens-programadas'

// corpo: { clienteId, conversaId?, canal, finalidade, texto?, modelo?: { nome, idioma, parametros }, agendadaPara }
export const agendarMensagem = (corpo) => chamarApi(BASE, { metodo: 'POST', corpo })

// A API devolve as 100 mais recentes do cliente (todas as situações).
export const listarMensagensProgramadas = ({ clienteId } = {}) =>
  chamarApi(clienteId ? `${BASE}?clienteId=${encodeURIComponent(clienteId)}` : BASE)

// Só a agendada cancela; a que já está saindo volta 400.
export const cancelarMensagemProgramada = (id) => chamarApi(`${BASE}/${id}`, { metodo: 'DELETE' })
