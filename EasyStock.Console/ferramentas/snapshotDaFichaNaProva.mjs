// Estas provas exercitam gravações de editores já abertos. A identidade é capturada ao montar a fixture.
export function comSnapshotDaFicha(acoes, conversas) {
  const snapshots = new Map(conversas.map(c => [c.id, { conversaId: c.id, clienteId: c.clienteId ?? null, vinculoRevisao: c.vinculoRevisao ?? 0 }]))
  return { ...acoes,
    salvarCadastroRapido: (id, dados, agora) => acoes.salvarCadastroRapido(id, dados, agora, snapshots.get(id)),
    editarDadoCliente: (id, campo, valor, agora) => acoes.editarDadoCliente(id, campo, valor, agora, snapshots.get(id)),
    cadastrarEndereco: id => acoes.cadastrarEndereco(id, snapshots.get(id)),
  }
}
