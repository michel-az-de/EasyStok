// #2393: avisos que a dona precisa ver fora do ModalAutomacoes: som bloqueado e notificação negada.
// #3005: texto curto (a faixa é de uma linha); o passo a passo fica no `detalhe`, que vira dica da faixa.
export function avisosDoDispositivo({ som, audioBloqueado, permissaoNotificacao }) {
  const avisos = []
  if (som && audioBloqueado) avisos.push({ id: 'som', texto: 'Som bloqueado pelo navegador.', detalhe: 'O som está bloqueado pelo navegador. Clique ou toque em qualquer parte da tela para ligar.' })
  if (permissaoNotificacao === 'denied') avisos.push({ id: 'notificacao', texto: 'Avisos bloqueados: use o cadeado ao lado do endereço.', detalhe: 'Os avisos do navegador estão bloqueados. Clique no cadeado ao lado do endereço, permita notificações e recarregue a página.' })
  return avisos
}

// #3005: os dois avisos viram uma linha só. `acao` é o que o botão resolve (só o som se liga por gesto; a
// notificação negada só o navegador destrava, pelo cadeado).
export function resumoDoAviso(avisos) {
  if (avisos.length === 0) return null
  const chave = avisos.map(aviso => aviso.id).join('|')
  const detalhe = avisos.map(aviso => aviso.detalhe).join(' ')
  const comSom = avisos.some(aviso => aviso.id === 'som')
  if (avisos.length > 1) return { chave, texto: 'Som e avisos bloqueados', detalhe, icone: 'bell-off', acao: 'som', rotuloDaAcao: 'Ligar som' }
  const [unico] = avisos
  return { chave, texto: unico.texto, detalhe, icone: comSom ? 'volume-x' : 'bell-off', acao: comSom ? 'som' : null, rotuloDaAcao: comSom ? 'Ligar som' : null }
}
