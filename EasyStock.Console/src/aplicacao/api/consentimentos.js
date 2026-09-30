import { definirAviso, listarAvisos } from '../../infra/api/consentimentosApi'

// Avisos por E-mail e SMS da Ficha no modo API (F02, S38). Por cliente cadastrado
// (`clienteId`), não por conversa: a polling da inbox não carrega consentimento.
export function criarAcoesConsentimentosApi() {
  return {
    carregarAvisos: (clienteId) => listarAvisos(clienteId),
    definirAvisoCliente: (clienteId, aviso, ligado) => definirAviso(clienteId, aviso, ligado),
  }
}
