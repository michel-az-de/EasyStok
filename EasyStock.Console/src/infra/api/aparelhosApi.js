import { chamarApi } from './cliente'

// #2081 (A6): pareamento do aparelho da produção (o APK piloto). A API gera um código de 6 dígitos que
// vale 10 minutos (DevicePairingController, policy Gerente). A resposta não vem no envelope `{ data }`.
// O código só passa pelo corpo da resposta: nunca na URL nem no log.
export const gerarCodigoDePareamento = ({ empresaId, lojaId }) =>
  chamarApi('/api/mobile/devices/pair-codes', { metodo: 'POST', corpo: { empresaId, lojaId }, envelope: true })

// As lojas ativas da empresa. A rota é de Configurações: a dona lê; quem não tem o módulo recebe 403.
export const listarLojas = () => chamarApi('/api/lojas')

// Os aparelhos já pareados (Gerente). Também fora do envelope.
export const listarAparelhos = (empresaId) =>
  chamarApi(`/api/mobile/devices?empresaId=${encodeURIComponent(empresaId)}`, { envelope: true })
