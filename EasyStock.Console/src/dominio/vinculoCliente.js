export function aplicarVinculoConfirmado(conversa, vinculo) {
  if (!Number.isInteger(vinculo?.vinculoRevisao) || vinculo.vinculoRevisao < (conversa.vinculoRevisao ?? 0)) return conversa
  const clienteId = vinculo.clienteId ?? null
  const mudou = clienteId !== conversa.clienteId
  return { ...conversa, clienteId, vinculoRevisao: vinculo.vinculoRevisao, vinculoConfirmado: true, vinculoEmRevisao: !clienteId,
    conta: clienteId ? 'cliente' : 'lead', nome: clienteId ? vinculo.clienteNome || 'Cadastro vinculado' : 'Identidade em revisão', nomeDaDona: false,
    ...(mudou ? { bloqueio: null, cliente: { desde: null, endereco: null, enderecoCapturado: null, pedidos: 0, tags: [], notas: [], fotoUrl: null,
      telefoneCanal: conversa.cliente?.telefoneCanal ?? null } } : {}),
  }
}

// Uma polling que saiu antes do commit não pode religar a identidade retirada.
export function preservarVinculoConfirmado(local, recebido) {
  const revisaoLocal = local.vinculoRevisao ?? 0, revisaoRecebida = recebido.vinculoRevisao ?? 0
  if (revisaoRecebida > revisaoLocal || (revisaoRecebida === revisaoLocal && !local.vinculoConfirmado)) return recebido
  return { ...recebido, clienteId: local.clienteId, vinculoRevisao: revisaoLocal, conta: local.conta, nome: local.nome,
    nomeDaDona: local.nomeDaDona, cliente: local.cliente, bloqueio: local.bloqueio, vinculoConfirmado: local.vinculoConfirmado, vinculoEmRevisao: local.vinculoEmRevisao }
}
