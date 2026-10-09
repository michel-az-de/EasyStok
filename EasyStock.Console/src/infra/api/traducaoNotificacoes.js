import { FONTES, ORIGENS, novoLembrete } from '../../dominio/lembrete'

const instante = (texto) => {
  if (!texto) return Date.now()
  const comFuso = /[zZ]|[+-]\d\d:?\d\d$/.test(texto) ? texto : `${texto}Z`
  const ms = Date.parse(comFuso)
  return Number.isNaN(ms) ? Date.now() : ms
}

// No modo API, inclusive os automáticos vêm do avaliador persistido.
export const lembreteDaApi = (dto) => ({
  ...novoLembrete({
    id: `api-lembrete-${dto.id}`,
    titulo: dto.texto,
    quando: instante(dto.venceEm),
    conversaId: dto.conversaId ?? null,
    origem: dto.tipo === 'ClienteSemResposta' ? ORIGENS.PASSAGEM
      : dto.tipo === 'PagamentoSemBaixa' ? ORIGENS.PAGAMENTO : ORIGENS.MANUAL,
  }),
  fonte: FONTES.LEMBRETE,
  servidorId: dto.id,
  visto: Boolean(dto.vistoEm),
})