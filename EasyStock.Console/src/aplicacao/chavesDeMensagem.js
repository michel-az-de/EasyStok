import { criarChaveDeEnvio } from './chaveDeEnvio'

// #1786: Idempotency-Key do envio de mensagem (texto, foto, áudio), uma por conversa. O mesmo conteúdo enviado de
// novo antes da confirmação (resposta perdida, a atendente toca em Enviar outra vez) leva a mesma chave e a API
// devolve a mensagem já gravada, sem mandar outra ao cliente. Confirmado, o próximo envio igual é outra mensagem.
export function criarChavesDeMensagem(gerarChave) {
  const porConversa = new Map()
  const daConversa = (id) => {
    if (!porConversa.has(id)) porConversa.set(id, criarChaveDeEnvio(gerarChave))
    return porConversa.get(id)
  }
  return {
    enviar: async (id, conteudo, enviar) => {
      const envio = daConversa(id)
      const chave = envio.para(conteudo)
      const resposta = await enviar(chave)
      envio.confirmar(chave)
      return resposta
    },
  }
}
