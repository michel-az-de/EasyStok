// #2734: gravar nota, tag, endereço, telefone ou o cadastro de uma pessoa avisa quem mostra essa pessoa em outro
// lugar. O editor de Cadastros também abre pela Buscar na Casa, fora do AtendimentoProvider: o aviso é o caminho
// para a Ficha do Atendimento reler a conversa dela sem o editor conhecer o Atendimento.
const ouvintes = new Set()

export function avisarMudancaNoCliente(clienteId) {
  if (!clienteId) return
  ouvintes.forEach((ouvinte) => {
    try { ouvinte(clienteId) } catch { /* ouvinte com defeito não derruba a gravação */ }
  })
}

export function assinarMudancaNoCliente(ouvinte) {
  ouvintes.add(ouvinte)
  return () => { ouvintes.delete(ouvinte) }
}
