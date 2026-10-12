import { definirAviso, listarAvisos } from '../../infra/api/consentimentosApi'

// Avisos por E-mail e SMS da Ficha no modo API (F02, S38). Por cliente cadastrado
// (`clienteId`), não por conversa: a polling da inbox não carrega consentimento.
// F13, item 5 (#1243): a rota é só de Admin; o operador comum lê que a decisão é do administrador.
const SO_ADMINISTRADOR = 'só o administrador da loja vê e muda os avisos do cliente.'

const explicar = (erro) => {
  if (erro?.status === 403) throw new Error(SO_ADMINISTRADOR)
  throw erro
}

export function criarAcoesConsentimentosApi() {
  return {
    carregarAvisos: (clienteId) => listarAvisos(clienteId).catch(explicar),
    definirAvisoCliente: (clienteId, aviso, ligado) => definirAviso(clienteId, aviso, ligado).catch(explicar),
  }
}
