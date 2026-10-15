export const moduloDaRota = (rota) => rota.modulo
  ?? ({ cozinha: 'cozinha', entregas: 'entregas', 'cardapio-link': 'atendimento', principal: 'atendimento' }[rota.tipo] ?? null)

// Lista ausente/avariada não libera nada. No modo API só o servidor decide a matriz.
export const permiteModulo = (acesso, id) => acesso?.modulos?.some((m) => m.id === id && m.liberado === true) === true

export const permiteRota = (acesso, rota) => !moduloDaRota(rota) || permiteModulo(acesso, moduloDaRota(rota))

// #1761: rotas que o Console lia para todo perfil e voltavam 403 a cada carga. A matriz de ações
// do servidor já diz quem pode: avisos do cliente são do administrador (`editarAtendimento`); os
// itens tirados do cardápio são da gerência do atendimento, a mesma regra de `controlarLoja`.
export const podeVerAvisosDoCliente = (acoes) => acoes?.editarAtendimento === true
export const podeVerItensFora = (acoes) => acoes?.controlarLoja === true

export const entradaDoPerfil = (acesso) => permiteModulo(acesso, acesso?.portaDeEntrada)
  ? `#/m/${acesso.portaDeEntrada}` : '#/'

// #1582: a matriz é relida a cada token novo (tablet da cozinha, sessão que se renova). Releitura
// que falha não derruba a tela: vale a matriz já carregada. Só a primeira carga mostra o erro.
export const aposFalhaDeLeitura = (atual, mensagem) => (atual?.dados ? atual : { dados: null, erro: mensagem })
