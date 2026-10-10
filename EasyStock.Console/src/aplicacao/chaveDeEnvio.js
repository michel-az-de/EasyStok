// Idempotency-Key que vale para o mesmo corpo por pouco tempo, até a API confirmar (#1540). Repetir
// logo depois de uma queda de rede devolve o que já foi gravado em vez de gravar de novo; corpo
// diferente ou envio confirmado ganha chave nova. A API só guarda resposta 2xx, então erro de regra
// pode ser reenviado.
//
// A chave vence. Sem prazo, uma resposta perdida deixaria a chave armada: a próxima operação igual,
// horas depois, receberia a resposta guardada e não seria gravada.
export const VALIDADE_DA_CHAVE_MS = 2 * 60 * 1000

export function criarChaveDeEnvio(gerarChave = () => crypto.randomUUID(), agora = () => Date.now()) {
  let atual = null

  return {
    para: (corpo) => {
      const assinatura = JSON.stringify(corpo)
      const vale = atual?.assinatura === assinatura && agora() - atual.desde <= VALIDADE_DA_CHAVE_MS
      if (!vale) atual = { assinatura, chave: gerarChave(), desde: agora() }
      return atual.chave
    },
    confirmar: (chave) => {
      if (atual?.chave === chave) atual = null
    },
  }
}
