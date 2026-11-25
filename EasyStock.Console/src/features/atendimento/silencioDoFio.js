// #3330 (F21): o fio inteiro que entra de uma vez numa região viva (`aria-live`) é lido de ponta a ponta pelo leitor
// de tela (964 caracteres medidos). O fio fica calado ao abrir a conversa e volta a anunciar meio segundo depois,
// para a mensagem nova seguir anunciada. No modo API a conversa pode abrir sem mensagens e o histórico chegar numa
// volta seguinte do ciclo: um fio que abriu vazio e ganha mais de uma mensagem de uma vez cala de novo. Uma só é
// mensagem ao vivo e segue anunciada; `vazio` cai para `false` depois dela, e o resto também segue.
export const MS_DE_SILENCIO_DO_FIO = 500

export const novoSilencio = (chave, total, rodada = 0) => ({ chave, vazio: total === 0, calado: true, rodada })

export function proximoSilencio(silencio, chave, total) {
  if (silencio.chave !== chave || (silencio.vazio && total > 1)) return novoSilencio(chave, total, silencio.rodada + 1)
  if (silencio.vazio && total === 1) return { ...silencio, vazio: false }
  return silencio
}
