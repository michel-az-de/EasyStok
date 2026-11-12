// Só dados materiais: reler a API, imprimir ou marcar uma mensagem como lida
// não muda o assunto. Uma transcrição, item, pagamento ou mensagem nova muda.
export function revisaoDoContexto(conversa) {
  const p = conversa?.pedido
  return JSON.stringify([
    conversa?.clienteId ?? null, conversa?.vinculoRevisao ?? 0, conversa?.estado, conversa?.assumidaPorUsuarioId,
    (conversa?.mensagens ?? []).slice(-20).map(m => [m.id, m.dir, m.texto, m.transcricao, m.assunto]),
    p ? [p.pedidoId, p.estado, p.statusApi, p.janela, p.meio, p.frete, p.totalApi, p.totalPagoApi, p.alocadoApi,
      p.observacoes, p.trocoPara, p.cobranca?.statusApi, p.cobranca?.valor,
      (p.itens ?? []).map(i => [i.sku, i.qtd, i.obs, i.precoUnitario])] : null,
  ])
}

export function contextoDaSugestao(conversa, identidade) {
  const ultima = conversa?.mensagens?.filter(m => m.dir === 'in' || m.dir === 'out').at(-1)
  if (!identidade || !conversa?.id || conversa.estado === 'Encerrado' || ultima?.dir !== 'in') return null
  return { conversaId: conversa.id, identidade, chave: JSON.stringify([identidade, conversa.id, ultima.id, revisaoDoContexto(conversa)]) }
}
