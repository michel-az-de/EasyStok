export const identidadeDaEdicao = conversa => ({ conversaId: conversa.id, clienteId: conversa.clienteId ?? null, vinculoRevisao: conversa.vinculoRevisao ?? 0, nome: conversa.nome })
export const mesmaIdentidadeDaEdicao = (identidade, conversa) => Boolean(identidade && conversa
  && identidade.conversaId === conversa.id && Object.hasOwn(identidade, 'clienteId')
  && identidade.clienteId === (conversa.clienteId ?? null) && Number.isInteger(identidade.vinculoRevisao)
  && identidade.vinculoRevisao >= 0 && identidade.vinculoRevisao === (conversa.vinculoRevisao ?? 0))
export const ERRO_IDENTIDADE_DA_EDICAO = 'O vínculo da conversa mudou. Confira o destino da edição antes de salvar. O texto digitado foi preservado.'
