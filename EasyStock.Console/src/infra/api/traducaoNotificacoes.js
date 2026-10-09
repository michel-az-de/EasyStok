import { FONTES, ORIGENS, novoLembrete } from '../../dominio/lembrete'

const instante = (texto) => {
  if (!texto) return Date.now()
  const comFuso = /[zZ]|[+-]\d\d:?\d\d$/.test(texto) ? texto : `${texto}Z`
  const ms = Date.parse(comFuso)
  return Number.isNaN(ms) ? Date.now() : ms
}

// Os automáticos locais já são calculados a partir das conversas.
export const lembreteEhManual = (dto) => dto?.tipo === 'Manual'
export const lembreteDaApi = (dto) => ({
  ...novoLembrete({
    id: `api-lembrete-${dto.id}`,
    titulo: dto.texto,
    quando: instante(dto.venceEm),
    conversaId: dto.conversaId ?? null,
    origem: ORIGENS.MANUAL,
  }),
  fonte: FONTES.LEMBRETE,
  servidorId: dto.id,
  visto: Boolean(dto.vistoEm),
})