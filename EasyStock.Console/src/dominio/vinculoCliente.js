// #2461: quem é a pessoa para a loja. 'lead' é quem tem cadastro e nenhum pedido; a regra do que conta como
// pedido é da API (`temPedido`, cancelado conta). Sem `temPedido` (API anterior ao campo), vale
// o de antes: cadastro é cliente. Não é o `conta === 'lead'`, que segue querendo dizer sem cadastro.
export const relacionamentoDaApi = (clienteId, temPedido) => !clienteId ? 'sem-cadastro' : temPedido === false ? 'lead' : 'cliente'

// #2461: o cadastro criado agora ainda não tem pedido, então já nasce Lead. O telefone que era de outro cadastro
// (`novo === false`) pode ter pedido: sem `temPedido`, quem responde é a próxima leitura da lista.
export const vinculoDoCadastro = (salvo) => ({ clienteId: salvo.clienteId, clienteNome: salvo.nome, vinculoRevisao: salvo.vinculoRevisao,
  ...(salvo.novo === true ? { temPedido: false } : {}) })

export function aplicarVinculoConfirmado(conversa, vinculo) {
  if (!Number.isInteger(vinculo?.vinculoRevisao) || vinculo.vinculoRevisao < (conversa.vinculoRevisao ?? 0)) return conversa
  const clienteId = vinculo.clienteId ?? null
  const mudou = clienteId !== conversa.clienteId
  return { ...conversa, clienteId, vinculoRevisao: vinculo.vinculoRevisao, vinculoConfirmado: true, vinculoEmRevisao: !clienteId,
    conta: clienteId ? 'cliente' : 'lead',
    // Resposta sem `temPedido` para o mesmo cadastro (salvar dados) não desfaz o que a lista já informou.
    relacionamento: !mudou && vinculo.temPedido == null && conversa.relacionamento ? conversa.relacionamento : relacionamentoDaApi(clienteId, vinculo.temPedido),
    nome: clienteId ? vinculo.clienteNome || 'Cadastro vinculado' : 'Identidade em revisão', nomeDaDona: false,
    ...(mudou ? { bloqueio: null, cliente: { desde: null, endereco: null, enderecoCapturado: null, pedidos: 0, tags: [], notas: [], fotoUrl: null,
      telefoneCanal: conversa.cliente?.telefoneCanal ?? null } } : {}),
  }
}

// Uma polling que saiu antes do commit não pode religar a identidade retirada.
export function preservarVinculoConfirmado(local, recebido) {
  const revisaoLocal = local.vinculoRevisao ?? 0, revisaoRecebida = recebido.vinculoRevisao ?? 0
  if (revisaoRecebida > revisaoLocal || (revisaoRecebida === revisaoLocal && !local.vinculoConfirmado)) return recebido
  return { ...recebido, clienteId: local.clienteId, vinculoRevisao: revisaoLocal, conta: local.conta, nome: local.nome,
    // #2461: a leitura da mesma revisão fala do mesmo cadastro e traz o primeiro pedido; a atrasada fala do anterior.
    relacionamento: revisaoRecebida === revisaoLocal ? recebido.relacionamento : local.relacionamento,
    nomeDaDona: local.nomeDaDona, cliente: local.cliente, bloqueio: local.bloqueio, vinculoConfirmado: local.vinculoConfirmado, vinculoEmRevisao: local.vinculoEmRevisao }
}
