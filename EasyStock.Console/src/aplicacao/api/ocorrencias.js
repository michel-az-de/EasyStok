import { apurarOcorrencia, resolverOcorrencia } from '../../infra/api/ocorrenciasApi'

export function criarAcoesOcorrenciasApi() {
  return {
    apurarOcorrencia: (_conversaId, ocorrenciaId) => apurarOcorrencia(ocorrenciaId),
    encerrarOcorrenciaSemEstorno: (_conversaId, resolucao, ocorrenciaId) =>
      resolverOcorrencia(ocorrenciaId, { resolucao, reembolsar: false }),
  }
}
