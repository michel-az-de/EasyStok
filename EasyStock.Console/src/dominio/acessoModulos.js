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
// #1921 e #1940 (item 1): a atendente recebia 403 a cada carga e a cada minuto. Integrações e o cadastro
// de entrega da vitrine (janelas, bloqueios, zonas, raio) são da policy Admin da API; a matriz manda
// `verIntegracoes` e `ajustarEntrega`. Enquanto o servidor não manda a chave, vale a do administrador
// (`editarAtendimento`, Admin com o atendimento liberado), a mesma de `podeVerAvisosDoCliente`.
export const podeVerIntegracoes = (acoes) => (acoes?.verIntegracoes ?? acoes?.editarAtendimento) === true
export const podeAjustarEntrega = (acoes) => (acoes?.ajustarEntrega ?? acoes?.editarAtendimento) === true
// #1966: `PUT api/atendimento/automacoes/{gatilho}` exige Gerente (PR #1965). É a gerência do atendimento, a mesma
// regra de `controlarLoja`, até o servidor mandar `editarAutomacoes`. Vale antes e depois daquela PR: o Operador
// não liga, não desliga nem edita. Na demonstração a matriz não decide.
export const podeEditarAutomacoes = (acoes) => (acoes?.editarAutomacoes ?? acoes?.controlarLoja) === true
export const automacoesEditaveis = ({ fonteApi, acoes }) => !fonteApi || podeEditarAutomacoes(acoes)
// Os desacertos de estoque (produto vendido sem saldo) exigem a permissão de estoque, que a matriz só
// passa a dizer com `verDesacertosEstoque`. Sem a chave, lê como antes: a recusa segue sem alerta.
export const podeVerDesacertos = (acoes) => acoes?.verDesacertosEstoque !== false

export const entradaDoPerfil = (acesso) => permiteModulo(acesso, acesso?.portaDeEntrada)
  ? `#/m/${acesso.portaDeEntrada}` : '#/'

// #1582: a matriz é relida a cada token novo (tablet da cozinha, sessão que se renova). Releitura
// que falha não derruba a tela: vale a matriz já carregada. Só a primeira carga mostra o erro.
export const aposFalhaDeLeitura = (atual, mensagem) => (atual?.dados ? atual : { dados: null, erro: mensagem })
