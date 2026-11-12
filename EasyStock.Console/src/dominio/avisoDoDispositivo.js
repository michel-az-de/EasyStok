// #2393: avisos que a dona precisa ver fora do ModalAutomacoes: som bloqueado e notificação negada.
export function avisosDoDispositivo({ som, audioBloqueado, permissaoNotificacao }) {
  const avisos = []
  if (som && audioBloqueado) avisos.push({ id: 'som', texto: 'O som está bloqueado pelo navegador. Toque em qualquer parte da tela para ligar.' })
  if (permissaoNotificacao === 'denied') avisos.push({ id: 'notificacao', texto: 'Os avisos do navegador estão bloqueados. Clique no cadeado ao lado do endereço, permita notificações e recarregue a página.' })
  return avisos
}
