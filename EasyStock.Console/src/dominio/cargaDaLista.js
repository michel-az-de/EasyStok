// #1611 e #2227: no modo API a lista de conversas começa vazia até a primeira leitura e pode
// nunca chegar. Zero conversa sem leitura concluída não é "nenhuma conversa": é 'carregando'
// ou 'erro'. Com conversas na tela ou fora da API, nada a avisar (null).
export function cargaDaLista({ fonteApi, estado, total }) {
  if (!fonteApi || estado === 'ok' || total > 0) return null
  return estado === 'erro' ? 'erro' : 'carregando'
}
