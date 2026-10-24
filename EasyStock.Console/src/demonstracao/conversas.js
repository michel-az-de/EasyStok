import { CONVERSAS_SEMENTE } from '../infra/conversasSemente'
import { carregarMassa } from '../infra/massaConversas'
import { iniciarContadorPedido } from '../infra/numeracao'
import { bloqueioDeSemente } from '../dominio/conversa'

// Padrão: a massa de 30 conversas (dados/massa-conversas.json). Com ?massa=0
// na URL volta a semente curta de 5, útil para demonstração rápida.
const querSemente = () => {
  if (typeof window === 'undefined') return false
  return new URL(window.location.href).searchParams.get('massa') === '0'
}

// O campo `bloqueio` é normalizado na entrada: quem traz o campo (ou a etiqueta
// antiga da massa) nasce bloqueada, o resto nasce livre. Assim nenhum
// componente precisa adivinhar o que significa a ausência do campo.
export function carregarConversas() {
  const origem = querSemente() ? CONVERSAS_SEMENTE : carregarMassa()
  iniciarContadorPedido(origem)
  return origem.map((conversa) => ({ ...conversa, bloqueio: bloqueioDeSemente(conversa) }))
}
