export const moduloDaRota = (rota) => rota.modulo
  ?? ({ cozinha: 'cozinha', entregas: 'entregas', 'cardapio-link': 'atendimento', principal: 'atendimento' }[rota.tipo] ?? null)

// Lista ausente/avariada não libera nada. No modo API só o servidor decide a matriz.
export const permiteModulo = (acesso, id) => acesso?.modulos?.some((m) => m.id === id && m.liberado === true) === true

export const permiteRota = (acesso, rota) => !moduloDaRota(rota) || permiteModulo(acesso, moduloDaRota(rota))

export const entradaDoPerfil = (acesso) => permiteModulo(acesso, acesso?.portaDeEntrada)
  ? `#/m/${acesso.portaDeEntrada}` : '#/'
