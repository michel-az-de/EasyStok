import { FONTES, ORIGENS, novoLembrete } from '../../dominio/lembrete'

// Do contrato da API para o item do sininho (#1426). Os dois tipos que vêm do EasyStok carregam
// `fonte` e `servidorId`: o reducer troca cada fonte por inteiro a cada leitura, e o Concluir sabe
// qual rota chamar. O id da tela leva prefixo para não colidir com os locais.

// `DateTime` do .NET pode chegar sem fuso; no banco é sempre UTC.
const instante = (texto) => {
  if (!texto) return Date.now()
  const comFuso = /[zZ]|[+-]\d\d:?\d\d$/.test(texto) ? texto : `${texto}Z`
  const ms = Date.parse(comFuso)
  return Number.isNaN(ms) ? Date.now() : ms
}

// Só o manual entra: os automáticos do servidor (pagamento sem baixa, cliente sem resposta) já
// aparecem como automáticos locais, calculados do mesmo estado das conversas.
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
})

// Notificação InApp: título quando houver, a mensagem vira o detalhe.
export const avisoDaApi = (dto) => ({
  ...novoLembrete({
    id: `api-aviso-${dto.id}`,
    titulo: dto.titulo || dto.mensagem || 'Aviso do EasyStok',
    detalhe: dto.titulo && dto.mensagem && dto.mensagem !== dto.titulo ? dto.mensagem : null,
    quando: instante(dto.createdAt),
    origem: ORIGENS.AVISO,
  }),
  fonte: FONTES.AVISO,
  servidorId: dto.id,
})
